#nullable enable

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.System;

namespace Screenbox.Core.ViewModels
{
    public partial class FolderViewPageViewModel : ObservableRecipient,
        IRecipient<RefreshFolderMessage>,
        IRecipient<TagsChangedMessage>
    {
        private enum FolderSortMode
        {
            Name,
            Newest,
            Oldest,
            Longest,
            Shortest,
            Quality,
            Favorites
        }

        public string TitleText => Breadcrumbs.LastOrDefault()?.Name ?? string.Empty;

        public ObservableCollection<StorageItemViewModel> Items { get; }

        public string[] SortOptions { get; } = { "Name", "Newest", "Oldest", "Longest", "Shortest", "Quality", "Favorites" };

        public string[] ThumbnailSizeOptions { get; } = { "Small", "Medium", "Large", "X-large" };

        public IReadOnlyList<StorageFolder> Breadcrumbs { get; private set; }

        internal NavigationMetadata? NavData { get; private set; }

        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private int _selectedSortIndex;
        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private int _selectedThumbnailSizeIndex = 1;
        [ObservableProperty] private double _thumbnailWidth = 232;
        [ObservableProperty] private double _thumbnailHeight = 128;

        private readonly IFilesService _filesService;
        private readonly LibraryContext _libraryContext;
        private readonly INavigationService _navigationService;
        private readonly ISettingsService _settingsService;
        private readonly StorageItemViewModelFactory _storageVmFactory;
        private readonly ITagsService _tagsService;
        private readonly DispatcherQueue _dispatcherQueue;
        private readonly DispatcherQueueTimer _loadingTimer;
        private readonly List<MediaViewModel> _playableItems;
        private readonly List<StorageItemViewModel> _allItems;
        private bool _isActive;
        private object? _source;

        public FolderViewPageViewModel(IFilesService filesService, LibraryContext libraryContext, INavigationService navigationService,
            StorageItemViewModelFactory storageVmFactory,
            ITagsService tagsService,
            ISettingsService settingsService)
        {
            _filesService = filesService;
            _libraryContext = libraryContext;
            _storageVmFactory = storageVmFactory;
            _navigationService = navigationService;
            _tagsService = tagsService;
            _settingsService = settingsService;
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
            _loadingTimer = _dispatcherQueue.CreateTimer();
            _playableItems = new List<MediaViewModel>();
            _allItems = new List<StorageItemViewModel>();
            Breadcrumbs = Array.Empty<StorageFolder>();
            Items = new ObservableCollection<StorageItemViewModel>();
            _selectedSortIndex = ClampSortIndex(_settingsService.VideoFoldersSortIndex);
            _selectedThumbnailSizeIndex = ClampThumbnailSizeIndex(_settingsService.VideoFoldersThumbnailSizeIndex);
            ApplyThumbnailSize(_selectedThumbnailSizeIndex);

            IsActive = true;
        }

        partial void OnSelectedSortIndexChanged(int value)
        {
            _settingsService.VideoFoldersSortIndex = ClampSortIndex(value);
            _ = ApplySortAsync();
        }

        partial void OnSearchQueryChanged(string value)
        {
            _ = ApplySortAsync(false);
        }

        partial void OnThumbnailWidthChanged(double value)
        {
            ThumbnailHeight = Math.Round(value * 0.552);
        }

        partial void OnSelectedThumbnailSizeIndexChanged(int value)
        {
            int clampedValue = ClampThumbnailSizeIndex(value);
            _settingsService.VideoFoldersThumbnailSizeIndex = clampedValue;
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

        public void Receive(RefreshFolderMessage message)
        {
            if (!_isActive) return;
            _dispatcherQueue.TryEnqueue(RefreshFolderContent);
        }

        public void Receive(TagsChangedMessage message)
        {
            if (!_isActive) return;
            _ = RefreshTagsAsync();
        }

        public async Task OnNavigatedTo(object? parameter)
        {
            _isActive = true;
            _source = parameter;
            NavData = parameter as NavigationMetadata;
            await FetchContentAsync(NavData?.Parameter ?? parameter);
        }

        public void OnBreadcrumbBarItemClicked(int index)
        {
            IReadOnlyList<StorageFolder> crumbs = Breadcrumbs.Take(index + 1).ToArray();
            if (NavData != null)
            {
                if (index == 0)
                {
                    _navigationService.Navigate(NavData.RootViewModelType);
                }
                else
                {
                    _navigationService.Navigate(typeof(FolderViewPageViewModel),
                        new NavigationMetadata(NavData.RootViewModelType, crumbs));
                }
            }
            else
            {
                _navigationService.Navigate(typeof(FolderViewPageViewModel),
                    new NavigationMetadata(typeof(FolderViewPageViewModel), crumbs));
            }
        }

        private async Task FetchContentAsync(object? parameter)
        {
            switch (parameter)
            {
                case IReadOnlyList<StorageFolder> { Count: > 0 } breadcrumbs:
                    Breadcrumbs = breadcrumbs;
                    await FetchFolderContentAsync(breadcrumbs.Last());
                    break;
                case StorageLibrary library:
                    await FetchFolderContentAsync(library);
                    break;
                case StorageFileQueryResult queryResult:
                    await FetchQueryItemAsync(queryResult);
                    break;
                case "VideosLibrary":   // Special case for VideosPage
                    // VideosPage needs to serialize navigation state so it cannot set nav data
                    try
                    {
                        Breadcrumbs = new[] { SystemInformation.IsXbox ? KnownFolders.RemovableDevices : KnownFolders.VideosLibrary };
                        NavData = new NavigationMetadata(typeof(VideosPageViewModel), Breadcrumbs);
                        await FetchVideoLibraryFoldersAsync();
                    }
                    catch (Exception)
                    {
                        // pass
                    }
                    break;
            }
        }

        public void OnNavigatedFrom()
        {
            _isActive = false;
        }

        protected virtual void Navigate(object? parameter = null)
        {
            // _navigationService.NavigateExisting(typeof(FolderViewPageViewModel), parameter);
            _navigationService.Navigate(typeof(FolderViewPageViewModel),
                new NavigationMetadata(NavData?.RootViewModelType ?? typeof(FolderViewPageViewModel), parameter));
        }

        [RelayCommand]
        private void Play(StorageItemViewModel item)
        {
            if (item.Media == null) return;
            Messenger.SendQueueAndPlay(item.Media, _playableItems, true);
        }

        [RelayCommand]
        private void PlayNext(StorageItemViewModel item)
        {
            if (item.Media == null) return;
            Messenger.SendPlayNext(item.Media);
        }

        [RelayCommand]
        private void AddToQueue(StorageItemViewModel item)
        {
            if (item.Media == null) return;
            Messenger.SendAddToQueue(item.Media);
        }

        [RelayCommand]
        private void Click(StorageItemViewModel item)
        {
            if (item.Media != null)
            {
                Messenger.Send(new SelectedMediaChangedMessage(item.Media));
                Play(item);
            }
            else if (item.StorageItem is StorageFolder folder)
            {
                StorageFolder[] crumbs = Breadcrumbs.Append(folder).ToArray();
                Navigate(crumbs);
            }
        }

        private async Task FetchQueryItemAsync(StorageFileQueryResult query)
        {
            Items.Clear();
            _allItems.Clear();
            _playableItems.Clear();

            uint fetchIndex = 0;
            while (_isActive)
            {
                _loadingTimer.Debounce(() => IsLoading = true, TimeSpan.FromMilliseconds(800));
                IReadOnlyList<StorageFile> items = await query.GetFilesAsync(fetchIndex, 30);
                if (items.Count == 0) break;
                fetchIndex += (uint)items.Count;
                foreach (StorageFile storageFile in items)
                {
                    StorageItemViewModel item = _storageVmFactory.GetInstance(storageFile);
                    _allItems.Add(item);
                    if (item.Media != null) _playableItems.Add(item.Media);
                }
            }

            _loadingTimer.Stop();
            IsLoading = false;
            RefreshDisplayText();
            await RefreshTagsAsync();
            await ApplySortAsync(false);
            _ = LoadFolderPreviewsAsync(_allItems.ToArray());
        }

        private async Task FetchFolderContentAsync(StorageFolder folder)
        {
            Items.Clear();
            _allItems.Clear();
            _playableItems.Clear();

            StorageItemQueryResult itemQuery = _filesService.GetSupportedItems(folder);
            uint fetchIndex = 0;
            while (_isActive)
            {
                _loadingTimer.Debounce(() => IsLoading = true, TimeSpan.FromMilliseconds(800));
                IReadOnlyList<IStorageItem> items = await itemQuery.GetItemsAsync(fetchIndex, 30);
                if (items.Count == 0) break;
                fetchIndex += (uint)items.Count;
                foreach (IStorageItem storageItem in items)
                {
                    StorageItemViewModel item = _storageVmFactory.GetInstance(storageItem);
                    _allItems.Add(item);
                    if (item.Media != null) _playableItems.Add(item.Media);
                }
            }

            _loadingTimer.Stop();
            IsLoading = false;
            RefreshDisplayText();
            await RefreshTagsAsync();
            await ApplySortAsync(false);
            _ = LoadFolderPreviewsAsync(_allItems.ToArray());
        }

        private async Task FetchVideoLibraryFoldersAsync()
        {
            Items.Clear();
            _allItems.Clear();
            _playableItems.Clear();

            IReadOnlyList<StorageFolder> folders = _libraryContext.VideoFolders.ToList();

            foreach (StorageFolder folder in folders)
            {
                StorageItemViewModel item = _storageVmFactory.GetInstance(folder);
                _allItems.Add(item);
                await item.UpdateCaptionAsync();
            }

            _loadingTimer.Stop();
            IsLoading = false;
            await RefreshTagsAsync();
            await ApplySortAsync(false);
            _ = LoadFolderPreviewsAsync(_allItems.ToArray());
        }

        private async Task LoadFolderPreviewsAsync(IReadOnlyList<StorageItemViewModel> items)
        {
            if (!_settingsService.AutoLoadThumbnails) return;

            StorageItemViewModel[] queuedFolders = items
                .Where(item => !item.IsFile && item.ThumbnailSource == null)
                .ToArray();

            foreach (StorageItemViewModel folder in queuedFolders)
            {
                if (!_isActive || !_settingsService.AutoLoadThumbnails)
                {
                    continue;
                }

                await folder.LoadFolderPreviewThumbnailAsync();
            }
        }

        private async Task FetchFolderContentAsync(StorageLibrary library)
        {
            if (library.Folders.Count <= 0)
            {
                IsEmpty = true;
                return;
            }

            if (library.Folders.Count == 1)
            {
                // StorageLibrary is always the root
                // Fetch content of the only folder if applicable
                StorageFolder folder = library.Folders[0];
                Breadcrumbs = new[] { folder };
                await FetchFolderContentAsync(folder);
            }
            else
            {
                Items.Clear();
                _allItems.Clear();
                foreach (StorageFolder folder in library.Folders)
                {
                    StorageItemViewModel item = _storageVmFactory.GetInstance(folder);
                    _allItems.Add(item);
                    await item.UpdateCaptionAsync();
                }

                await RefreshTagsAsync();
                await ApplySortAsync(false);
            }
        }

        [RelayCommand]
        private void Refresh()
        {
            RefreshFolderContent();
        }

        private async void RefreshFolderContent()
        {
            await FetchContentAsync(NavData?.Parameter ?? _source);
        }

        private void RefreshDisplayText()
        {
            MediaTitleFormatter.LearnCommonStudioPrefixes(_playableItems, Breadcrumbs.Select(folder => folder.Name));
            foreach (StorageItemViewModel item in Items)
            {
                item.InvalidateDisplayText();
            }
        }

        private async Task RefreshTagsAsync()
        {
            IReadOnlyDictionary<string, string> tagMap = await _tagsService.LoadItemTagMapAsync();
            foreach (StorageItemViewModel item in _allItems)
            {
                item.Tag = GetTagText(item, tagMap);
            }
        }

        private static string GetTagText(StorageItemViewModel item, IReadOnlyDictionary<string, string> tagMap)
        {
            if (item.IsFile)
            {
                return tagMap.TryGetValue(item.Path, out string tag) ? tag : string.Empty;
            }

            string[] tags = tagMap
                .Where(entry => IsPathInFolder(entry.Key, item.Path))
                .Select(entry => entry.Value)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();

            return tags.Length switch
            {
                0 => string.Empty,
                1 => tags[0],
                _ => "Multiple tags"
            };
        }

        private static bool IsPathInFolder(string path, string folderPath)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folderPath))
            {
                return false;
            }

