#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Screenbox.Pages;

public sealed partial class FavoritesPage : Page
{
    internal FavoritesPageViewModel ViewModel => (FavoritesPageViewModel)DataContext;

    internal CommonViewModel Common { get; }

    public FavoritesPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<FavoritesPageViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.OnNavigatedTo();
    }
}
