using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using System.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class AllVideosPage : Page
    {
        internal AllVideosPageViewModel ViewModel => (AllVideosPageViewModel)DataContext;

        internal CommonViewModel Common { get; }

        public AllVideosPage()
        {
            this.InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<AllVideosPageViewModel>();
            Common = Ioc.Default.GetRequiredService<CommonViewModel>();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.UpdateVideos();
        }

        private void SelectionModeToggleButton_OnClick(object sender, RoutedEventArgs e)
        {
            bool selectionEnabled = SelectionModeToggleButton.IsChecked == true;

            if (selectionEnabled)
            {
                VideosGridView.SelectionMode = ListViewSelectionMode.Multiple;
                VideosGridView.IsItemClickEnabled = false;
            }
            else
            {
                ClearSelectedVideos();
                VideosGridView.SelectionMode = ListViewSelectionMode.None;
                VideosGridView.IsItemClickEnabled = true;
            }

            AddTagsToSelectionButton.Visibility = selectionEnabled ? Visibility.Visible : Visibility.Collapsed;
            UpdateSelectionActionState();
        }

        private void AddTagsToSelectionButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (Common.AddTagsToItemsCommand.CanExecute(VideosGridView.SelectedItems))
            {
                Common.AddTagsToItemsCommand.Execute(VideosGridView.SelectedItems);
            }
        }

        private void VideosGridView_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionActionState();
        }

        private void UpdateSelectionActionState()
        {
            AddTagsToSelectionButton.IsEnabled = VideosGridView.SelectedItems
                .OfType<MediaViewModel>()
                .Any();
        }

        private void ClearSelectedVideos()
        {
            if (VideosGridView.SelectedItems.Count > 0)
            {
                VideosGridView.SelectedItems.Clear();
            }
        }
    }
}
