#nullable enable

using Screenbox.Core.Contexts;
using Screenbox.Core.Factories;
using Screenbox.Core.Services;

namespace Screenbox.Core.ViewModels
{
    // To support navigation type matching
    public sealed class FolderListViewPageViewModel : FolderViewPageViewModel
    {
        private readonly INavigationService _navigationService;

        public FolderListViewPageViewModel(IFilesService filesService,
            LibraryContext libraryContext,
            INavigationService navigationService,
            StorageItemViewModelFactory storageVmFactory,
            ITagsService tagsService,
            ISettingsService settingsService) :
            base(filesService, libraryContext, navigationService, storageVmFactory, tagsService, settingsService)
        {
            _navigationService = navigationService;
        }

        protected override void Navigate(object? parameter = null)
        {
            _navigationService.NavigateExisting(typeof(FolderListViewPageViewModel),
                new NavigationMetadata(NavData?.RootViewModelType ?? typeof(FolderListViewPageViewModel), parameter));
        }
    }
}
