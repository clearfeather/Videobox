using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Xaml.Interactivity;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Behaviors
{
    internal class ThumbnailGridViewBehavior : Behavior<GridView>
    {
        private readonly ISettingsService _settingsService;

        public ThumbnailGridViewBehavior()
        {
            _settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
        }

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.Loaded += OnLoaded;
            AssociatedObject.ContainerContentChanging += OnContainerContentChanging;
            WeakReferenceMessenger.Default.Register<ThumbnailGridViewBehavior, ThumbnailCacheInvalidatedMessage>(
                this,
                static (recipient, _) => recipient.ReloadVisibleThumbnails());
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.Loaded -= OnLoaded;
            AssociatedObject.ContainerContentChanging -= OnContainerContentChanging;
            WeakReferenceMessenger.Default.Unregister<ThumbnailCacheInvalidatedMessage>(this);
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            ReloadVisibleThumbnails();
        }

        private async void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.Phase != 0) return;
            await LoadThumbnailAsync(args.Item);
        }

        private async void ReloadVisibleThumbnails()
        {
            object[] visibleItems = AssociatedObject.Items
                .Cast<object>()
                .Where(item => AssociatedObject.ContainerFromItem(item) != null)
                .ToArray();

            foreach (object item in visibleItems)
            {
                await LoadThumbnailAsync(item, true);
            }
        }

        private async Task LoadThumbnailAsync(object item, bool invalidate = false)
        {
            switch (item)
            {
                case AlbumViewModel album:
                    if (!_settingsService.AutoLoadThumbnails) return;
                    await album.LoadAlbumArtAsync();
                    break;
                case MediaViewModel media:
                    if (invalidate)
                    {
                        media.InvalidateThumbnail();
                    }

                    await media.LoadThumbnailAsync();
                    break;
                case StorageItemViewModel storageItem:
                    if (!invalidate)
                    {
                        await storageItem.UpdateCaptionAsync();
                    }

                    if (invalidate)
                    {
                        storageItem.InvalidateThumbnail();
                    }

                    await storageItem.LoadThumbnailAsync();
                    break;
            }
        }
    }
}
