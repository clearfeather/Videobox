using Windows.Storage;
using Screenbox.Core.Services;

using StorageItemViewModel = Screenbox.Core.ViewModels.StorageItemViewModel;

namespace Screenbox.Core.Factories
{
    public sealed class StorageItemViewModelFactory
    {
        private readonly IFilesService _filesService;
        private readonly MediaViewModelFactory _mediaFactory;
        private readonly IThumbnailService _thumbnailService;
        private readonly IThumbnailLoadingService _thumbnailLoadingService;

        public StorageItemViewModelFactory(IFilesService filesService,
            MediaViewModelFactory mediaFactory,
            IThumbnailService thumbnailService,
            IThumbnailLoadingService thumbnailLoadingService)
        {
            _filesService = filesService;
            _mediaFactory = mediaFactory;
            _thumbnailService = thumbnailService;
            _thumbnailLoadingService = thumbnailLoadingService;
        }

        public StorageItemViewModel GetInstance(IStorageItem storageItem)
        {
            return new StorageItemViewModel(_filesService, _mediaFactory, _thumbnailService, _thumbnailLoadingService, storageItem);
        }
    }
}
