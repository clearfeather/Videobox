#nullable enable

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Contexts;

public sealed partial class FavoritesContext : ObservableObject
{
    public ObservableCollection<MediaViewModel> Favorites { get; } = new();

    [ObservableProperty] private bool _isLoaded;
}
