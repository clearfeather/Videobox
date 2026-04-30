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

namespace Screenbox.Core.ViewModels;

public sealed partial class TagPageViewModel : ObservableObject
{
    public ObservableCollection<StorageItemViewModel> Items { get; } = new();

    public string TagName { get; private set; } = string.Empty;

    public string TitleText => string.IsNullOrWhiteSpace(TagName) ? "Tag" : TagName;

    public bool IsEmpty => Items.Count == 0 && !IsLoading;

    [ObservableProperty] private bool _isLoading;

    private readonly ITagsService _tagsService;
    private readonly INavigationService _navigationService;
    private readonly StorageItemViewModelFactory _storageVmFactory;
    private readonly List<MediaViewModel> _playableItems = new();

    public TagPageViewModel(ITagsService tagsService,
        INavigationService navigationService,
        StorageItemViewModelFactory storageVmFactory)
    {
        _tagsService = tagsService;
        _navigationService = navigationService;
        _storageVmFactory = storageVmFactory;
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public async Task OnNavigatedTo(object? parameter)
    {
        TagName = parameter as string ?? string.Empty;
        OnPropertyChanged(nameof(TitleText));

        IsLoading = true;
        Items.Clear();
        _playableItems.Clear();

        IReadOnlyList<IStorageItem> taggedItems = await _tagsService.LoadTaggedItemsAsync(TagName);
        foreach (IStorageItem storageItem in taggedItems)
        {
            StorageItemViewModel item = _storageVmFactory.GetInstance(storageItem);
            Items.Add(item);
            await item.UpdateCaptionAsync();
            if (item.Media != null)
            {
                _playableItems.Add(item.Media);
            }
        }

        MediaTitleFormatter.LearnCommonStudioPrefixes(_playableItems);
        foreach (StorageItemViewModel item in Items)
        {
            item.InvalidateDisplayText();
        }

        IsLoading = false;
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private void Click(StorageItemViewModel item)
    {
        if (item.Media != null)
        {
            WeakReferenceMessenger.Default.Send(new SelectedMediaChangedMessage(item.Media));
            Play(item);
        }
        else if (item.StorageItem is StorageFolder folder)
        {
            _navigationService.Navigate(typeof(FolderViewPageViewModel), new[] { folder });
        }
    }

    [RelayCommand]
    private void Play(StorageItemViewModel item)
    {
        if (item.Media == null) return;
        WeakReferenceMessenger.Default.SendQueueAndPlay(item.Media, _playableItems, true);
    }

    [RelayCommand]
    private void PlayNext(StorageItemViewModel item)
    {
        if (item.Media == null) return;
        WeakReferenceMessenger.Default.SendPlayNext(item.Media);
    }

    [RelayCommand]
    private void AddToQueue(StorageItemViewModel item)
    {
        if (item.Media == null) return;
        WeakReferenceMessenger.Default.SendAddToQueue(item.Media);
    }

    [RelayCommand]
    private async Task RemoveFromTagAsync(StorageItemViewModel item)
    {
        IReadOnlyList<string> tags = await _tagsService.RemoveTagAsync(TagName, item.StorageItem);
        Items.Remove(item);
        WeakReferenceMessenger.Default.Send(new TagsChangedMessage(tags));
        OnPropertyChanged(nameof(IsEmpty));
    }
}
