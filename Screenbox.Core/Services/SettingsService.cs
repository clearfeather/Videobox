#nullable enable

using System;
using System.Linq;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Windows.Foundation.Collections;
using Windows.Media;
using Windows.Storage;

namespace Screenbox.Core.Services;

public sealed class SettingsService : ISettingsService
{
    private static IPropertySet SettingsStorage => ApplicationData.Current.LocalSettings.Values;

    private const string GeneralThemeKey = "General/Theme";
    private const string PlayerAutoResizeKey = "Player/AutoResize";
    private const string PlayerVolumeGestureKey = "Player/Gesture/Volume";
    private const string PlayerSeekGestureKey = "Player/Gesture/Seek";
    private const string PlayerTapGestureKey = "Player/Gesture/Tap";
    private const string PlayerShowControlsKey = "Player/ShowControls";
    private const string PlayerControlsHideDelayKey = "Player/ControlsHideDelay";
    private const string PlayerThumbnailCaptureTimeKey = "Player/ThumbnailCaptureTime";
    private const string PlayerThumbnailCaptureTimeDefaultMigratedKey = "Player/ThumbnailCaptureTimeDefaultMigrated";
    private const string PlayerAutoLoadThumbnailsKey = "Player/AutoLoadThumbnails";
    private const string PlayerUseGeneratedVideoThumbnailsKey = "Player/UseGeneratedVideoThumbnails";
    private const string PlayerUseCustomVideoThumbnailsKey = "Player/UseCustomVideoThumbnails";
    private const string GeneralStartupDestinationKey = "General/StartupDestination";
    private const string GeneralStartupTagKey = "General/StartupTag";
    private const string PrivacyAppLockEnabledKey = "Privacy/AppLockEnabled";
    private const string PrivacyAppLockPinHashKey = "Privacy/AppLockPinHash";
    private const string PrivacyAppLockPinSaltKey = "Privacy/AppLockPinSalt";
    private const string GeneralAutoCleanVideoTitlesKey = "General/AutoCleanVideoTitles";
    private const string PlayerLivelyPathKey = "Player/Lively/Path";
    private const string PlayerStartupVolumeModeKey = "Player/StartupVolumeMode";
    private const string PlayerStartupVolumePercentKey = "Player/StartupVolumePercent";
    private const string LibrariesUseIndexerKey = "Libraries/UseIndexer";
    private const string LibrariesSearchRemovableStorageKey = "Libraries/SearchRemovableStorage";
    private const string GeneralShowRecent = "General/ShowRecent";
    private const string GeneralRecentLimit = "General/RecentLimit";
    private const string GeneralNavigationPaneWidth = "General/NavigationPaneWidth";
    private const string GeneralVideoFoldersSortIndex = "General/VideoFoldersSortIndex";
    private const string GeneralAllVideosSortIndex = "General/AllVideosSortIndex";
    private const string GeneralVideoFoldersThumbnailSizeIndex = "General/VideoFoldersThumbnailSizeIndex";
    private const string GeneralAllVideosThumbnailSizeIndex = "General/AllVideosThumbnailSizeIndex";
    private const string GeneralEnqueueAllInFolder = "General/EnqueueAllInFolder";
    private const string GeneralRestorePlaybackPosition = "General/RestorePlaybackPosition";
    private const string AdvancedModeKey = "Advanced/IsEnabled";
    private const string AdvancedVideoUpscaleKey = "Advanced/VideoUpscale";
    private const string AdvancedMultipleInstancesKey = "Advanced/MultipleInstances";
    private const string GlobalArgumentsKey = "Values/GlobalArguments";
    private const string PersistentVolumeKey = "Values/Volume";
    private const string MaxVolumeKey = "Values/MaxVolume";
    private const string PersistentRepeatModeKey = "Values/RepeatMode";
    private const string PersistentSubtitleLanguageKey = "Values/SubtitleLanguage";
    private const string PlayerShowChaptersKey = "Player/ShowChapters";
    private const string PrivacyPersistPlaybackPosition = "Privacy/PersistPlaybackPosition";

