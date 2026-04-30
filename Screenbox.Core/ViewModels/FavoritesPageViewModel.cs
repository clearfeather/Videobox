#nullable enable

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;

namespace Screenbox.Core.ViewModels;

public sealed partial class FavoritesPageViewModel : ObservableRecipient
{
    public ObservableCollection<MediaViewModel> Favorites => _favoritesContext.Favorites;

    public bool IsEmpty => Favorites.Count == 0 && !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    private readonly FavoritesContext _favoritesContext;
    private readonly IFavoritesService _favoritesService;

    public FavoritesPageViewModel(FavoritesContext favoritesContext, IFavoritesService favoritesService)
    {
        _favoritesContext = favoritesContext;
        _favoritesService = favoritesService;
        Favorites.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task OnNavigatedTo()
    {
        if (_favoritesContext.IsLoaded) return;

        IsLoading = true;
        Favorites.Clear();
        foreach (MediaViewModel media in await _favoritesService.LoadFavoritesAsync())
        {
            Favorites.Add(media);
        }

        _favoritesContext.IsLoaded = true;
        IsLoading = false;
    }

    [RelayCommand]
    private void Select(MediaViewModel? media)
    {
        if (media == null) return;
        Messenger.Send(new SelectedMediaChangedMessage(media));
        Play(media);
    }

    [RelayCommand]
    private void Play(MediaViewModel? media)
    {
        if (media == null) return;
        Messenger.SendQueueAndPlay(media, Favorites.ToList(), true);
    }

    [RelayCommand]
    private async Task RemoveAsync(MediaViewModel? media)
    {
        if (media == null) return;
        Favorites.Remove(media);
        media.IsFavorite = false;
        await _favoritesService.SaveFavoritesAsync(Favorites);
    }
}
