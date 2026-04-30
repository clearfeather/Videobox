#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Factories;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Screenbox.Core.ViewModels;

public sealed partial class RecentPageViewModel : ObservableRecipient,
    IRecipient<PlaylistCurrentItemChangedMessage>
{
    public ObservableCollection<MediaViewModel> Recent { get; }

    public bool IsEmpty => Recent.Count == 0 && !IsLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    private readonly MediaViewModelFactory _mediaFactory;
    private readonly IFilesService _filesService;
    private readonly ISettingsService _settingsService;
    private readonly Dictionary<string, string> _pathToMruMappings;

    public RecentPageViewModel(
        MediaViewModelFactory mediaFactory,
        IFilesService filesService,
        ISettingsService settingsService)
    {
        _mediaFactory = mediaFactory;
        _filesService = filesService;
        _settingsService = settingsService;
        _pathToMruMappings = new Dictionary<string, string>();
        Recent = new ObservableCollection<MediaViewModel>();
        Recent.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));

        IsActive = true;
    }

    public void Receive(PlaylistCurrentItemChangedMessage message)
    {
        if (_settingsService.ShowRecent)
        {
            _ = LoadRecentAsync(false);
        }
    }

    public async Task OnNavigatedTo()
    {
        await LoadRecentAsync(true);
    }

    private async Task LoadRecentAsync(bool loadMediaDetails)
    {
        IsLoading = true;

        string[] tokens = StorageApplicationPermissions.MostRecentlyUsedList.Entries
            .OrderByDescending(entry => entry.Metadata)
            .Select(entry => entry.Token)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Take(_settingsService.RecentLimit)
            .ToArray();

        StorageFile?[] files = await Task.WhenAll(tokens.Select(ConvertMruTokenToStorageFileAsync));
        List<(string Token, StorageFile File)> validFiles = tokens
            .Zip(files, (token, file) => (Token: token, File: file))
            .Where(pair => pair.File != null && !pair.File.IsSupportedPlaylist())
            .Select(pair => (pair.Token, pair.File!))
            .ToList();

        Recent.Clear();
        _pathToMruMappings.Clear();
        foreach ((string token, StorageFile file) in validFiles)
        {
            MediaViewModel media = _mediaFactory.GetSingleton(file);
            _pathToMruMappings[media.Location] = token;
            Recent.Add(media);
        }

        if (loadMediaDetails)
        {
            IEnumerable<Task> loadingTasks = Recent.Select(media => media.LoadDetailsAsync(_filesService));
            loadingTasks = Recent.Select(media => media.LoadThumbnailAsync()).Concat(loadingTasks);
            await Task.WhenAll(loadingTasks);
        }

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
        Messenger.SendQueueAndPlay(media, Recent.ToList(), true);
    }

    [RelayCommand]
    private void Remove(MediaViewModel? media)
    {
        if (media == null) return;
        Recent.Remove(media);
        if (_pathToMruMappings.Remove(media.Location, out string token))
        {
            StorageApplicationPermissions.MostRecentlyUsedList.Remove(token);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private static async Task<StorageFile?> ConvertMruTokenToStorageFileAsync(string token)
    {
        try
        {
            return await StorageApplicationPermissions.MostRecentlyUsedList.GetFileAsync(
                token,
                AccessCacheOptions.SuppressAccessTimeUpdate);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (System.IO.FileNotFoundException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Exception e)
        {
            LogService.Log(e);
            return null;
        }
    }
}