            string normalizedFolder = folderPath.TrimEnd('\\', '/');
            return path.StartsWith(normalizedFolder + "\\", StringComparison.OrdinalIgnoreCase) ||
                   path.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
        }

        private async Task ApplySortAsync(bool showLoading = true)
        {
            IReadOnlyList<StorageItemViewModel> visibleItems = GetFilteredItems().ToArray();

            FolderSortMode sortMode = GetSortMode();
            if (RequiresMediaDetails(sortMode))
            {
                if (showLoading)
                {
                    IsLoading = true;
                }

                foreach (StorageItemViewModel item in visibleItems.Where(item => item.IsFile))
                {
                    if (!_isActive) return;
                    await item.LoadSortDetailsAsync();
                }

                if (showLoading)
                {
                    IsLoading = false;
                }
            }

            List<StorageItemViewModel> sortedItems = SortItems(visibleItems, sortMode).ToList();
            Items.Clear();
            foreach (StorageItemViewModel item in sortedItems)
            {
                Items.Add(item);
            }

            _playableItems.Clear();
            _playableItems.AddRange(Items.Where(item => item.Media != null).Select(item => item.Media!));
            IsEmpty = Items.Count == 0;
        }

        private FolderSortMode GetSortMode()
        {
            return (FolderSortMode)ClampSortIndex(SelectedSortIndex);
        }

        private int ClampSortIndex(int value)
        {
            return value >= 0 && value < SortOptions.Length ? value : 0;
        }

        private int ClampThumbnailSizeIndex(int value)
        {
            return value >= 0 && value < ThumbnailSizeOptions.Length ? value : 1;
        }

        private IEnumerable<StorageItemViewModel> GetFilteredItems()
        {
            string query = SearchQuery.Trim();
            if (query.Length == 0)
            {
                return _allItems;
            }

            return _allItems.Where(item =>
                item.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.DisplayCaption.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.Path.Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        private static IEnumerable<StorageItemViewModel> SortItems(IEnumerable<StorageItemViewModel> items, FolderSortMode sortMode)
        {
            return sortMode switch
            {
                FolderSortMode.Newest => items
                    .OrderBy(item => item.IsFile)
                    .ThenByDescending(item => item.SortDate)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                FolderSortMode.Oldest => items
                    .OrderBy(item => item.IsFile)
                    .ThenBy(item => item.SortDate)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                FolderSortMode.Longest => items
                    .OrderBy(item => item.IsFile)
                    .ThenByDescending(item => item.SortDuration)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                FolderSortMode.Shortest => items
                    .OrderBy(item => item.IsFile)
                    .ThenBy(item => item.SortDuration == TimeSpan.Zero)
                    .ThenBy(item => item.SortDuration)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                FolderSortMode.Quality => items
                    .OrderBy(item => item.IsFile)
                    .ThenByDescending(item => item.SortQuality)
                    .ThenByDescending(item => item.SortBitrate)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                FolderSortMode.Favorites => items
                    .OrderByDescending(item => item.Media?.IsFavorite == true)
                    .ThenBy(item => item.IsFile)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase),
                _ => items
                    .OrderBy(item => item.IsFile)
                    .ThenBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            };
        }

        private static bool RequiresMediaDetails(FolderSortMode sortMode)
        {
            return sortMode is FolderSortMode.Newest
                or FolderSortMode.Oldest
                or FolderSortMode.Longest
                or FolderSortMode.Shortest
                or FolderSortMode.Quality;
        }
    }
}
