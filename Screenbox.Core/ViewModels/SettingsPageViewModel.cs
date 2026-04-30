#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI;
using Screenbox.Core.Contexts;
using Screenbox.Core.Controllers;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Windows.Devices.Enumeration;
using Windows.Globalization;
using Windows.Storage;
using Windows.Storage.AccessCache;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

namespace Screenbox.Core.ViewModels;

public sealed partial class SettingsPageViewModel : ObservableRecipient
{
    private static readonly StartupDestinationOption[] StartupDestinationValues =
    {
        StartupDestinationOption.Home,
        StartupDestinationOption.Recent,
        StartupDestinationOption.VideoFolders,
        StartupDestinationOption.Favorites,
        StartupDestinationOption.Tag
    };

    [ObservableProperty] private int _playerAutoResize;
    [ObservableProperty] private bool _playerVolumeGesture;
    [ObservableProperty] private bool _playerSeekGesture;
    [ObservableProperty] private bool _playerTapGesture;
    [ObservableProperty] private bool _playerShowControls;
    [ObservableProperty] private bool _playerShowChapters;
    [ObservableProperty] private int _playerControlsHideDelay;
    [ObservableProperty] private int _thumbnailCaptureTimeSeconds;
    [ObservableProperty] private bool _autoLoadThumbnails;
    [ObservableProperty] private bool _useGeneratedVideoThumbnails;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScannedThumbnailSourceSelected))]
    private int _selectedVideoThumbnailSourceIndex;
    [ObservableProperty] private bool _useCustomVideoThumbnails;
    [ObservableProperty] private int _volumeBoost;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartupVolumeFixed))]
    private int _startupVolumeModeIndex;
    [ObservableProperty] private double _startupVolumePercent;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStartupTagSelectionEnabled))]
    private int _startupDestination;
    [ObservableProperty] private string _startupTag;
    [ObservableProperty] private bool _useIndexer;
    [ObservableProperty] private bool _showRecent;
    [ObservableProperty] private int _recentLimit;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AppLockStatus))]
    private bool _appLockEnabled;
    [ObservableProperty] private int _theme;
    [ObservableProperty] private bool _enqueueAllFilesInFolder;
    [ObservableProperty] private bool _autoCleanVideoTitles;
    [ObservableProperty] private bool _restorePlaybackPosition;
    [ObservableProperty] private bool _searchRemovableStorage;
    [ObservableProperty] private bool _advancedMode;
    [ObservableProperty] private int _videoUpscaling;
    [ObservableProperty] private bool _useMultipleInstances;
    [ObservableProperty] private string _globalArguments;
    [ObservableProperty] private bool _isRelaunchRequired;
    [ObservableProperty] private int _selectedLanguage;
    [ObservableProperty] private bool _persistPlaybackPosition;

    public ObservableCollection<StorageFolder> MusicLocations { get; }

    public ObservableCollection<StorageFolder> VideoLocations { get; }

    public ObservableCollection<StorageFolder> RemovableStorageFolders { get; }

    public ObservableCollection<string> AvailableStartupTags { get; }

    public List<LanguageInfo> AvailableLanguages { get; }

    public int[] PlayerControlsHideDelayOptions { get; } = { 1, 2, 3, 4, 5 };

    public int[] ThumbnailCaptureTimeOptions { get; } = { 10, 30, 60, 120, 300, 600 };

    public int[] RecentLimitOptions { get; } = { 6, 12, 24, 48 };

    public string[] VideoThumbnailSourceOptions { get; } = { "Windows default", "Scanned frame" };

    public string[] StartupDestinationOptions { get; } = { "Home", "Recent", "Video folders", "Favorites", "Selected tag" };

    public string[] StartupVolumeModeOptions { get; } = { "Remember last volume", "Muted", "Set percentage" };

    public bool IsStartupTagSelectionEnabled => GetStartupDestinationOption(StartupDestination) == StartupDestinationOption.Tag;

    public bool IsStartupVolumeFixed => GetStartupVolumeMode(StartupVolumeModeIndex) == Screenbox.Core.Enums.StartupVolumeMode.Fixed;

    public bool IsScannedThumbnailSourceSelected => SelectedVideoThumbnailSourceIndex == 1;

    public bool HasAppLockPin => !string.IsNullOrWhiteSpace(_settingsService.AppLockPinHash) &&
                                 !string.IsNullOrWhiteSpace(_settingsService.AppLockPinSalt);

    public string AppLockStatus => HasAppLockPin
        ? "PIN is set"
        : "Set a 4-digit PIN before turning app lock on.";

    private readonly ISettingsService _settingsService;
    private readonly LibraryContext _libraryContext;
    private readonly ILibraryService _libraryService;
    private readonly LibraryController _libraryController;
    private readonly IThumbnailService _thumbnailService;
    private readonly ITagsService _tagsService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _storageDeviceRefreshTimer;
    private readonly DeviceWatcher? _portableStorageDeviceWatcher;
    private readonly LastPositionTracker _lastPositionTracker;
    private static InitialValues? _initialValues;
    private StorageLibrary? _musicLibrary;

    private record InitialValues(string GlobalArguments, bool AdvancedMode, int VideoUpscaling, int Language)
    {
        public string GlobalArguments { get; } = GlobalArguments;
        public bool AdvancedMode { get; } = AdvancedMode;
        public int VideoUpscaling { get; } = VideoUpscaling;
        public int Language { get; } = Language;
    }

    public SettingsPageViewModel(
        ISettingsService settingsService,
        LibraryContext libraryContext,
        ILibraryService libraryService,
        LibraryController libraryController,
        LastPositionTracker lastPositionTracker,
        IThumbnailService thumbnailService,
        ITagsService tagsService)
    {
        _settingsService = settingsService;
        _libraryContext = libraryContext;
        _libraryService = libraryService;
        _libraryController = libraryController;
        _lastPositionTracker = lastPositionTracker;
        _thumbnailService = thumbnailService;
        _tagsService = tagsService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _storageDeviceRefreshTimer = _dispatcherQueue.CreateTimer();
        MusicLocations = new ObservableCollection<StorageFolder>();
        VideoLocations = new ObservableCollection<StorageFolder>();
        RemovableStorageFolders = new ObservableCollection<StorageFolder>();
        AvailableStartupTags = new ObservableCollection<string>();

        IEnumerable<Language> manifestLanguages = ApplicationLanguages.ManifestLanguages.Select(l => new Language(l));
        AvailableLanguages = manifestLanguages.Select(l => new LanguageInfo(l.NativeName, l.LanguageTag))
            .OrderBy(l => l.NativeName, StringComparer.CurrentCultureIgnoreCase)
            .Prepend(new LanguageInfo(string.Empty, string.Empty))
            .ToList();

        if (SystemInformation.IsXbox)
        {
            _portableStorageDeviceWatcher = DeviceInformation.CreateWatcher(DeviceClass.PortableStorageDevice);
            _portableStorageDeviceWatcher.Updated += OnPortableStorageDeviceChanged;
            _portableStorageDeviceWatcher.Removed += OnPortableStorageDeviceChanged;
            _portableStorageDeviceWatcher.Start();
        }

        // Load values
        _playerAutoResize = (int)_settingsService.PlayerAutoResize;
        _playerVolumeGesture = _settingsService.PlayerVolumeGesture;
        _playerSeekGesture = _settingsService.PlayerSeekGesture;
        _playerTapGesture = _settingsService.PlayerTapGesture;
        _playerShowControls = _settingsService.PlayerShowControls;
        _playerShowChapters = _settingsService.PlayerShowChapters;
        _playerControlsHideDelay = _settingsService.PlayerControlsHideDelay;
        _thumbnailCaptureTimeSeconds = _settingsService.ThumbnailCaptureTimeSeconds;
        _autoLoadThumbnails = _settingsService.AutoLoadThumbnails;
        _useGeneratedVideoThumbnails = _settingsService.UseGeneratedVideoThumbnails;
        _selectedVideoThumbnailSourceIndex = GetVideoThumbnailSourceIndex();
        _useCustomVideoThumbnails = _settingsService.UseCustomVideoThumbnails;
        _startupVolumeModeIndex = GetStartupVolumeModeIndex(_settingsService.StartupVolumeMode);
        _startupVolumePercent = _settingsService.StartupVolumePercent;
        _startupDestination = GetStartupDestinationIndex(_settingsService.StartupDestination);
        _startupTag = _settingsService.StartupTag;
        _useIndexer = _settingsService.UseIndexer;
        _showRecent = _settingsService.ShowRecent;
        _recentLimit = _settingsService.RecentLimit;
        _appLockEnabled = _settingsService.AppLockEnabled && HasAppLockPin;
        _persistPlaybackPosition = _settingsService.PersistPlaybackPosition;
        _theme = ((int)_settingsService.Theme + 2) % 3;
        _enqueueAllFilesInFolder = _settingsService.EnqueueAllFilesInFolder;
        _autoCleanVideoTitles = _settingsService.AutoCleanVideoTitles;
        _restorePlaybackPosition = _settingsService.RestorePlaybackPosition;
        _searchRemovableStorage = _settingsService.SearchRemovableStorage;
        _advancedMode = _settingsService.AdvancedMode;
        _useMultipleInstances = _settingsService.UseMultipleInstances;
        _videoUpscaling = (int)_settingsService.VideoUpscale;
        _globalArguments = _settingsService.GlobalArguments;
        int maxVolume = _settingsService.MaxVolume;
        _volumeBoost = maxVolume switch
        {
            >= 200 => 3,
            >= 150 => 2,
            >= 125 => 1,
            _ => 0
        };

        string currentLanguage = ApplicationLanguages.PrimaryLanguageOverride;
        _selectedLanguage = AvailableLanguages.FindIndex(l => l.LanguageTag.Equals(currentLanguage));

        // Setting initial values for relaunch check
        _initialValues ??= new InitialValues(_globalArguments, _advancedMode, _videoUpscaling, _selectedLanguage);
        CheckForRelaunch();

        IsActive = true;
    }

    partial void OnThemeChanged(int value)
    {
        // The recommended theme option order is Light, Dark, System
        // So we need to map the value to the correct ThemeOption
        _settingsService.Theme = (ThemeOption)((value + 1) % 3);
        Messenger.Send(new SettingsChangedMessage(nameof(Theme), typeof(SettingsPageViewModel)));
    }

    partial void OnSelectedLanguageChanged(int value)
    {
        if (value <= 0)
        {
            ApplicationLanguages.PrimaryLanguageOverride = string.Empty;
            CheckForRelaunch();
            return;
        }

        // If the value is out of bounds, do nothing
        if (value >= AvailableLanguages.Count) return;
        ApplicationLanguages.PrimaryLanguageOverride = AvailableLanguages[value].LanguageTag;
        CheckForRelaunch();
    }

    partial void OnPlayerAutoResizeChanged(int value)
    {
        _settingsService.PlayerAutoResize = (PlayerAutoResizeOption)value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerAutoResize), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerVolumeGestureChanged(bool value)
    {
        _settingsService.PlayerVolumeGesture = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerVolumeGesture), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerSeekGestureChanged(bool value)
    {
        _settingsService.PlayerSeekGesture = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerSeekGesture), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerTapGestureChanged(bool value)
    {
        _settingsService.PlayerTapGesture = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerTapGesture), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerShowControlsChanged(bool value)
    {
        _settingsService.PlayerShowControls = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerShowControls), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerShowChaptersChanged(bool value)
    {
        _settingsService.PlayerShowChapters = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerShowChapters), typeof(SettingsPageViewModel)));
    }

    partial void OnPlayerControlsHideDelayChanged(int value)
    {
        _settingsService.PlayerControlsHideDelay = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PlayerControlsHideDelay), typeof(SettingsPageViewModel)));
    }

    partial void OnThumbnailCaptureTimeSecondsChanged(int value)
    {
        _settingsService.ThumbnailCaptureTimeSeconds = value;
        Messenger.Send(new SettingsChangedMessage(nameof(ThumbnailCaptureTimeSeconds), typeof(SettingsPageViewModel)));
        InvalidateVideoThumbnails();
    }

    partial void OnSelectedVideoThumbnailSourceIndexChanged(int value)
    {
        bool useScannedFrame = value == 1;
        _settingsService.AutoLoadThumbnails = useScannedFrame;
        _settingsService.UseGeneratedVideoThumbnails = useScannedFrame;
        _autoLoadThumbnails = useScannedFrame;
        _useGeneratedVideoThumbnails = useScannedFrame;
        OnPropertyChanged(nameof(AutoLoadThumbnails));
        OnPropertyChanged(nameof(UseGeneratedVideoThumbnails));
        Messenger.Send(new SettingsChangedMessage(nameof(SelectedVideoThumbnailSourceIndex), typeof(SettingsPageViewModel)));
        Messenger.Send(new SettingsChangedMessage(nameof(AutoLoadThumbnails), typeof(SettingsPageViewModel)));
        Messenger.Send(new SettingsChangedMessage(nameof(UseGeneratedVideoThumbnails), typeof(SettingsPageViewModel)));
        InvalidateVideoThumbnails();
    }

    partial void OnUseCustomVideoThumbnailsChanged(bool value)
    {
        _settingsService.UseCustomVideoThumbnails = value;
        Messenger.Send(new SettingsChangedMessage(nameof(UseCustomVideoThumbnails), typeof(SettingsPageViewModel)));
        InvalidateVideoThumbnails();
    }

    partial void OnAutoLoadThumbnailsChanged(bool value)
    {
        _settingsService.AutoLoadThumbnails = value;
        Messenger.Send(new SettingsChangedMessage(nameof(AutoLoadThumbnails), typeof(SettingsPageViewModel)));
        InvalidateVideoThumbnails();
    }

    partial void OnUseGeneratedVideoThumbnailsChanged(bool value)
    {
        _settingsService.UseGeneratedVideoThumbnails = value;
        Messenger.Send(new SettingsChangedMessage(nameof(UseGeneratedVideoThumbnails), typeof(SettingsPageViewModel)));
        InvalidateVideoThumbnails();
    }

    partial void OnStartupDestinationChanged(int value)
    {
        _settingsService.StartupDestination = GetStartupDestinationOption(value);
        Messenger.Send(new SettingsChangedMessage(nameof(StartupDestination), typeof(SettingsPageViewModel)));
    }

    partial void OnStartupTagChanged(string value)
    {
        _settingsService.StartupTag = value ?? string.Empty;
        Messenger.Send(new SettingsChangedMessage(nameof(StartupTag), typeof(SettingsPageViewModel)));
    }

    private static StartupDestinationOption GetStartupDestinationOption(int index)
    {
        return index >= 0 && index < StartupDestinationValues.Length
            ? StartupDestinationValues[index]
            : StartupDestinationOption.Home;
    }

    private static int GetStartupDestinationIndex(StartupDestinationOption option)
    {
        int index = Array.IndexOf(StartupDestinationValues, option);
        return index >= 0 ? index : 0;
    }

    private static StartupVolumeMode GetStartupVolumeMode(int index)
    {
        return index switch
        {
            1 => Screenbox.Core.Enums.StartupVolumeMode.Muted,
            2 => Screenbox.Core.Enums.StartupVolumeMode.Fixed,
            _ => Screenbox.Core.Enums.StartupVolumeMode.RememberLast
        };
    }

    private static int GetStartupVolumeModeIndex(StartupVolumeMode mode)
    {
        return mode switch
        {
            Screenbox.Core.Enums.StartupVolumeMode.Muted => 1,
            Screenbox.Core.Enums.StartupVolumeMode.Fixed => 2,
            _ => 0
        };
    }

    partial void OnUseIndexerChanged(bool value)
    {
        _settingsService.UseIndexer = value;
        Messenger.Send(new SettingsChangedMessage(nameof(UseIndexer), typeof(SettingsPageViewModel)));

        try
        {
            _ = _libraryController.RefreshWatchersAsync();
        }
        catch (Exception)
        {
            // pass
        }
    }

    partial void OnShowRecentChanged(bool value)
    {
        _settingsService.ShowRecent = value;
        Messenger.Send(new SettingsChangedMessage(nameof(ShowRecent), typeof(SettingsPageViewModel)));
    }

    partial void OnRecentLimitChanged(int value)
    {
        _settingsService.RecentLimit = value;
        Messenger.Send(new SettingsChangedMessage(nameof(RecentLimit), typeof(SettingsPageViewModel)));
    }

    partial void OnAppLockEnabledChanged(bool value)
    {
        if (value && !HasAppLockPin)
        {
            _appLockEnabled = false;
            OnPropertyChanged(nameof(AppLockEnabled));
            return;
        }

        _settingsService.AppLockEnabled = value;
        Messenger.Send(new SettingsChangedMessage(nameof(AppLockEnabled), typeof(SettingsPageViewModel)));
    }

    partial void OnEnqueueAllFilesInFolderChanged(bool value)
    {
        _settingsService.EnqueueAllFilesInFolder = value;
        Messenger.Send(new SettingsChangedMessage(nameof(EnqueueAllFilesInFolder), typeof(SettingsPageViewModel)));
    }

    partial void OnAutoCleanVideoTitlesChanged(bool value)
    {
        _settingsService.AutoCleanVideoTitles = value;
        Messenger.Send(new SettingsChangedMessage(nameof(AutoCleanVideoTitles), typeof(SettingsPageViewModel)));
        foreach (MediaViewModel video in _libraryContext.Videos)
        {
            video.InvalidateDisplayText();
        }
    }

    partial void OnRestorePlaybackPositionChanged(bool value)
    {
        _settingsService.RestorePlaybackPosition = value;
        Messenger.Send(new SettingsChangedMessage(nameof(RestorePlaybackPosition), typeof(SettingsPageViewModel)));
    }

    async partial void OnSearchRemovableStorageChanged(bool value)
    {
        _settingsService.SearchRemovableStorage = value;
        Messenger.Send(new SettingsChangedMessage(nameof(SearchRemovableStorage), typeof(SettingsPageViewModel)));

        if (SystemInformation.IsXbox && RemovableStorageFolders.Count > 0)
        {
            await RefreshLibrariesAsync();
        }
    }

    partial void OnVolumeBoostChanged(int value)
    {
        _settingsService.MaxVolume = value switch
        {
            3 => 200,
            2 => 150,
            1 => 125,
            _ => 100
        };
        Messenger.Send(new SettingsChangedMessage(nameof(VolumeBoost), typeof(SettingsPageViewModel)));
    }

    partial void OnStartupVolumeModeIndexChanged(int value)
    {
        _settingsService.StartupVolumeMode = GetStartupVolumeMode(value);
        Messenger.Send(new SettingsChangedMessage(nameof(StartupVolumeModeIndex), typeof(SettingsPageViewModel)));
    }

    partial void OnStartupVolumePercentChanged(double value)
    {
        _settingsService.StartupVolumePercent = (int)Math.Round(Math.Clamp(value, 0d, 100d));
        Messenger.Send(new SettingsChangedMessage(nameof(StartupVolumePercent), typeof(SettingsPageViewModel)));
    }

    partial void OnAdvancedModeChanged(bool value)
    {
        _settingsService.AdvancedMode = value;
        Messenger.Send(new SettingsChangedMessage(nameof(AdvancedMode), typeof(SettingsPageViewModel)));
        CheckForRelaunch();
    }

    partial void OnVideoUpscalingChanged(int value)
    {
        _settingsService.VideoUpscale = (VideoUpscaleOption)value;
        Messenger.Send(new SettingsChangedMessage(nameof(VideoUpscaling), typeof(SettingsPageViewModel)));
        CheckForRelaunch();
    }

    partial void OnUseMultipleInstancesChanged(bool value)
    {
        _settingsService.UseMultipleInstances = value;
        Messenger.Send(new SettingsChangedMessage(nameof(UseMultipleInstances), typeof(SettingsPageViewModel)));
    }

    partial void OnGlobalArgumentsChanged(string value)
    {
        // No need to broadcast SettingsChangedMessage for this option
        if (value != _settingsService.GlobalArguments)
        {
            _settingsService.GlobalArguments = value;
        }

        GlobalArguments = _settingsService.GlobalArguments;
        CheckForRelaunch();
    }

    partial void OnPersistPlaybackPositionChanged(bool value)
    {
        _settingsService.PersistPlaybackPosition = value;
        Messenger.Send(new SettingsChangedMessage(nameof(PersistPlaybackPosition), typeof(SettingsPageViewModel)));
    }

    [RelayCommand]
    private async Task RefreshLibrariesAsync()
    {
        await Task.WhenAll(RefreshMusicLibrary(), RefreshVideosLibrary());
    }

    [RelayCommand]
    private async Task AddVideosFolderAsync()
    {
        StorageFolder? folder = await _libraryService.AddVideoLibraryFolderAsync();
        if (folder == null) return;

        await ReloadVideoLocationsAsync();
        await RefreshVideosLibrary();
        Messenger.Send(new RefreshFolderMessage());
    }

    [RelayCommand]
    private async Task RemoveVideosFolderAsync(StorageFolder folder)
    {
        try
        {
            await _libraryService.RemoveVideoLibraryFolderAsync(folder);
            await ReloadVideoLocationsAsync();
            await RefreshVideosLibrary();
            Messenger.Send(new RefreshFolderMessage());
        }
        catch (Exception)
        {
            // System.Exception: The remote procedure call was cancelled.
            // pass
        }
    }

    [RelayCommand]
    private async Task AddMusicFolderAsync()
    {
        if (_musicLibrary == null) return;
        await _musicLibrary.RequestAddFolderAsync();
    }

    [RelayCommand]
    private async Task RemoveMusicFolderAsync(StorageFolder folder)
    {
        if (_musicLibrary == null) return;
        try
        {
            await _musicLibrary.RequestRemoveFolderAsync(folder);
        }
        catch (Exception)
        {
            // System.Exception: The remote procedure call was cancelled.
            // pass
        }
    }

    [RelayCommand]
    private void ClearRecentHistory()
    {
        StorageApplicationPermissions.MostRecentlyUsedList.Clear();
    }

    [RelayCommand]
    private async Task ClearPlaybackPositionHistoryAsync()
    {
        try
        {
            _lastPositionTracker.ClearAll();
            await _lastPositionTracker.SaveToDiskAsync();
        }
        catch (Exception)
        {
            // pass
        }
    }

    [RelayCommand]
    private async Task ClearThumbnailCacheAsync()
    {
        ContentDialog dialog = new()
        {
            Title = "Clear thumbnail cache?",
            Content = "VideoBox will remove generated video thumbnail frames. Thumbnails will be recreated the next time they are needed.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        await _thumbnailService.ClearGeneratedThumbnailsAsync();
        InvalidateVideoThumbnails();
    }

    [RelayCommand]
    private async Task ClearSelectedFrameThumbnailsAsync()
    {
        ContentDialog dialog = new()
        {
            Title = "Clear user selected custom thumbnails?",
            Content = "VideoBox will remove user selected custom thumbnails for videos in your library. Folder covers will be kept.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        IReadOnlyList<string> videoLocations = _libraryContext.Videos
            .Select(video => video.Location)
            .Where(location => !string.IsNullOrWhiteSpace(location))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        await Task.WhenAll(videoLocations.Select(location => _thumbnailService.DeleteThumbnailAsync(location)));
        InvalidateVideoThumbnails();
    }

    [RelayCommand]
    private async Task ClearTagsAsync()
    {
        ContentDialog dialog = new()
        {
            Title = "Clear all tags?",
            Content = "This removes every saved tag and tagged category from the left menu.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        IReadOnlyList<string> tags = await _tagsService.ClearAllTagsAsync();
        StartupTag = string.Empty;
        AvailableStartupTags.Clear();
        Messenger.Send(new TagsChangedMessage(tags));
    }

    [RelayCommand]
    private async Task SetAppLockPinAsync()
    {
        string? pin = await PromptForPinAsync("Set app lock PIN", "Save");
        if (pin == null)
        {
            return;
        }

        string salt = PinLockHelper.CreateSalt();
        _settingsService.AppLockPinSalt = salt;
        _settingsService.AppLockPinHash = PinLockHelper.HashPin(pin, salt);
        OnPropertyChanged(nameof(HasAppLockPin));
        OnPropertyChanged(nameof(AppLockStatus));

        AppLockEnabled = true;
    }

    [RelayCommand]
    private async Task ClearAppLockPinAsync()
    {
        if (!HasAppLockPin)
        {
            return;
        }

        ContentDialog dialog = new()
        {
            Title = "Clear app lock PIN?",
            Content = "VideoBox will no longer ask for a PIN when it opens.",
            PrimaryButtonText = "Clear",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        ContentDialogResult result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return;
        }

        AppLockEnabled = false;
        _settingsService.AppLockPinHash = string.Empty;
        _settingsService.AppLockPinSalt = string.Empty;
        OnPropertyChanged(nameof(HasAppLockPin));
        OnPropertyChanged(nameof(AppLockStatus));
    }

    private void InvalidateVideoThumbnails()
    {
        foreach (MediaViewModel video in _libraryContext.Videos)
        {
            video.InvalidateThumbnail();
        }

        Messenger.Send(new ThumbnailCacheInvalidatedMessage());
    }

    private int GetVideoThumbnailSourceIndex()
    {
        return _settingsService.AutoLoadThumbnails && _settingsService.UseGeneratedVideoThumbnails ? 1 : 0;
    }

    private static async Task<string?> PromptForPinAsync(string title, string primaryButtonText)
    {
        PasswordBox pinBox = new()
        {
            MaxLength = 4,
            PlaceholderText = "4-digit PIN"
        };
        InputScope inputScope = new();
        inputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
        pinBox.InputScope = inputScope;

        TextBlock errorText = new()
        {
            Margin = new Thickness(0, 8, 0, 0),
            Text = string.Empty
        };

        StackPanel panel = new()
        {
            Children =
            {
                pinBox,
                errorText
            }
        };

        ContentDialog dialog = new()
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (PinLockHelper.IsValidPin(pinBox.Password))
            {
                return;
            }

            args.Cancel = true;
            errorText.Text = "Enter exactly 4 digits.";
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? pinBox.Password : null;
    }

    public void OnNavigatedFrom()
    {
        if (SystemInformation.IsXbox)
            _portableStorageDeviceWatcher?.Stop();
    }

    public async Task LoadLibraryLocations()
    {
        await ReloadVideoLocationsAsync();

        if (_musicLibrary == null)
        {
            if (_libraryContext.MusicLibrary == null)
            {
                try
                {
                    _libraryContext.MusicLibrary = await _libraryService.InitializeMusicLibraryAsync();
                }
                catch (Exception)
                {
                    // pass
                }
            }

            _musicLibrary = _libraryContext.MusicLibrary;
            if (_musicLibrary != null)
            {
                _musicLibrary.DefinitionChanged += LibraryOnDefinitionChanged;
            }
        }

        UpdateLibraryLocations();
        await UpdateRemovableStorageFoldersAsync();
    }

    public async Task LoadStartupTagsAsync()
    {
        IReadOnlyList<string> tagNames = await _tagsService.LoadTagNamesAsync();
        AvailableStartupTags.Clear();

        foreach (string tagName in tagNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
        {
            AvailableStartupTags.Add(tagName);
        }

        if (!string.IsNullOrWhiteSpace(StartupTag) &&
            !AvailableStartupTags.Contains(StartupTag, StringComparer.CurrentCultureIgnoreCase))
        {
            AvailableStartupTags.Add(StartupTag);
        }
    }

    private void LibraryOnDefinitionChanged(StorageLibrary sender, object args)
    {
        _dispatcherQueue.TryEnqueue(UpdateLibraryLocations);
    }

    private void OnPortableStorageDeviceChanged(DeviceWatcher sender, DeviceInformationUpdate args)
    {
        async void RefreshAction() => await UpdateRemovableStorageFoldersAsync();
        _storageDeviceRefreshTimer.Debounce(RefreshAction, TimeSpan.FromMilliseconds(500));
    }

    private void UpdateLibraryLocations()
    {
        VideoLocations.Clear();
        foreach (StorageFolder folder in _libraryContext.VideoFolders)
        {
            VideoLocations.Add(folder);
        }

        if (_musicLibrary != null)
        {
            MusicLocations.Clear();

            foreach (StorageFolder folder in _musicLibrary.Folders)
            {
                MusicLocations.Add(folder);
            }
        }
    }

    private async Task ReloadVideoLocationsAsync()
    {
        _libraryContext.VideoFolders = (await _libraryService.GetVideoLibraryFoldersAsync()).ToList();
        UpdateLibraryLocations();
    }

    private async Task UpdateRemovableStorageFoldersAsync()
    {
        if (SystemInformation.IsXbox)
        {
            RemovableStorageFolders.Clear();
            var accessStatus = await KnownFolders.RequestAccessAsync(KnownFolderId.RemovableDevices);
            if (accessStatus != KnownFoldersAccessStatus.Allowed)
                return;

            foreach (StorageFolder folder in await KnownFolders.RemovableDevices.GetFoldersAsync())
            {
                RemovableStorageFolders.Add(folder);
            }
        }
    }

    private async Task RefreshMusicLibrary()
    {
        try
        {
            await _libraryService.FetchMusicAsync(_libraryContext, false);
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Music));
        }
        catch (Exception e)
        {
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
        }
    }

    private async Task RefreshVideosLibrary()
    {
        try
        {
            await _libraryService.FetchVideosAsync(_libraryContext, false);
        }
        catch (UnauthorizedAccessException)
        {
            Messenger.Send(new RaiseLibraryAccessDeniedNotificationMessage(KnownLibraryId.Videos));
        }
        catch (Exception e)
        {
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
        }
    }

    private void CheckForRelaunch()
    {
        if (_initialValues == null) return;

        // Check if upscaling mode has been changed
        bool upscalingChanged = _initialValues.VideoUpscaling != VideoUpscaling;

        // Check if app language has been changed
        bool languageChanged = _initialValues.Language != SelectedLanguage;

        // Check if global arguments have been changed
        bool argsChanged = _initialValues.GlobalArguments != _settingsService.GlobalArguments;

        // Check if advanced mode has been changed
        bool modeChanged = _initialValues.AdvancedMode != AdvancedMode;

        // Check if there are any global arguments set
        bool hasArgs = _settingsService.GlobalArguments.Length > 0;

        // Check if advanced mode is on, and if global arguments are set
        bool whenOn = modeChanged && AdvancedMode && hasArgs;

        // Check if advanced mode is off, and if global arguments are set or have been removed
        bool whenOff = modeChanged && !AdvancedMode && ((!hasArgs && argsChanged) || hasArgs);

        // Require relaunch when advanced mode is on and global arguments have been changed
        bool whenOnAndChanged = AdvancedMode && argsChanged;

        // Combine everything
        IsRelaunchRequired = upscalingChanged || languageChanged || whenOn || whenOff || whenOnAndChanged;
    }
}
