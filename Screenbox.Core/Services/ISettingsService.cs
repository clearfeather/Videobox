using Screenbox.Core.Enums;
using Windows.Media;

namespace Screenbox.Core.Services;

public interface ISettingsService
{
    PlayerAutoResizeOption PlayerAutoResize { get; set; }
    bool UseIndexer { get; set; }
    bool PlayerVolumeGesture { get; set; }
    bool PlayerSeekGesture { get; set; }
    bool PlayerTapGesture { get; set; }
    bool PlayerShowControls { get; set; }
    bool PlayerShowChapters { get; set; }
    int PlayerControlsHideDelay { get; set; }
    int ThumbnailCaptureTimeSeconds { get; set; }
    bool AutoLoadThumbnails { get; set; }
    bool UseGeneratedVideoThumbnails { get; set; }
    bool UseCustomVideoThumbnails { get; set; }
    StartupDestinationOption StartupDestination { get; set; }
    string StartupTag { get; set; }
    bool AppLockEnabled { get; set; }
    string AppLockPinHash { get; set; }
    string AppLockPinSalt { get; set; }
    bool AutoCleanVideoTitles { get; set; }
    StartupVolumeMode StartupVolumeMode { get; set; }
    int StartupVolumePercent { get; set; }
    int PersistentVolume { get; set; }
    string PersistentSubtitleLanguage { get; set; }
    bool ShowRecent { get; set; }
    int RecentLimit { get; set; }
    int NavigationPaneWidth { get; set; }
    int VideoFoldersSortIndex { get; set; }
    int AllVideosSortIndex { get; set; }
    int VideoFoldersThumbnailSizeIndex { get; set; }
    int AllVideosThumbnailSizeIndex { get; set; }
    ThemeOption Theme { get; set; }
    bool EnqueueAllFilesInFolder { get; set; }
    bool RestorePlaybackPosition { get; set; }
    bool SearchRemovableStorage { get; set; }
    int MaxVolume { get; set; }
    string GlobalArguments { get; set; }
    bool AdvancedMode { get; set; }
    VideoUpscaleOption VideoUpscale { get; set; }
    bool UseMultipleInstances { get; set; }
    string LivelyActivePath { get; set; }
    MediaPlaybackAutoRepeatMode PersistentRepeatMode { get; set; }

    /// <summary>
    /// Gets or sets a value that indicates whether the playback position should be saved
    /// and restored between sessions.
    /// </summary>
    bool PersistPlaybackPosition { get; set; }
}
