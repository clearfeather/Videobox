#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Screenbox.Pages;

public sealed partial class RecentPage : Page
{
    internal RecentPageViewModel ViewModel => (RecentPageViewModel)DataContext;

    internal CommonViewModel Common { get; }

    public RecentPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<RecentPageViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.OnNavigatedTo();
    }
}
