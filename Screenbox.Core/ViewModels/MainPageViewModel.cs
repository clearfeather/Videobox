#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.Contexts;
using Screenbox.Core.Controllers;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.System;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Core.ViewModels;

public sealed partial class MainPageViewModel : ObservableRecipient,
    IRecipient<SettingsChangedMessage>,
    IRecipient<PropertyChangedMessage<PlayerVisibilityState>>,
    IRecipient<NavigationViewDisplayModeRequestMessage>,
    IRecipient<CriticalErrorMessage>,
    IRecipient<SelectedMediaChangedMessage>,
    IRecipient<TagsChangedMessage>
{
    private const int MaxSuggestionsPerCategory = 6;
    private const int MaxTotalSuggestions = 10;
    private const double IndexWeightFactor = 0.1;

    [ObservableProperty] private bool _playerVisible;
    [ObservableProperty] private bool _shouldUseMargin;
    [ObservableProperty] private bool _isPaneOpen;
    [ObservableProperty] private string _searchQuery;
    [ObservableProperty] private string _criticalErrorMessage;
    [ObservableProperty] private bool _hasCriticalError;
    [ObservableProperty] private bool _hasSelectedMedia;
    [ObservableProperty] private string _selectedMediaName;
    [ObservableProperty] private string _selectedMediaPath;
    [ObservableProperty] private string _selectedMediaDuration;
    [ObservableProperty] private string _selectedMediaResolution;
    [ObservableProperty] private string _selectedMediaSize;
    [ObservableProperty] private string _selectedMediaLastWatched;
    [ObservableProperty] private string _selectedMediaFavoriteStatus;
    [ObservableProperty] private string _selectedMediaTags;

    public bool ShowThumbnailLoadingStatus => _thumbnailLoadingService.ShouldShowStatus;

    public bool IsThumbnailLoadingActive => _thumbnailLoadingService.IsBusy;

    public string ThumbnailLoadingStatusText => _thumbnailLoadingService.StatusText;

    public bool ShowRecent => _settingsService.ShowRecent;

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private NavigationViewDisplayMode _navigationViewDisplayMode;

    private readonly ISearchService _searchService;
    private readonly INavigationService _navigationService;
    private readonly ISettingsService _settingsService;
    private readonly LibraryContext _libraryContext;
    private readonly ILibraryService _libraryService;
    private readonly LibraryController _libraryController;
    private readonly PlaylistsContext _playlistsContext;
    private readonly IPlaylistService _playlistService;
    private readonly IFilesService _filesService;
    private readonly ITagsService _tagsService;
    private readonly IThumbnailLoadingService _thumbnailLoadingService;
    private readonly LastPositionTracker _lastPositionTracker;
    private MediaViewModel? _selectedMedia;
    private StorageFile? _selectedMediaFile;

    public ObservableCollection<SearchSuggestionItem> SearchSuggestions { get; } = new();

    public MainPageViewModel(ISearchService searchService, INavigationService navigationService,
        ISettingsService settingsService,
        LibraryContext libraryContext, ILibraryService libraryService, LibraryController libraryController,
        PlaylistsContext playlistsContext, IPlaylistService playlistService, IFilesService filesService,
        ITagsService tagsService, IThumbnailLoadingService thumbnailLoadingService,
        LastPositionTracker lastPositionTracker)
    {
        _searchService = searchService;
        _navigationService = navigationService;
        _settingsService = settingsService;
        _libraryContext = libraryContext;
        _libraryService = libraryService;
        _libraryController = libraryController;
        _playlistsContext = playlistsContext;
        _playlistService = playlistService;
        _filesService = filesService;
        _tagsService = tagsService;
        _thumbnailLoadingService = thumbnailLoadingService;
        _lastPositionTracker = lastPositionTracker;
        _searchQuery = string.Empty;
        _criticalErrorMessage = string.Empty;
        _selectedMediaName = string.Empty;
        _selectedMediaPath = string.Empty;
        _selectedMediaDuration = string.Empty;
        _selectedMediaResolution = string.Empty;
        _selectedMediaSize = string.Empty;
        _selectedMediaLastWatched = string.Empty;
        _selectedMediaFavoriteStatus = string.Empty;
        _selectedMediaTags = string.Empty;
        if (_thumbnailLoadingService is INotifyPropertyChanged thumbnailLoadingNotifier)
        {
            thumbnailLoadingNotifier.PropertyChanged += ThumbnailLoadingService_PropertyChanged;
        }

        IsActive = true;
    }

    public void Receive(SettingsChangedMessage message)
    {
        if (message.SettingsName == nameof(SettingsPageViewModel.ShowRecent))
        {
            OnPropertyChanged(nameof(ShowRecent));
        }
    }

    private void ThumbnailLoadingService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ShowThumbnailLoadingStatus));
        OnPropertyChanged(nameof(IsThumbnailLoadingActive));
        OnPropertyChanged(nameof(ThumbnailLoadingStatusText));
    }

    public void Receive(CriticalErrorMessage message)
    {
        HasCriticalError = true;
        CriticalErrorMessage = message.Message;
    }

    public void Receive(SelectedMediaChangedMessage message)
    {
        _ = SelectMediaAsync(message.Value);
    }

    public void Receive(TagsChangedMessage message)
    {
        if (_selectedMediaFile != null)
        {
            _ = UpdateSelectedMediaTagsAsync(_selectedMediaFile);
        }
    }

    public void Receive(PropertyChangedMessage<PlayerVisibilityState> message)
    {
        PlayerVisible = message.NewValue == PlayerVisibilityState.Visible;
        ShouldUseMargin = message.NewValue != PlayerVisibilityState.Hidden;

        if (message.NewValue == PlayerVisibilityState.Hidden)
        {
            ClearSelectedMedia();
        }
    }

    public void Receive(NavigationViewDisplayModeRequestMessage message)
    {
        message.Reply(NavigationViewDisplayMode);
    }

    public bool TryGetPageTypeFromParameter(object? parameter, out Type pageType)
    {
        pageType = typeof(object);
        return parameter is NavigationMetadata metadata &&
               _navigationService.TryGetPageType(metadata.RootViewModelType, out pageType);
    }

    public bool ProcessGamepadKeyDown(VirtualKey key)
    {
        // All Gamepad keys are in the range of [195, 218]
        if ((int)key < 195 || (int)key > 218) return false;
        Playlist playlist = Messenger.Send(new PlaylistRequestMessage());
        if (playlist.IsEmpty) return false;

        int? volumeChange = null;
        switch (key)
        {
            case VirtualKey.GamepadRightThumbstickLeft:
            case VirtualKey.GamepadLeftShoulder:
                Messenger.SendSeekWithStatus(TimeSpan.FromMilliseconds(-5000));
                break;
            case VirtualKey.GamepadRightThumbstickRight:
            case VirtualKey.GamepadRightShoulder:
                Messenger.SendSeekWithStatus(TimeSpan.FromMilliseconds(5000));
                break;
            case VirtualKey.GamepadLeftTrigger when PlayerVisible:
                Messenger.SendSeekWithStatus(TimeSpan.FromMilliseconds(-30_000));
                break;
            case VirtualKey.GamepadRightTrigger when PlayerVisible:
                Messenger.SendSeekWithStatus(TimeSpan.FromMilliseconds(30_000));
                break;
            case VirtualKey.GamepadRightThumbstickUp:
                volumeChange = 2;
                break;
            case VirtualKey.GamepadRightThumbstickDown:
                volumeChange = -2;
                break;
            case VirtualKey.GamepadX:
                Messenger.Send(new TogglePlayPauseMessage(true));
                break;
            case VirtualKey.GamepadView when PlayerVisible || NavigationViewDisplayMode == NavigationViewDisplayMode.Expanded:
                Messenger.Send(new TogglePlayerVisibilityMessage());
                break;
            default:
                return false;
        }

        if (volumeChange.HasValue)
        {
            int volume = Messenger.Send(new ChangeVolumeRequestMessage(volumeChange.Value, true));
            Messenger.Send(new UpdateVolumeStatusMessage(volume));
        }

        return true;
    }

    public void OnDrop(DataPackageView data)
    {
        Messenger.Send(new DragDropMessage(data));
    }

    public void UpdateSearchSuggestions(string queryText)
    {
        string searchQuery = queryText.Trim();
        SearchSuggestions.Clear();
        if (searchQuery.Length > 0)
        {
            var result = _searchService.SearchLocalLibrary(_libraryContext, searchQuery);
            var suggestions = GetSuggestItems(result, searchQuery);

            if (suggestions.Count != 0)
            {
                foreach (var suggestion in suggestions)
                {
                    SearchSuggestions.Add(suggestion);
                }
            }
            else
            {
                SearchSuggestions.Add(new SearchSuggestionItem(searchQuery, null, SearchSuggestionKind.None));
            }
        }
    }

    public void SubmitSearch(string queryText)
    {
        string searchQuery = queryText.Trim();
        if (searchQuery.Length > 0)
        {
            SearchResult result = _searchService.SearchLocalLibrary(_libraryContext, searchQuery);
            _navigationService.Navigate(typeof(SearchResultPageViewModel), result);
        }
    }

    public void SelectSuggestion(SearchSuggestionItem? chosenSuggestion)
    {
        if (chosenSuggestion?.Data == null) return;

        switch (chosenSuggestion.Data)
        {
            case MediaViewModel media:
                Messenger.Send(new PlayMediaMessage(media));
                break;
            case AlbumViewModel album:
                _navigationService.Navigate(typeof(AlbumDetailsPageViewModel), album);
                break;
            case ArtistViewModel artist:
                _navigationService.Navigate(typeof(ArtistDetailsPageViewModel), artist);
                break;
        }
    }

    [RelayCommand]
    private async Task EditSelectedTagsAsync()
    {
        if (_selectedMediaFile == null) return;

        IReadOnlyList<string> existingTags = await _tagsService.LoadTagsForItemAsync(_selectedMediaFile);
        string? selectedTag = await TagPickerDialog.ShowAsync(
            "Edit tag",
            await _tagsService.LoadTagNamesAsync(),
            existingTags.FirstOrDefault());
        if (selectedTag == null)
        {
            return;
        }

        IReadOnlyList<string> tags = await _tagsService.SetTagsAsync(_selectedMediaFile, ToSingleTagList(selectedTag));
        Messenger.Send(new TagsChangedMessage(tags));
        await UpdateSelectedMediaTagsAsync(_selectedMediaFile);
    }

    private IReadOnlyList<SearchSuggestionItem> GetSuggestItems(SearchResult result, string searchQuery)
    {
        if (!result.HasItems) return Array.Empty<SearchSuggestionItem>();

        IEnumerable<SearchSuggestionItem> songs = result.Songs
            .Take(MaxSuggestionsPerCategory)
            .Select(s => new SearchSuggestionItem(s.Name, s, SearchSuggestionKind.Song));
        IEnumerable<SearchSuggestionItem> videos = result.Videos
            .Take(MaxSuggestionsPerCategory)
            .Select(v => new SearchSuggestionItem(v.DisplayName, v, SearchSuggestionKind.Video));
        IEnumerable<SearchSuggestionItem> artists = result.Artists
            .Take(MaxSuggestionsPerCategory)
            .Select(a => new SearchSuggestionItem(a.Name, a, SearchSuggestionKind.Artist));
        IEnumerable<SearchSuggestionItem> albums = result.Albums
            .Take(MaxSuggestionsPerCategory)
            .Select(a => new SearchSuggestionItem(a.Name, a, SearchSuggestionKind.Album));
        IEnumerable<(double, SearchSuggestionItem)> searchResults = songs
            .Concat(videos).Concat(artists).Concat(albums)
            .Select(item => (GetRanking(item.Name, searchQuery), item))
            .OrderBy(t => t.Item1)
            .Take(MaxTotalSuggestions);

        return searchResults.Select(t => t.Item2).ToArray();
    }

    private static double GetRanking(string text, string query)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(query))
            return -1;

        int index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (query.Contains(' ') || index < 0)
        {
            return index;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double wordRank = words
            .Select(s => s.IndexOf(query, StringComparison.CurrentCultureIgnoreCase))
            .Where(i => i >= 0)
            .Average();
        return (index * IndexWeightFactor) + wordRank;
    }

    private async Task SelectMediaAsync(MediaViewModel media)
    {
        _selectedMedia = media;
        _selectedMediaFile = await GetStorageFileAsync(media);

        if (_selectedMediaFile != null && !media.DetailsLoaded)
        {
            await media.LoadDetailsAsync(_filesService);
        }

        SelectedMediaName = media.DisplayName;
        SelectedMediaPath = media.Location;
        SelectedMediaDuration = media.Duration > TimeSpan.Zero ? Humanizer.ToDuration(media.Duration) : "Unknown";
        SelectedMediaResolution = GetResolutionText(media);
        SelectedMediaSize = await GetSizeTextAsync(media, _selectedMediaFile);
        SelectedMediaLastWatched = GetLastWatchedText(media);
        SelectedMediaFavoriteStatus = media.IsFavorite ? "Favorite" : "Not favorite";
        if (_selectedMediaFile != null)
        {
            await UpdateSelectedMediaTagsAsync(_selectedMediaFile);
        }
        else
        {
            SelectedMediaTags = "None";
        }

        HasSelectedMedia = true;
    }

    private void ClearSelectedMedia()
    {
        _selectedMedia = null;
        _selectedMediaFile = null;
        HasSelectedMedia = false;
        SelectedMediaName = string.Empty;
        SelectedMediaPath = string.Empty;
        SelectedMediaDuration = string.Empty;
        SelectedMediaResolution = string.Empty;
        SelectedMediaSize = string.Empty;
        SelectedMediaLastWatched = string.Empty;
        SelectedMediaFavoriteStatus = string.Empty;
        SelectedMediaTags = string.Empty;
    }

    private async Task UpdateSelectedMediaTagsAsync(IStorageItem item)
    {
        IReadOnlyList<string> tags = await _tagsService.LoadTagsForItemAsync(item);
        SelectedMediaTags = tags.Count == 0 ? "None" : string.Join(", ", tags);
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

    private static string GetResolutionText(MediaViewModel media)
    {
        VideoInfo video = media.MediaInfo.VideoProperties;
        return video.Width > 0 && video.Height > 0
            ? $"{video.Width} x {video.Height}"
            : "Unknown";
    }

    private static async Task<string> GetSizeTextAsync(MediaViewModel media, StorageFile? file)
    {
        ulong size = media.MediaInfo.Size;
        if (size == 0 && file != null)
        {
            BasicProperties properties = await StorageFilePropertiesGate.GetBasicPropertiesAsync(file);
            size = properties.Size;
        }

        return size > 0 ? FormatFileSize(size) : "Unknown";
    }

    private string GetLastWatchedText(MediaViewModel media)
    {
        DateTime? lastWatched = _lastPositionTracker.GetLastWatched(media.Location);
        return lastWatched is { } value && value != default
            ? value.ToLocalTime().ToString("g")
            : "Never";
    }

    private static string FormatFileSize(ulong bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static IEnumerable<string> ToSingleTagList(string tag)
    {
        tag = tag.Trim();
        return string.IsNullOrWhiteSpace(tag) ? Array.Empty<string>() : new[] { tag };
    }

    public async Task FetchLibraries()
    {
        try
        {
            await _libraryController.EnsureWatchingAsync();
        }
        catch (Exception)
        {
            // pass
        }

        List<Task> tasks = new() { FetchMusicLibraryAsync(), FetchVideosLibraryAsync(), FetchPlaylistsAsync() };
        await Task.WhenAll(tasks);
    }

    private async Task FetchMusicLibraryAsync()
    {
        try
        {
            await _libraryService.FetchMusicAsync(_libraryContext);
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Music));
        }
        catch (Exception e)
        {
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
        }
    }

    private async Task FetchVideosLibraryAsync()
    {
        try
        {
            await _libraryService.FetchVideosAsync(_libraryContext);
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Videos));
        }
        catch (Exception e)
        {
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
        }
    }

    /// <summary>
    /// Fetches playlists from storage and populates the PlaylistsContext.
    /// </summary>
    private async Task FetchPlaylistsAsync()
    {
        try
        {
            var loaded = await _playlistService.ListPlaylistsAsync();
            _playlistsContext.Playlists.Clear();
            foreach (var p in loaded)
            {
                var playlist = Ioc.Default.GetRequiredService<PlaylistViewModel>();
                try
                {
                    playlist.Load(p);
                    _playlistsContext.Playlists.Add(playlist);
                }
                catch (Exception e)
                {
                    LogService.Log(e);
                }
            }
        }
        catch (Exception e)
        {
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
        }
    }
}
