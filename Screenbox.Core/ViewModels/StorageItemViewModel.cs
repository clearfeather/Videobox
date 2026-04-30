#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Services;
using Windows.Storage.Search;

namespace Screenbox.Core.ViewModels
{
    public sealed partial class StorageItemViewModel : ObservableObject
    {
        public string Name { get; }

        public string DisplayName => Media?.DisplayName ?? Name;

        public string DisplayCaption
        {
            get
            {
                string caption = Media?.DisplayCaption ?? CaptionText;
                if (string.IsNullOrWhiteSpace(Tag))
                {
                    return caption;
                }

                return string.IsNullOrWhiteSpace(caption)
                    ? Tag
                    : $"{caption} | {Tag}";
            }
        }

        public string Path { get; }

        public DateTimeOffset DateCreated { get; }

        public DateTimeOffset SortDate => Media?.MediaInfo.DateModified ?? DateCreated;

        public TimeSpan SortDuration => Media?.Duration ?? TimeSpan.Zero;

        public ulong SortQuality
        {
            get
            {
                if (Media == null) return 0;
                var video = Media.MediaInfo.VideoProperties;
                return (ulong)video.Width * video.Height;
            }
        }

        public uint SortBitrate => Media?.MediaInfo.VideoProperties.Bitrate ?? 0;

        public IStorageItem StorageItem { get; }

        public MediaViewModel? Media { get; }

        public ImageSource? ThumbnailSource => Media?.Thumbnail ?? FolderPreviewThumbnailSource;