    public bool UseIndexer
    {
        get => GetValue<bool>(LibrariesUseIndexerKey);
        set => SetValue(LibrariesUseIndexerKey, value);
    }

    public ThemeOption Theme
    {
        get => (ThemeOption)GetValue<int>(GeneralThemeKey);
        set => SetValue(GeneralThemeKey, (int)value);
    }

    public PlayerAutoResizeOption PlayerAutoResize
    {
        get => (PlayerAutoResizeOption)GetValue<int>(PlayerAutoResizeKey);
        set => SetValue(PlayerAutoResizeKey, (int)value);
    }

    public bool PlayerVolumeGesture
    {
        get => GetValue<bool>(PlayerVolumeGestureKey);
        set => SetValue(PlayerVolumeGestureKey, value);
    }

    public bool PlayerSeekGesture
    {
        get => GetValue<bool>(PlayerSeekGestureKey);
        set => SetValue(PlayerSeekGestureKey, value);
    }

    public bool PlayerTapGesture
    {
        get => GetValue<bool>(PlayerTapGestureKey);
        set => SetValue(PlayerTapGestureKey, value);
    }

    public int PersistentVolume
    {
        get => GetValue<int>(PersistentVolumeKey);
        set => SetValue(PersistentVolumeKey, value);
    }

    public string PersistentSubtitleLanguage
    {
        get => GetValue<string>(PersistentSubtitleLanguageKey) ?? string.Empty;
        set => SetValue(PersistentSubtitleLanguageKey, value);
    }

    public int MaxVolume
    {
        get => GetValue<int>(MaxVolumeKey);
        set => SetValue(MaxVolumeKey, value);
    }

    public bool ShowRecent
    {
        get => GetValue<bool>(GeneralShowRecent);
        set => SetValue(GeneralShowRecent, value);
    }

    public int RecentLimit
    {
        get => GetValue<int>(GeneralRecentLimit);
        set => SetValue(GeneralRecentLimit, value);
    }

    public int NavigationPaneWidth
    {
        get => GetValue<int>(GeneralNavigationPaneWidth);
        set => SetValue(GeneralNavigationPaneWidth, value);
    }

    public int VideoFoldersSortIndex
    {
        get => GetValue<int>(GeneralVideoFoldersSortIndex);
        set => SetValue(GeneralVideoFoldersSortIndex, value);
    }

    public int AllVideosSortIndex
    {
        get => GetValue<int>(GeneralAllVideosSortIndex);
        set => SetValue(GeneralAllVideosSortIndex, value);
    }

    public int VideoFoldersThumbnailSizeIndex
    {
        get => GetValue<int>(GeneralVideoFoldersThumbnailSizeIndex);
        set => SetValue(GeneralVideoFoldersThumbnailSizeIndex, value);
    }

    public int AllVideosThumbnailSizeIndex
    {
        get => GetValue<int>(GeneralAllVideosThumbnailSizeIndex);
        set => SetValue(GeneralAllVideosThumbnailSizeIndex, value);
    }

    public bool EnqueueAllFilesInFolder
    {
        get => GetValue<bool>(GeneralEnqueueAllInFolder);
        set => SetValue(GeneralEnqueueAllInFolder, value);
    }

    public bool RestorePlaybackPosition
    {
        get => GetValue<bool>(GeneralRestorePlaybackPosition);
        set => SetValue(GeneralRestorePlaybackPosition, value);
    }

    public bool PlayerShowControls
    {
        get => GetValue<bool>(PlayerShowControlsKey);
        set => SetValue(PlayerShowControlsKey, value);
    }

    public int PlayerControlsHideDelay
    {
        get => GetValue<int>(PlayerControlsHideDelayKey);
        set => SetValue(PlayerControlsHideDelayKey, value);
    }

    public int ThumbnailCaptureTimeSeconds
    {
        get => GetValue<int>(PlayerThumbnailCaptureTimeKey);
        set => SetValue(PlayerThumbnailCaptureTimeKey, value);
    }

    public bool AutoLoadThumbnails
    {
        get => GetValue<bool>(PlayerAutoLoadThumbnailsKey);
        set => SetValue(PlayerAutoLoadThumbnailsKey, value);
    }

