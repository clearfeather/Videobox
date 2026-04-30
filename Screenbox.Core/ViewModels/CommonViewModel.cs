#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Core.ViewModels;

public sealed partial class CommonViewModel : ObservableRecipient,
    IRecipient<SettingsChangedMessage>,
    IRecipient<PropertyChangedMessage<NavigationViewDisplayMode>>,
    IRecipient<PropertyChangedMessage<PlayerVisibilityState>>
{
    public Dictionary<Type, string> NavigationStates { get; }

    public bool IsAdvancedModeEnabled => _settingsService.AdvancedMode;

    [ObservableProperty] private NavigationViewDisplayMode _navigationViewDisplayMode;
    [ObservableProperty] private Thickness _scrollBarMargin;
    [ObservableProperty] private Thickness _footerBottomPaddingMargin;
    [ObservableProperty] private double _footerBottomPaddingHeight;
    [ObservableProperty] private bool _animationsEnabled;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly INavigationService _navigationService;
    private readonly IFilesService _filesService;
    private readonly ISettingsService _settingsService;
    private readonly IPlaylistService _playlistService;
    private readonly IFavoritesService _favoritesService;
    private readonly ITagsService _tagsService;
    private readonly IThumbnailService _thumbnailService;
    private readonly PlaylistsContext _playlistsContext;
    private readonly FavoritesContext _favoritesContext;
    private readonly Dictionary<string, object> _pageStates;
    private readonly UISettings _uiSettings;

    public CommonViewModel(INavigationService navigationService,
        IFilesService filesService,
        ISettingsService settingsService,
        IPlaylistService playlistService,
        PlaylistsContext playlistsContext,
        IFavoritesService favoritesService,
        FavoritesContext favoritesContext,
        ITagsService tagsService,
        IThumbnailService thumbnailService)
    {
        _navigationService = navigationService;
        _filesService = filesService;
        _settingsService = settingsService;
        _playlistService = playlistService;
        _playlistsContext = playlistsContext;
        _favoritesService = favoritesService;
        _favoritesContext = favoritesContext;
        _tagsService = tagsService;
        _thumbnailService = thumbnailService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _navigationViewDisplayMode = Messenger.Send<NavigationViewDisplayModeRequestMessage>();
        NavigationStates = new Dictionary<Type, string>();
        _pageStates = new Dictionary<string, object>();

        _uiSettings = new UISettings();
        _uiSettings.AnimationsEnabledChanged += UISettings_OnAnimationsEnabledChanged;
        _animationsEnabled = _uiSettings.AnimationsEnabled;

        // Activate the view model's messenger
        IsActive = true;
    }

    public void Receive(SettingsChangedMessage message)
    {
        if (message.SettingsName == nameof(SettingsPageViewModel.Theme) &&
            Window.Current.Content is Frame rootFrame)
        {
            rootFrame.RequestedTheme = _settingsService.Theme.ToElementTheme();
        }
    }

    public void Receive(PropertyChangedMessage<NavigationViewDisplayMode> message)
    {
        this.NavigationViewDisplayMode = message.NewValue;
    }

    public void Receive(PropertyChangedMessage<PlayerVisibilityState> message)
    {
        ScrollBarMargin = message.NewValue == PlayerVisibilityState.Hidden
            ? new Thickness(0)
            : (Thickness)Application.Current.Resources["ContentPageScrollBarMargin"];

        FooterBottomPaddingMargin = message.NewValue == PlayerVisibilityState.Hidden
            ? new Thickness(0)
            : (Thickness)Application.Current.Resources["ContentPageBottomMargin"];

        FooterBottomPaddingHeight = message.NewValue == PlayerVisibilityState.Hidden
            ? 0
            : (double)Application.Current.Resources["ContentPageBottomPaddingHeight"];
    }

    public void SavePageState(object state, string pageTypeName, int backStackDepth)
    {
        _pageStates[pageTypeName + backStackDepth] = state;
    }

    public bool TryGetPageState(string pageTypeName, int backStackDepth, out object state)
    {
        return _pageStates.TryGetValue(pageTypeName + backStackDepth, out state);
    }

    private void UISettings_OnAnimationsEnabledChanged(UISettings sender, UISettingsAnimationsEnabledChangedEventArgs args)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            AnimationsEnabled = sender.AnimationsEnabled;
        });
    }

    [RelayCommand]
    private void PlayNext(MediaViewModel media)
    {
        Messenger.SendPlayNext(media);
    }

    [RelayCommand]
    private void AddToQueue(MediaViewModel media)
    {
        Messenger.SendAddToQueue(media);
    }

    [RelayCommand]
    private async Task AddToFavoritesAsync(MediaViewModel? media)
    {
        if (media == null) return;
        await EnsureFavoritesLoadedAsync();
        if (_favoritesContext.Favorites.Any(f => SameLocation(f, media))) return;

        media.IsFavorite = true;
        _favoritesContext.Favorites.Add(media);
        await _favoritesService.SaveFavoritesAsync(_favoritesContext.Favorites);
    }

    [RelayCommand]
    private async Task RemoveFromFavoritesAsync(MediaViewModel? media)
    {
        if (media == null) return;
        await EnsureFavoritesLoadedAsync();

        MediaViewModel? existing = _favoritesContext.Favorites.FirstOrDefault(f => SameLocation(f, media));
        if (existing == null) return;

        _favoritesContext.Favorites.Remove(existing);
        existing.IsFavorite = false;
        media.IsFavorite = false;
        await _favoritesService.SaveFavoritesAsync(_favoritesContext.Favorites);
    }

    [RelayCommand]
    private async Task AddTagAsync(StorageItemViewModel? item)
    {
        if (item == null) return;
        bool isFolder = item.StorageItem is StorageFolder;

        string? selectedTag = await TagPickerDialog.ShowAsync(
            isFolder ? "Tag all videos in folder" : "Add tag",
            await _tagsService.LoadTagNamesAsync(),
            primaryButtonText: "Add");
        if (string.IsNullOrWhiteSpace(selectedTag))
        {
            return;
        }

        string tagName = selectedTag.Trim();
        IReadOnlyList<string> tags = await _tagsService.AddTagAsync(tagName, item.StorageItem);
        Messenger.Send(new TagsChangedMessage(tags));
    }

    [RelayCommand]
    private async Task EditTagsAsync(StorageItemViewModel? item)
    {
        if (item == null) return;

        await EditTagsForStorageItemAsync(
            item.StorageItem,
            item.StorageItem is StorageFolder ? "Edit tag for folder videos" : "Edit tag");
    }

    [RelayCommand]
    private async Task EditMediaTagsAsync(MediaViewModel? media)
    {
        if (media == null) return;

        StorageFile? file = await GetStorageFileAsync(media);
        if (file == null) return;

        await EditTagsForStorageItemAsync(file, "Edit tag");
    }

    [RelayCommand]
    private async Task AddTagsToItemsAsync(IList<object>? selectedItems)
    {
        if (selectedItems == null || selectedItems.Count == 0) return;

        List<IStorageItem> items = new();
        foreach (object selectedItem in selectedItems)
        {
            switch (selectedItem)
            {
                case StorageItemViewModel { Media: not null, StorageItem: StorageFile file }:
                    items.Add(file);
                    break;
                case MediaViewModel media:
                    StorageFile? mediaFile = await GetStorageFileAsync(media);
                    if (mediaFile != null)
                    {
                        items.Add(mediaFile);
                    }

                    break;
            }
        }

        items = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        if (items.Count == 0) return;

        string? selectedTag = await TagPickerDialog.ShowAsync(
            $"Set tag for {items.Count} videos",
            await _tagsService.LoadTagNamesAsync());
        if (string.IsNullOrWhiteSpace(selectedTag))
        {
            return;
        }

        string tagName = selectedTag.Trim();
        IReadOnlyList<string> tags = await _tagsService.AddTagsAsync(new[] { tagName }, items);
        Messenger.Send(new TagsChangedMessage(tags));
    }

    [RelayCommand]
    private async Task SetFolderCoverAsync(StorageItemViewModel? item)
    {
        if (item?.StorageItem is not StorageFolder folder || string.IsNullOrWhiteSpace(folder.Path))
        {
            return;
        }

        FileOpenPicker picker = new()
        {
            ViewMode = PickerViewMode.Thumbnail,
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".webp");
        picker.FileTypeFilter.Add(".bmp");

        StorageFile? coverFile = await picker.PickSingleFileAsync();
        if (coverFile == null) return;

        IBuffer buffer = await FileIO.ReadBufferAsync(coverFile);
        byte[] bytes = new byte[buffer.Length];
        using DataReader reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);

        await _thumbnailService.SaveThumbnailAsync(folder.Path, bytes);
        item.InvalidateThumbnail();
        await item.LoadFolderPreviewThumbnailAsync();
    }

    private async Task EditTagsForStorageItemAsync(IStorageItem item, string title)
    {
        IReadOnlyList<string> existingTags = await _tagsService.LoadTagsForItemAsync(item);
        string? selectedTag = await TagPickerDialog.ShowAsync(
            title,
            await _tagsService.LoadTagNamesAsync(),
            existingTags.FirstOrDefault());
        if (selectedTag == null)
        {
            return;
        }

        IReadOnlyList<string> tags = await _tagsService.SetTagsAsync(item, ToSingleTagList(selectedTag));
        Messenger.Send(new TagsChangedMessage(tags));
    }

    private static async Task<StorageFile?> GetStorageFileAsync(MediaViewModel media)
    {
        if (media.Source is StorageFile file)
        {
            return file;
        }

        if (string.IsNullOrWhiteSpace(media.Location))
        {
            return null;
        }

        try
        {
            return await StorageFile.GetFileFromPathAsync(media.Location);
        }
        catch
        {
            return null;
        }
    }

    [RelayCommand]
    private void OpenAlbum(AlbumViewModel? album)
    {
        if (album == null) return;
        _navigationService.Navigate(typeof(AlbumDetailsPageViewModel),
            new NavigationMetadata(typeof(MusicPageViewModel), album));
    }

    [RelayCommand]
    private void OpenArtist(ArtistViewModel? artist)
    {
        if (artist == null) return;
        _navigationService.Navigate(typeof(ArtistDetailsPageViewModel),
            new NavigationMetadata(typeof(MusicPageViewModel), artist));
    }

    [RelayCommand]
    private void OpenPlaylist(PlaylistViewModel? playlist)
    {
        if (playlist == null) return;
        _navigationService.Navigate(typeof(PlaylistDetailsPageViewModel),
            new NavigationMetadata(typeof(PlaylistsPageViewModel), playlist));
    }

    /// <summary>
    /// Opens a file picker for the user to select one or more media files to play.
    /// Sends a <see cref="Core.Messages.FailedToOpenFilesNotificationMessage"/> on failure.
    /// </summary>
    [RelayCommand]
    private async Task OpenFilesAsync()
    {
        try
        {
            IReadOnlyList<StorageFile>? files = await _filesService.PickMultipleFilesAsync();
            if (files == null || files.Count == 0) return;
            Messenger.Send(new PlayMediaMessage(files));
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToOpenFilesNotificationMessage(e.Message));
        }
    }

    private async Task EnsureFavoritesLoadedAsync()
    {
        if (_favoritesContext.IsLoaded) return;

        _favoritesContext.Favorites.Clear();
        foreach (MediaViewModel favorite in await _favoritesService.LoadFavoritesAsync())
        {
            _favoritesContext.Favorites.Add(favorite);
        }

        _favoritesContext.IsLoaded = true;
    }

    private static bool SameLocation(MediaViewModel left, MediaViewModel right)
    {
        return left.Location.Equals(right.Location, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ToSingleTagList(string tag)
    {
        tag = tag.Trim();
        return string.IsNullOrWhiteSpace(tag) ? Array.Empty<string>() : new[] { tag };
    }

}
