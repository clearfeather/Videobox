#nullable enable

using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Screenbox.Pages;

public sealed partial class TagPage : Page
{
    internal TagPageViewModel ViewModel => (TagPageViewModel)DataContext;

    internal CommonViewModel Common { get; }

    public TagPage()
    {
        InitializeComponent();
        DataContext = Ioc.Default.GetRequiredService<TagPageViewModel>();
        Common = Ioc.Default.GetRequiredService<CommonViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.OnNavigatedTo(e.Parameter);
    }
}