    public bool UseGeneratedVideoThumbnails
    {
        get => GetValue<bool>(PlayerUseGeneratedVideoThumbnailsKey);
        set => SetValue(PlayerUseGeneratedVideoThumbnailsKey, value);
    }

    public bool UseCustomVideoThumbnails
    {
        get => GetValue<bool>(PlayerUseCustomVideoThumbnailsKey);
        set => SetValue(PlayerUseCustomVideoThumbnailsKey, value);
    }

    public StartupDestinationOption StartupDestination
    {
        get => (StartupDestinationOption)GetValue<int>(GeneralStartupDestinationKey);
        set => SetValue(GeneralStartupDestinationKey, (int)value);
    }

    public string StartupTag
    {
        get => GetValue<string>(GeneralStartupTagKey) ?? string.Empty;
        set => SetValue(GeneralStartupTagKey, value);
    }

    public bool AppLockEnabled
    {
        get => GetValue<bool>(PrivacyAppLockEnabledKey);
        set => SetValue(PrivacyAppLockEnabledKey, value);
    }

    public string AppLockPinHash
    {
        get => GetValue<string>(PrivacyAppLockPinHashKey) ?? string.Empty;
        set => SetValue(PrivacyAppLockPinHashKey, value);
    }

    public string AppLockPinSalt
    {
        get => GetValue<string>(PrivacyAppLockPinSaltKey) ?? string.Empty;
        set => SetValue(PrivacyAppLockPinSaltKey, value);
    }

    public bool AutoCleanVideoTitles
    {
        get => GetValue<bool>(GeneralAutoCleanVideoTitlesKey);
        set => SetValue(GeneralAutoCleanVideoTitlesKey, value);
    }

    public StartupVolumeMode StartupVolumeMode
    {
        get => (StartupVolumeMode)GetValue<int>(PlayerStartupVolumeModeKey);
        set => SetValue(PlayerStartupVolumeModeKey, (int)value);
    }

    public int StartupVolumePercent
    {
        get => GetValue<int>(PlayerStartupVolumePercentKey);
        set => SetValue(PlayerStartupVolumePercentKey, Math.Clamp(value, 0, 100));
    }

    public bool SearchRemovableStorage
    {
        get => GetValue<bool>(LibrariesSearchRemovableStorageKey);
        set => SetValue(LibrariesSearchRemovableStorageKey, value);
    }

    public MediaPlaybackAutoRepeatMode PersistentRepeatMode
    {
        get => (MediaPlaybackAutoRepeatMode)GetValue<int>(PersistentRepeatModeKey);
        set => SetValue(PersistentRepeatModeKey, (int)value);
    }

    public string GlobalArguments
    {
        get => GetValue<string>(GlobalArgumentsKey) ?? string.Empty;
        set => SetValue(GlobalArgumentsKey, SanitizeArguments(value));
    }

    public bool AdvancedMode
    {
        get => GetValue<bool>(AdvancedModeKey);
        set => SetValue(AdvancedModeKey, value);
    }

    public VideoUpscaleOption VideoUpscale
    {
        get => (VideoUpscaleOption)GetValue<int>(AdvancedVideoUpscaleKey);
        set => SetValue(AdvancedVideoUpscaleKey, (int)value);
    }

    public bool UseMultipleInstances
    {
        get => GetValue<bool>(AdvancedMultipleInstancesKey);
        set => SetValue(AdvancedMultipleInstancesKey, value);
    }

    public string LivelyActivePath
    {
        get => GetValue<string>(PlayerLivelyPathKey) ?? string.Empty;
        set => SetValue(PlayerLivelyPathKey, value);
    }

    public bool PlayerShowChapters
    {
        get => GetValue<bool>(PlayerShowChaptersKey);
        set => SetValue(PlayerShowChaptersKey, value);
    }

    public bool PersistPlaybackPosition
    {
        get => GetValue<bool>(PrivacyPersistPlaybackPosition);
        set => SetValue(PrivacyPersistPlaybackPosition, value);
    }

