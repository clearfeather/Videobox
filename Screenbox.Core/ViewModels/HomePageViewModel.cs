#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class HomePageViewModel : ObservableRecipient,
    IRecipient<PlaylistCurrentItemChangedMessage>
{
    public ObservableCollection<MediaViewModel> Recent { get; }

    public ObservableCollection<HomeDashboardTile> VideoFolderTiles { get; }

    public ObservableCollection<HomeDashboardTile> FavoriteTiles { get; }

    public ObservableCollection<HomeDashboardTile> TagTiles { get; }

    public ObservableCollection<HomeDashboardTile> PlaylistTiles { get; }

    public bool HasRecentMedia => Recent.Count > 0 && _settingsService.ShowRecent;

    public bool HasVideoFolders => VideoFolderTiles.Count > 0;

    public bool HasFavorites => FavoriteTiles.Count > 0;

    public bool HasTags => TagTiles.Count > 0;

    public bool HasPlaylists => PlaylistTiles.Count > 0;

    public bool HasHomeContent => HasRecentMedia || HasVideoFolders || HasFavorites || HasTags || HasPlaylists;

    private readonly MediaViewModelFactory _mediaFactory;
    private readonly IFilesService _filesService;
    private readonly ISettingsService _settingsService;
    private readonly ILibraryService _libraryService;
    private readonly LibraryContext _libraryContext;
    private readonly FavoritesContext _favoritesContext;
    private readonly IFavoritesService _favoritesService;
    private readonly PlaylistsContext _playlistsContext;
    private readonly IPlaylistService _playlistService;
    private readonly ITagsService _tagsService;
    private readonly INavigationService _navigationService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _changeDebounceTimer;
    private readonly Dictionary<string, string> _pathToMruMappings;

    public HomePageViewModel(MediaViewModelFactory mediaFactory, IFilesService filesService,
        ISettingsService settingsService, ILibraryService libraryService, LibraryContext libraryContext,
        FavoritesContext favoritesContext, IFavoritesService favoritesService, PlaylistsContext playlistsContext,
        IPlaylistService playlistService, ITagsService tagsService, INavigationService navigationService)
    {
        _mediaFactory = mediaFactory;
        _filesService = filesService;
        _settingsService = settingsService;
        _libraryService = libraryService;
        _libraryContext = libraryContext;
        _favoritesContext = favoritesContext;
        _favoritesService = favoritesService;
        _playlistsContext = playlistsContext;
        _playlistService = playlistService;
        _tagsService = tagsService;
        _navigationService = navigationService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _changeDebounceTimer = _dispatcherQueue.CreateTimer();
        _pathToMruMappings = new Dictionary<string, string>();
        Recent = new ObservableCollection<MediaViewModel>();
        VideoFolderTiles = new ObservableCollection<HomeDashboardTile>();
        FavoriteTiles = new ObservableCollection<HomeDashboardTile>();
        TagTiles = new ObservableCollection<HomeDashboardTile>();
        PlaylistTiles = new ObservableCollection<HomeDashboardTile>();

        Recent.CollectionChanged += (_, _) => RaiseHomeSectionProperties();
        VideoFolderTiles.CollectionChanged += (_, _) => RaiseHomeSectionProperties();
        FavoriteTiles.CollectionChanged += (_, _) => RaiseHomeSectionProperties();
        TagTiles.CollectionChanged += (_, _) => RaiseHomeSectionProperties();
        PlaylistTiles.CollectionChanged += (_, _) => RaiseHomeSectionProperties();

        // Activate the view model's messenger
        IsActive = true;
    }

    public void Receive(PlaylistCurrentItemChangedMessage message)
    {
        if (_settingsService.ShowRecent)
        {
            _changeDebounceTimer.Debounce(DebouncedAction, TimeSpan.FromMilliseconds(100));

            async void DebouncedAction()
            {
                await UpdateRecentMediaListAsync(false).ConfigureAwait(false);
            }
        }
    }

    public async void OnLoaded()
    {
        await UpdateContentAsync();
    }

    private async Task UpdateContentAsync()
    {
        // Update recent media
        if (_settingsService.ShowRecent)
        {
            await UpdateRecentMediaListAsync(true);
        }
        else
        {
            lock (Recent)
            {
                Recent.Clear();
            }
        }

        await UpdateDashboardTilesAsync();
    }

    private async Task UpdateRecentMediaListAsync(bool loadMediaDetails)
    {
        // Assume UI Thread
        string[] tokens = StorageApplicationPermissions.MostRecentlyUsedList.Entries
            .OrderByDescending(x => x.Metadata)
            .Select(x => x.Token)
            .Where(t => !string.IsNullOrEmpty(t))
            .Take(_settingsService.RecentLimit)
            .ToArray();

        if (tokens.Length == 0)
        {
            lock (Recent)
            {
                Recent.Clear();
            }
            RaiseHomeSectionProperties();
            return;
        }

        var files = await Task.WhenAll(tokens.Select(ConvertMruTokenToStorageFileAsync));
        var pairs = tokens.Zip(files, (t, f) => (Token: t, File: f)).ToList();
        var pairsToRemove = pairs.Where(p => p.File == null).ToList();

        lock (Recent)
        {
            for (int i = 0; i < tokens.Length; i++)
            {
                var (token, file) = pairs[i];
                if (file == null) continue;
                // TODO: Add support for playing playlist file from home page
                if (file.IsSupportedPlaylist()) continue;
                if (i >= Recent.Count)
                {
                    MediaViewModel media = _mediaFactory.GetSingleton(file);
                    _pathToMruMappings[media.Location] = token;
                    Recent.Add(media);
                }
                else if (Recent[i].Source is StorageFile existing)
                {
                    try
                    {
                        if (!file.IsEqual(existing)) MoveOrInsert(file, token, i);
                    }
                    catch (Exception)
                    {
                        // StorageFile.IsEqual() throws an exception
                        // System.Exception: Element not found. (Exception from HRESULT: 0x80070490)
                        // pass
                    }
                }
            }

            // Remove stale items
            while (Recent.Count > tokens.Length)
            {
                Recent.RemoveAt(Recent.Count - 1);
            }
        }

        foreach (var (token, _) in pairsToRemove)
        {
            try
            {
                StorageApplicationPermissions.MostRecentlyUsedList.Remove(token);
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
        }

        // Load media details for the remaining items
        if (!loadMediaDetails) return;
        IEnumerable<Task> loadingTasks = Recent.Select(x => x.LoadDetailsAsync(_filesService));
        loadingTasks = Recent.Select(x => x.LoadThumbnailAsync()).Concat(loadingTasks);
        await Task.WhenAll(loadingTasks);
        RaiseHomeSectionProperties();
    }

    private async Task UpdateDashboardTilesAsync()
    {
        await Task.WhenAll(UpdateVideoFolderTilesAsync(), UpdateFavoriteTilesAsync(), UpdatePlaylistTilesAsync());
        await UpdateTagTilesAsync();
        RaiseHomeSectionProperties();
    }

    private async Task UpdateVideoFolderTilesAsync()
    {
        _libraryContext.VideoFolders = (await _libraryService.GetVideoLibraryFoldersAsync()).ToList();

        VideoFolderTiles.Clear();
        foreach (StorageFolder folder in _libraryContext.VideoFolders)
        {
            VideoFolderTiles.Add(new HomeDashboardTile
            {
                Title = folder.DisplayName,
                Subtitle = folder.Path,
                Glyph = "\uE8B7",
                Kind = HomeDashboardTileKind.VideoFolder,
                Target = folder
            });
        }
    }

    private async Task UpdateFavoriteTilesAsync()
    {
        if (!_favoritesContext.IsLoaded)
        {
            _favoritesContext.Favorites.Clear();
            foreach (MediaViewModel favorite in await _favoritesService.LoadFavoritesAsync())
            {
                _favoritesContext.Favorites.Add(favorite);
            }

            _favoritesContext.IsLoaded = true;
        }

        FavoriteTiles.Clear();
        if (_favoritesContext.Favorites.Count == 0)
        {
            return;
        }

        FavoriteTiles.Add(new HomeDashboardTile
        {
            Title = "Favorites",
            Subtitle = FormatItemsCount(_favoritesContext.Favorites.Count),
            Glyph = "\uE734",
            Kind = HomeDashboardTileKind.Favorites
        });
    }

    private async Task UpdateTagTilesAsync()
    {
        TagTiles.Clear();
        foreach (string tagName in await _tagsService.LoadTagNamesAsync())
        {
            TagTiles.Add(new HomeDashboardTile
            {
                Title = tagName,
                Subtitle = "Tag",
                Glyph = "\uE8EC",
                Kind = HomeDashboardTileKind.Tag,
                Target = tagName
            });
        }
    }

    private async Task UpdatePlaylistTilesAsync()
    {
        if (_playlistsContext.Playlists.Count == 0)
        {
            try
            {
                IReadOnlyList<PersistentPlaylist> loaded = await _playlistService.ListPlaylistsAsync();
                foreach (PersistentPlaylist playlist in loaded)
                {
                    PlaylistViewModel vm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<PlaylistViewModel>();
                    vm.Load(playlist);
                    _playlistsContext.Playlists.Add(vm);
                }
            }
            catch
            {
                // pass
            }
        }

        PlaylistTiles.Clear();
        foreach (PlaylistViewModel playlist in _playlistsContext.Playlists)
        {
            PlaylistTiles.Add(new HomeDashboardTile
            {
                Title = playlist.Name,
                Subtitle = FormatItemsCount((int)playlist.ItemsCount),
                Glyph = "\uE8FD",
                Kind = HomeDashboardTileKind.Playlist,
                Target = playlist
            });
        }
    }

    [RelayCommand]
    private void OpenDashboardTile(HomeDashboardTile? tile)
    {
        if (tile == null)
        {
            return;
        }

        switch (tile.Kind)
        {
            case HomeDashboardTileKind.VideoFolder when tile.Target is StorageFolder folder:
                _navigationService.Navigate(typeof(FolderViewPageViewModel), new[] { folder });
                break;
            case HomeDashboardTileKind.Favorites:
                _navigationService.Navigate(typeof(FavoritesPageViewModel));
                break;
            case HomeDashboardTileKind.Tag when tile.Target is string tagName:
                _navigationService.Navigate(typeof(TagPageViewModel), tagName);
                break;
            case HomeDashboardTileKind.Playlist when tile.Target is PlaylistViewModel playlist:
                _navigationService.Navigate(typeof(PlaylistDetailsPageViewModel), playlist);
                break;
        }
    }

    private void MoveOrInsert(StorageFile file, string token, int desiredIndex)
    {
        // Find index of the VM of the same file
        // There is no FindIndex method for ObservableCollection :(
        int existingIndex = -1;
        for (int j = desiredIndex + 1; j < Recent.Count; j++)
        {
            if (Recent[j].Source is StorageFile existingFile && file.IsEqual(existingFile))
            {
                existingIndex = j;
                break;
            }
        }

        if (existingIndex == -1)
        {
            MediaViewModel media = _mediaFactory.GetSingleton(file);
            _pathToMruMappings[media.Location] = token;
            Recent.Insert(desiredIndex, media);
        }
        else
        {
            MediaViewModel toInsert = Recent[existingIndex];
            Recent.RemoveAt(existingIndex);
            Recent.Insert(desiredIndex, toInsert);
        }
    }

    [RelayCommand]
    private void Play(MediaViewModel media)
    {
        if (media.IsMediaActive)
        {
            Messenger.Send(new TogglePlayPauseMessage(false));
        }
        else
        {
            Messenger.Send(new PlayMediaMessage(media, false));
        }
    }

    [RelayCommand]
    private void Remove(MediaViewModel media)
    {
        lock (Recent)
        {
            Recent.Remove(media);
            if (_pathToMruMappings.Remove(media.Location, out string token))
            {
                StorageApplicationPermissions.MostRecentlyUsedList.Remove(token);
            }
        }
        RaiseHomeSectionProperties();
    }

    [RelayCommand]
    private async Task AddVideoFolderAsync()
    {
        try
        {
            StorageFolder? folder = await _libraryService.AddVideoLibraryFolderAsync();
            if (folder == null) return;

            _libraryContext.VideoFolders = (await _libraryService.GetVideoLibraryFoldersAsync()).ToList();
            await UpdateDashboardTilesAsync();
            Messenger.Send(new RefreshFolderMessage());
            await _libraryService.FetchVideosAsync(_libraryContext, false);
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToAddFolderNotificationMessage(e.Message));
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        StorageFolder? folder = await _filesService.PickFolderAsync();
        if (folder == null) return;
        IReadOnlyList<IStorageItem> items = await _filesService.GetSupportedItems(folder).GetItemsAsync();
        IStorageFile[] files = items.OfType<IStorageFile>().ToArray();
        if (files.Length == 0) return;
        Messenger.Send(new PlayMediaMessage(files));
    }

    private static async Task<StorageFile?> ConvertMruTokenToStorageFileAsync(string token)
    {
        try
        {
            return await StorageApplicationPermissions.MostRecentlyUsedList.GetFileAsync(token,
                AccessCacheOptions.SuppressAccessTimeUpdate);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.IO.FileNotFoundException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Exception e)
        {
            LogService.Log(e);
            return null;
        }
    }

    private void RaiseHomeSectionProperties()
    {
        OnPropertyChanged(nameof(HasRecentMedia));
        OnPropertyChanged(nameof(HasVideoFolders));
        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(HasTags));
        OnPropertyChanged(nameof(HasPlaylists));
        OnPropertyChanged(nameof(HasHomeContent));
    }

    private static string FormatItemsCount(int count)
    {
        return count == 1 ? "1 item" : $"{count} items";
    }
}