        public bool IsFile { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayCaption))]
        private string _captionText;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(DisplayCaption))]
        private string _tag = string.Empty;
        [ObservableProperty] private uint _itemCount;
        [ObservableProperty] private bool _isThumbnailLoading;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ThumbnailSource))]
        private ImageSource? _folderPreviewThumbnailSource;

        private readonly IFilesService _filesService;
        private readonly MediaViewModelFactory _mediaFactory;
        private readonly IThumbnailService _thumbnailService;
        private readonly IThumbnailLoadingService _thumbnailLoadingService;

        public StorageItemViewModel(IFilesService filesService,
            MediaViewModelFactory mediaFactory,
            IThumbnailService thumbnailService,
            IThumbnailLoadingService thumbnailLoadingService,
            IStorageItem storageItem)
        {
            _filesService = filesService;
            _mediaFactory = mediaFactory;
            _thumbnailService = thumbnailService;
            _thumbnailLoadingService = thumbnailLoadingService;
            StorageItem = storageItem;
            _captionText = string.Empty;
            DateCreated = storageItem.DateCreated;

            if (storageItem is StorageFile file)
            {
                IsFile = true;
                Media = mediaFactory.GetSingleton(file);
                Name = Media.Name;
                Path = Media.Location;
            }
            else
            {
                Name = storageItem.Name;
                Path = storageItem.Path;
            }
        }

        public void InvalidateThumbnail()
        {
            Media?.InvalidateThumbnail();
            FolderPreviewThumbnailSource = null;
            OnPropertyChanged(nameof(ThumbnailSource));
        }

        public void InvalidateDisplayText()
        {
            Media?.InvalidateDisplayText();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DisplayCaption));
        }

        public async Task LoadSortDetailsAsync()
        {
            if (Media == null || DetailsAlreadyAvailable()) return;
            await Media.LoadDetailsAsync(_filesService);
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(DisplayCaption));
        }

        public async Task LoadThumbnailAsync()
        {
            if (Media != null)
            {
                if (ThumbnailSource != null || IsThumbnailLoading) return;

                IsThumbnailLoading = true;
                try
                {
                    await Media.LoadThumbnailAsync();
                    OnPropertyChanged(nameof(ThumbnailSource));
                }
                finally
                {
                    IsThumbnailLoading = false;
                }

                return;
            }

            // Folder tiles intentionally stay lightweight. Video thumbnails load
            // when the user opens the folder and those video items become visible.
        }

        public async Task LoadFolderPreviewThumbnailAsync(bool isAlreadyQueued = false)
        {
            if (IsFile || ThumbnailSource != null || (!isAlreadyQueued && IsThumbnailLoading)) return;
            if (StorageItem is not StorageFolder folder) return;

            IsThumbnailLoading = true;
            using IDisposable thumbnailOperation = _thumbnailLoadingService.BeginOperation();
            try
            {
                StorageFile? selectedCoverFile = await _thumbnailService.GetThumbnailFileAsync(folder.Path);
                if (selectedCoverFile != null)
                {
                    ImageSource? cover = await TryLoadImageSourceAsync(selectedCoverFile);
                    if (cover != null)
                    {
                        FolderPreviewThumbnailSource = cover;
                        return;
                    }
                }

                StorageFile? folderCoverFile = await GetFolderCoverFileAsync(folder);
                if (folderCoverFile != null)
                {
                    ImageSource? cover = await TryLoadImageSourceAsync(folderCoverFile);
                    if (cover != null)
                    {
                        FolderPreviewThumbnailSource = cover;
                        return;
                    }
                }

                StorageFile? previewFile = await GetFirstVideoFileAsync(folder);
                if (previewFile == null) return;

                MediaViewModel previewMedia = _mediaFactory.GetSingleton(previewFile);
                await previewMedia.LoadThumbnailAsync();
                FolderPreviewThumbnailSource = previewMedia.Thumbnail;
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
            finally
            {
                IsThumbnailLoading = false;
            }
        }

        private static async Task<ImageSource?> TryLoadImageSourceAsync(StorageFile file)
        {
            try
            {
                using IRandomAccessStream stream = await file.OpenReadAsync();
                BitmapImage image = new();
                await image.SetSourceAsync(stream);
                return image;
            }
            catch (Exception e)
            {
                LogService.Log(e);
                return null;
            }
        }

        private static async Task<StorageFile?> GetFolderCoverFileAsync(StorageFolder folder)
        {
            string[] coverNames =
            {
                "folder.jpg",
                "folder.jpeg",
                "folder.png",
                "folder.webp",
                "cover.jpg",
                "cover.jpeg",
                "cover.png",
                "cover.webp"
            };

            foreach (string coverName in coverNames)
            {
                try
                {
                    if (await folder.TryGetItemAsync(coverName) is StorageFile file)
                    {
                        return file;
                    }
                }
                catch
                {
                    // Keep looking; some folders may deny access to individual items.
                }
            }

            return null;
        }

        private static async Task<StorageFile?> GetFirstVideoFileAsync(StorageFolder folder)
        {
            try
            {
                QueryOptions queryOptions = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedVideoFormats)
                {
                    FolderDepth = FolderDepth.Deep
                };

                StorageFileQueryResult query = folder.CreateFileQueryWithOptions(queryOptions);
                return (await query.GetFilesAsync(0, 1)).FirstOrDefault();
            }
            catch
            {
                return await GetFirstVideoFileRecursivelyAsync(folder);
            }
        }

        private static async Task<StorageFile?> GetFirstVideoFileRecursivelyAsync(StorageFolder folder)
        {
            try
            {
                foreach (StorageFile file in await folder.GetFilesAsync())
                {
                    if (FilesHelpers.SupportedVideoFormats.Contains(file.FileType.ToLowerInvariant()))
                    {
                        return file;
                    }
                }

                foreach (StorageFolder subfolder in await folder.GetFoldersAsync())
                {
                    StorageFile? file = await GetFirstVideoFileRecursivelyAsync(subfolder);
                    if (file != null) return file;
                }
            }
            catch
            {
                // Skip folders that Windows will not let us enumerate.
            }

            return null;
        }

        public async Task UpdateCaptionAsync()
        {
            try
            {
                switch (StorageItem)
                {
                    case StorageFolder folder when !string.IsNullOrEmpty(folder.Path):
                        ItemCount = await _filesService.GetSupportedItemCountAsync(folder);
                        break;
                    case StorageFile file:
                        if (!string.IsNullOrEmpty(Media?.Caption))
                        {
                            CaptionText = Media?.Caption ?? string.Empty;
                        }
                        else
                        {
                            string[] additionalPropertyKeys =
                            {
                                SystemProperties.Music.Artist,
                                SystemProperties.Media.Duration
                            };

                            IDictionary<string, object> additionalProperties =
                                await file.Properties.RetrievePropertiesAsync(additionalPropertyKeys);

                            if (additionalProperties[SystemProperties.Music.Artist] is string[] { Length: > 0 } contributingArtists)
                            {
                                CaptionText = string.Join(", ", contributingArtists);
                            }
                            else if (additionalProperties[SystemProperties.Media.Duration] is ulong ticks and > 0)
                            {
                                TimeSpan duration = TimeSpan.FromTicks((long)ticks);
                                CaptionText = Humanizer.ToDuration(duration);
                            }
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                LogService.Log(e);
            }
        }

        private bool DetailsAlreadyAvailable()
        {
            if (Media == null) return true;
            return Media.DetailsLoaded ||
                   Media.MediaInfo.DateModified != default ||
                   Media.Duration > TimeSpan.Zero ||
                   Media.MediaInfo.VideoProperties.Width > 0;
        }
    }
}