    public SettingsService()
    {
        SetDefault(PlayerAutoResizeKey, (int)PlayerAutoResizeOption.Never);
        SetDefault(PlayerVolumeGestureKey, true);
        SetDefault(PlayerSeekGestureKey, true);
        SetDefault(PlayerTapGestureKey, true);
        SetDefault(PlayerShowControlsKey, true);
        SetDefault(PlayerControlsHideDelayKey, 3);
        SetDefault(PlayerThumbnailCaptureTimeKey, 60);
        SetDefault(PlayerAutoLoadThumbnailsKey, true);
        SetDefault(PlayerUseGeneratedVideoThumbnailsKey, true);
        SetDefault(PlayerUseCustomVideoThumbnailsKey, true);
        MigrateThumbnailCaptureTimeDefault();
        SetDefault(GeneralStartupDestinationKey, (int)StartupDestinationOption.Home);
        SetDefault(GeneralStartupTagKey, string.Empty);
        SetDefault(PrivacyAppLockEnabledKey, false);
        SetDefault(PrivacyAppLockPinHashKey, string.Empty);
        SetDefault(PrivacyAppLockPinSaltKey, string.Empty);
        SetDefault(GeneralAutoCleanVideoTitlesKey, true);
        SetDefault(PlayerStartupVolumeModeKey, (int)StartupVolumeMode.RememberLast);
        SetDefault(PlayerStartupVolumePercentKey, 100);
        SetDefault(PersistentVolumeKey, 100);
        SetDefault(MaxVolumeKey, 100);
        SetDefault(LibrariesUseIndexerKey, true);
        SetDefault(LibrariesSearchRemovableStorageKey, true);
        SetDefault(GeneralShowRecent, true);
        SetDefault(GeneralRecentLimit, 12);
        SetDefault(GeneralNavigationPaneWidth, 320);
        SetDefault(GeneralVideoFoldersSortIndex, 0);
        SetDefault(GeneralAllVideosSortIndex, 0);
        SetDefault(GeneralVideoFoldersThumbnailSizeIndex, 1);
        SetDefault(GeneralAllVideosThumbnailSizeIndex, 1);
        SetDefault(PersistentRepeatModeKey, (int)MediaPlaybackAutoRepeatMode.None);
        SetDefault(AdvancedModeKey, false);
        SetDefault(AdvancedVideoUpscaleKey, (int)VideoUpscaleOption.Linear);
        SetDefault(AdvancedMultipleInstancesKey, false);
        SetDefault(GlobalArgumentsKey, string.Empty);
        SetDefault(PlayerShowChaptersKey, true);
        SetDefault(PrivacyPersistPlaybackPosition, true);

        // Device family specific overrides
        if (SystemInformation.IsXbox)
        {
            SetValue(PlayerTapGestureKey, false);
            SetValue(PlayerSeekGestureKey, false);
            SetValue(PlayerVolumeGestureKey, false);
            SetValue(PlayerAutoResizeKey, (int)PlayerAutoResizeOption.Never);
            SetValue(PlayerShowControlsKey, true);
        }
    }

    private static T? GetValue<T>(string key)
    {
        if (SettingsStorage.TryGetValue(key, out object value))
        {
            return (T)value;
        }

        return default;
    }

    private static void SetValue<T>(string key, T value)
    {
        SettingsStorage[key] = value;
    }

    private static void SetDefault<T>(string key, T value)
    {
        if (SettingsStorage.ContainsKey(key) && SettingsStorage[key] is T) return;
        SettingsStorage[key] = value;
    }

    private static void MigrateThumbnailCaptureTimeDefault()
    {
        if (GetValue<bool>(PlayerThumbnailCaptureTimeDefaultMigratedKey)) return;

        if (GetValue<int>(PlayerThumbnailCaptureTimeKey) == 10)
        {
            SetValue(PlayerThumbnailCaptureTimeKey, 60);
        }

        SetValue(PlayerThumbnailCaptureTimeDefaultMigratedKey, true);
    }

    private static string SanitizeArguments(string raw)
    {
        string[] args = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(s => s.StartsWith('-') && s != "--").ToArray();
        return string.Join(' ', args);
    }
}
