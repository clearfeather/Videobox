using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.System;

namespace Screenbox.Core.ViewModels;

public sealed partial class AllVideosPageViewModel : ObservableRecipient,
    IRecipient<LibraryContentChangedMessage>,
    IRecipient<TagsChangedMessage>
{
    private enum VideoSortMode
    {
        Name,
        Folder,
        Newest,
        Oldest,
        Longest,
        Shortest,
        Quality,
        Favorites
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private int _selectedSortIndex;
    [ObservableProperty] private string _searchQuery = string.Empty;
    [ObservableProperty] private int _selectedThumbnailSizeIndex = 1;
    [ObservableProperty] private double _thumbnailWidth = 232;
    [ObservableProperty] private double _thumbnailHeight = 128;

    public ObservableCollection<MediaViewModel> Videos { get; }

    public string[] SortOptions { get; } = { "Name", "Folder", "Newest", "Oldest", "Longest", "Shortest", "Quality", "Favorites" };

    public string[] ThumbnailSizeOptions { get; } = { "Small", "Medium", "Large", "X-large" };

    private readonly LibraryContext _libraryContext;
    private readonly IFilesService _filesService;
    private readonly ITagsService _tagsService;
    private readonly ISettingsService _settingsService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _timer;
    private readonly List<MediaViewModel> _allVideos = new();

    public AllVideosPageViewModel(
        LibraryContext libraryContext,
        IFilesService filesService,
        ITagsService tagsService,
        ISettingsService settingsService)
    {
        _libraryContext = libraryContext;
        _filesService = filesService;
        _tagsService = tagsService;
        _settingsService = settingsService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcherQueue.CreateTimer();
        Videos = new ObservableCollection<MediaViewModel>();
        _selectedSortIndex = ClampSortIndex(_settingsService.AllVideosSortIndex);
        _selectedThumbnailSizeIndex = ClampThumbnailSizeIndex(_settingsService.AllVideosThumbnailSizeIndex);
        ApplyThumbnailSize(_selectedThumbnailSizeIndex);

        IsActive = true;
    }

    partial void OnSelectedSortIndexChanged(int value)
    {
        _settingsService.AllVideosSortIndex = ClampSortIndex(value);
        _ = ApplyViewAsync();
    }

    partial void OnSearchQueryChanged(string value)
    {
        _ = ApplyViewAsync(false);
    }

    partial void OnThumbnailWidthChanged(double value)
    {
        ThumbnailHeight = Math.Round(value * 0.552);
    }

    partial void OnSelectedThumbnailSizeIndexChanged(int value)
    {
        int clampedValue = ClampThumbnailSizeIndex(value);
        _settingsService.AllVideosThumbnailSizeIndex = clampedValue;
        ApplyThumbnailSize(clampedValue);
    }

    private void ApplyThumbnailSize(int value)
    {
        ThumbnailWidth = value switch
        {
            0 => 180,
            2 => 300,
            3 => 360,
            _ => 232
        };
    }

    public void Receive(LibraryContentChangedMessage message)
    {
        if (message.LibraryId != KnownLibraryId.Videos) return;
        _dispatcherQueue.TryEnqueue(UpdateVideos);
    }

    public void Receive(TagsChangedMessage message)
    {
        _ = UpdateVisibleCaptionsAsync();
    }

    public void UpdateVideos()
    {
        IsLoading = _libraryContext.IsLoadingVideos;
        _allVideos.Clear();
        _allVideos.AddRange(_libraryContext.Videos);

        _ = ApplyViewAsync(false);

        // Progressively update when it's still loading
        if (IsLoading)
        {
            _timer.Debounce(UpdateVideos, TimeSpan.FromSeconds(5));
        }
        else
        {
            _timer.Stop();
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        UpdateVideos();
    }

    private async Task ApplyViewAsync(bool showLoading = true)
    {
        IReadOnlyList<MediaViewModel> videos = GetFilteredVideos().ToArray();
        VideoSortMode sortMode = GetSortMode();
        if (RequiresMediaDetails(sortMode))
        {
            if (showLoading)
            {
                IsLoading = true;
            }

            foreach (MediaViewModel video in videos)
            {
                if (!video.DetailsLoaded)
                {
                    await video.LoadDetailsAsync(_filesService);
                }
            }

            if (showLoading)
            {
                IsLoading = _libraryContext.IsLoadingVideos;
            }
        }

        string[] libraryRoots = GetVideoLibraryRoots();
        videos = SortVideos(videos, sortMode, libraryRoots).ToArray();
        await UpdateAllVideosCaptionsAsync(videos, libraryRoots);
        if (videos.Count < 5000)
        {
            // Only sync when the number of items is low enough
            // Sync on too many items can cause UI hang
            Videos.SyncItems(videos);
        }
        else
        {
            Videos.Clear();
            foreach (MediaViewModel video in videos)
            {
                Videos.Add(video);
            }
        }
    }

    private VideoSortMode GetSortMode()
    {
        return (VideoSortMode)ClampSortIndex(SelectedSortIndex);
    }

    private int ClampSortIndex(int value)
    {
        return value >= 0 && value < SortOptions.Length ? value : 0;
    }

    private int ClampThumbnailSizeIndex(int value)
    {
        return value >= 0 && value < ThumbnailSizeOptions.Length ? value : 1;
    }

    private IEnumerable<MediaViewModel> GetFilteredVideos()
    {
        string query = SearchQuery.Trim();
        if (query.Length == 0)
        {
            return _allVideos;
        }

        return _allVideos.Where(video =>
            video.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            video.DisplayCaption.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
            video.Location.Contains(query, StringComparison.CurrentCultureIgnoreCase));
    }

    private static IEnumerable<MediaViewModel> SortVideos(
        IEnumerable<MediaViewModel> videos,
        VideoSortMode sortMode,
        IReadOnlyList<string> libraryRoots)
    {
        return sortMode switch
        {
            VideoSortMode.Folder => videos
                .OrderBy(video => GetMainFolderName(video, libraryRoots), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Newest => videos
                .OrderByDescending(video => video.MediaInfo.DateModified)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Oldest => videos
                .OrderBy(video => video.MediaInfo.DateModified == default)
                .ThenBy(video => video.MediaInfo.DateModified)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Longest => videos
                .OrderByDescending(video => video.Duration)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Shortest => videos
                .OrderBy(video => video.Duration == TimeSpan.Zero)
                .ThenBy(video => video.Duration)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Quality => videos
                .OrderByDescending(video => GetQuality(video))
                .ThenByDescending(video => video.MediaInfo.VideoProperties.Bitrate)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            VideoSortMode.Favorites => videos
                .OrderByDescending(video => video.IsFavorite)
                .ThenBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase),
            _ => videos.OrderBy(video => video.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        };
    }

    private static ulong GetQuality(MediaViewModel video)
    {
        VideoInfo info = video.MediaInfo.VideoProperties;
        return (ulong)info.Width * info.Height;
    }

    private static bool RequiresMediaDetails(VideoSortMode sortMode)
    {
        return sortMode is VideoSortMode.Newest
            or VideoSortMode.Oldest
            or VideoSortMode.Longest
            or VideoSortMode.Shortest
            or VideoSortMode.Quality;
    }

    private async Task UpdateVisibleCaptionsAsync()
    {
        await UpdateAllVideosCaptionsAsync(Videos, GetVideoLibraryRoots());
    }

    private string[] GetVideoLibraryRoots()
    {
        return _libraryContext.VideosLibrary?.Folders
            .Select(folder => folder.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .OrderByDescending(path => path.Length)
            .ToArray() ?? Array.Empty<string>();
    }

    private async Task UpdateAllVideosCaptionsAsync(IEnumerable<MediaViewModel> videos, IReadOnlyList<string> libraryRoots)
    {
        IReadOnlyDictionary<string, string> tagMap = await _tagsService.LoadItemTagMapAsync();

        foreach (MediaViewModel video in videos)
        {
            video.AllVideosCaption = BuildAllVideosCaption(video, libraryRoots, tagMap);
        }
    }

    private static string BuildAllVideosCaption(
        MediaViewModel video,
        IReadOnlyList<string> libraryRoots,
        IReadOnlyDictionary<string, string> tagMap)
    {
        List<string> parts = new();
        if (!string.IsNullOrWhiteSpace(video.DisplayCaption))
        {
            parts.Add(video.DisplayCaption);
        }

        string folderName = GetMainFolderName(video, libraryRoots);
        if (!string.IsNullOrWhiteSpace(folderName))
        {
            parts.Add(folderName);
        }

        string path = GetFilePath(video);
        if (!string.IsNullOrWhiteSpace(path) && tagMap.TryGetValue(path, out string tag) && !string.IsNullOrWhiteSpace(tag))
        {
            parts.Add(tag);
        }

        return string.Join(" | ", parts);
    }

    private static string GetMainFolderName(MediaViewModel video, IReadOnlyList<string> libraryRoots)
    {
        string path = GetFilePath(video);
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        foreach (string root in libraryRoots)
        {
            string normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith(normalizedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string relative = path.Substring(normalizedRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string? firstSegment = relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstSegment) ||
                   firstSegment.Equals(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileName(normalizedRoot)
                : firstSegment;
        }

        string? parent = Path.GetDirectoryName(path);
        return string.IsNullOrWhiteSpace(parent) ? string.Empty : Path.GetFileName(parent);
    }

    private static string GetFilePath(MediaViewModel video)
    {
        if (video.Source is IStorageItem item && !string.IsNullOrWhiteSpace(item.Path))
        {
            return item.Path;
        }

        if (Uri.TryCreate(video.Location, UriKind.Absolute, out Uri uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return video.Location;
    }

    [RelayCommand]
    private void Select(MediaViewModel media)
    {
        Messenger.Send(new SelectedMediaChangedMessage(media));
        Play(media);
    }

    [RelayCommand]
    private void Play(MediaViewModel media)
    {
        if (Videos.Count == 0) return;
        Messenger.SendQueueAndPlay(media, Videos, true);
    }
}
