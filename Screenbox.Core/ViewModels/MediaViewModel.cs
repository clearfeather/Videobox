#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using LibVLCSharp.Shared;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Playback;
using Screenbox.Core.Services;
using TagLib;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Screenbox.Core.ViewModels;

public partial class MediaViewModel : ObservableRecipient
{
    public string Location { get; }

    public object Source { get; private set; }

    public bool IsFromLibrary { get; set; }

    public bool DetailsLoaded { get; private set; }

    public ArtistViewModel? MainArtist => Artists.FirstOrDefault();

    public Lazy<PlaybackItem?> Item { get; internal set; }

    public IReadOnlyList<string> Options { get; }

    public DateTimeOffset DateAdded { get; set; }

    public MediaPlaybackType MediaType => MediaInfo.MediaType;

    public TimeSpan Duration => MediaInfo.MusicProperties.Duration > TimeSpan.Zero
        ? MediaInfo.MusicProperties.Duration
        : MediaInfo.VideoProperties.Duration;

    public string DurationText => Duration > TimeSpan.Zero ? Humanizer.ToDuration(Duration) : string.Empty;     // Helper for binding

    public string DisplayName => MediaTitleFormatter.GetDisplayName(this, _settingsService.AutoCleanVideoTitles);

    public string DisplayCaption => MediaTitleFormatter.GetDisplayCaption(this, _settingsService.AutoCleanVideoTitles);

    public string TrackNumberText =>
        MediaInfo.MusicProperties.TrackNumber > 0 ? MediaInfo.MusicProperties.TrackNumber.ToString() : string.Empty;    // Helper for binding

    public BitmapImage? Thumbnail
    {
        get
        {
            if (_thumbnailRef == null) return null;
            return _thumbnailRef.TryGetTarget(out BitmapImage image) ? image : null;
        }
        set
        {
            if (_thumbnailRef == null && value == null) return;
            if ((_thumbnailRef?.TryGetTarget(out BitmapImage image) ?? false) && image == value) return;
            SetProperty(ref _thumbnailRef, value == null ? null : new WeakReference<BitmapImage>(value));
        }
    }

    private IMediaPlayer? MediaPlayer => _playerContext.MediaPlayer;

    private readonly IPlayerService _playerService;
    private readonly PlayerContext _playerContext;
    private readonly ISettingsService _settingsService;
    private readonly IThumbnailService _thumbnailService;
    private readonly IThumbnailLoadingService _thumbnailLoadingService;
    private readonly List<string> _options;
    private const string GeneratedThumbnailCacheVersion = "v2-vlc-first";
    private static readonly SemaphoreSlim ThumbnailCaptureSemaphore = new(1, 1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name = string.Empty;
    [ObservableProperty] private bool _isMediaActive;
    [ObservableProperty] private bool _isAvailable = true;
    [ObservableProperty] private AlbumViewModel? _album;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayCaption))]
    private string _caption = string.Empty;  // For list item subtitle
    [ObservableProperty] private string _altCaption = string.Empty;   // For player page subtitle
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private string _allVideosCaption = string.Empty;
    [ObservableProperty] private bool _isThumbnailLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    [NotifyPropertyChangedFor(nameof(DisplayCaption))]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    [NotifyPropertyChangedFor(nameof(TrackNumberText))]
    private MediaInfo _mediaInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MainArtist))]
    private ArtistViewModel[] _artists;

    [ObservableProperty]
    private bool? _isPlaying;

    private WeakReference<BitmapImage>? _thumbnailRef;

    public MediaViewModel(MediaViewModel source)
    {
        _playerService = source._playerService;
        _playerContext = source._playerContext;
        _settingsService = source._settingsService;
        _thumbnailService = source._thumbnailService;
        _thumbnailLoadingService = source._thumbnailLoadingService;
        _name = source._name;
        _thumbnailRef = source._thumbnailRef;
        _mediaInfo = source._mediaInfo;
        _artists = source._artists;
        _album = source._album;
        _caption = source._caption;
        _altCaption = source._altCaption;
        _options = new List<string>(source.Options);
        Options = new ReadOnlyCollection<string>(_options);
        Location = source.Location;
        Source = source.Source;
        Item = new Lazy<PlaybackItem?>(CreatePlaybackItem);
        DateAdded = source.DateAdded;
        IsFromLibrary = source.IsFromLibrary;
        DetailsLoaded = source.DetailsLoaded;
        IsFavorite = source.IsFavorite;
        AllVideosCaption = source.AllVideosCaption;
        IsThumbnailLoading = source.IsThumbnailLoading;
    }

    private MediaViewModel(object source,
        MediaInfo mediaInfo,
        PlayerContext playerContext,
        IPlayerService playerService,
        ISettingsService settingsService,
        IThumbnailService thumbnailService,
        IThumbnailLoadingService thumbnailLoadingService)
    {
        _playerService = playerService;
        _playerContext = playerContext;
        _settingsService = settingsService;
        _thumbnailService = thumbnailService;
        _thumbnailLoadingService = thumbnailLoadingService;
        Source = source;
        Location = string.Empty;
        DateAdded = DateTimeOffset.Now;
        _name = string.Empty;
        _mediaInfo = mediaInfo;
        _artists = Array.Empty<ArtistViewModel>();
        _options = new List<string>();
        Options = new ReadOnlyCollection<string>(_options);
        Item = new Lazy<PlaybackItem?>(CreatePlaybackItem);
    }

    public MediaViewModel(PlayerContext playerContext,
        IPlayerService playerService,
        ISettingsService settingsService,
        IThumbnailService thumbnailService,
        IThumbnailLoadingService thumbnailLoadingService,
        StorageFile file)
        : this(file, new MediaInfo(FilesHelpers.GetMediaTypeForFile(file)), playerContext, playerService, settingsService, thumbnailService, thumbnailLoadingService)
    {
        Location = file.Path;
        _name = file.Name;
        _altCaption = file.Name;
    }

    public MediaViewModel(PlayerContext playerContext,
        IPlayerService playerService,
        ISettingsService settingsService,
        IThumbnailService thumbnailService,
        IThumbnailLoadingService thumbnailLoadingService,
        Uri uri)
        : this(uri, new MediaInfo(MediaPlaybackType.Unknown), playerContext, playerService, settingsService, thumbnailService, thumbnailLoadingService)
    {
        Guard.IsTrue(uri.IsAbsoluteUri);
        Location = uri.OriginalString;
        _name = uri.Segments.Length > 0 ? Uri.UnescapeDataString(uri.Segments.Last()) : string.Empty;
    }

    public MediaViewModel(PlayerContext playerContext,
        IPlayerService playerService,
        ISettingsService settingsService,
        IThumbnailService thumbnailService,
        IThumbnailLoadingService thumbnailLoadingService,
        Media media)
        : this(media, new MediaInfo(MediaPlaybackType.Unknown), playerContext, playerService, settingsService, thumbnailService, thumbnailLoadingService)
    {
        Location = media.Mrl;

        // Media is already loaded, create PlaybackItem
        Item = new Lazy<PlaybackItem?>(new PlaybackItem(media, media));
    }

    partial void OnMediaInfoChanged(MediaInfo value)
    {
        UpdateCaptions();
    }

    private PlaybackItem? CreatePlaybackItem()
    {
        if (MediaPlayer == null)
        {
            Messenger.Send(new MediaLoadFailedNotificationMessage("Media player is not initialized", Location));
            return null;
        }

        PlaybackItem? item = null;
        try
        {
            if (Source is Media mediaSource)
            {
                item = new PlaybackItem(mediaSource, mediaSource);
            }
            else
            {
                item = _playerService.CreatePlaybackItem(MediaPlayer, Source, _options.ToArray());
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // Coding error. Rethrow.
            throw;
        }
        catch (Exception e)
        {
            Messenger.Send(new MediaLoadFailedNotificationMessage(e.Message, Location));
        }

        return item;
    }

    public void SetOptions(string options)
    {
        string[] opts = options.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(o => o.StartsWith(":") && o.Length > 1).ToArray();

        // Check if new options and existing options are the same
        if (opts.Length == _options.Count)
        {
            bool same = !opts.Where((o, i) => o != _options[i]).Any();
            if (same) return;
        }

        _options.Clear();
        _options.AddRange(opts);

        if (!Item.IsValueCreated) return;
        Clean();
    }

    public void Clean()
    {
        // If source is Media then there is no way to recreate. Don't clean up.
        if (Source is Media || !Item.IsValueCreated) return;
        PlaybackItem? item = Item.Value;
        Item = new Lazy<PlaybackItem?>(CreatePlaybackItem);
        if (item == null) return;
        _playerService.DisposePlaybackItem(item);
    }

    public void UpdateSource(StorageFile file)
    {
        Source = file;
        AltCaption = file.Name;
    }

    public async Task LoadDetailsAsync(IFilesService filesService)
    {
        DetailsLoaded = true;
        switch (Source)
        {
            case StorageFile file:
                MediaInfo = await filesService.GetMediaInfoAsync(file);
                break;
            case Uri uri when await TryGetStorageFileFromUri(uri) is { } uriFile:
                UpdateSource(uriFile);
                MediaInfo = await filesService.GetMediaInfoAsync(uriFile);
                break;
        }

        switch (MediaType)
        {
            case MediaPlaybackType.Unknown when Item is { IsValueCreated: true, Value: { VideoTracks.Count: 0, Media.ParsedStatus: MediaParsedStatus.Done } }:
                // Update media type when it was previously set Unknown. Usually when source is a URI.
                // We don't want to init PlaybackItem just for this.
                MediaInfo.MediaType = MediaPlaybackType.Music;
                break;
            case MediaPlaybackType.Music when !string.IsNullOrEmpty(MediaInfo.MusicProperties.Title):
                Name = MediaInfo.MusicProperties.Title;
                break;
            case MediaPlaybackType.Video when !string.IsNullOrEmpty(MediaInfo.VideoProperties.Title):
                Name = MediaInfo.VideoProperties.Title;
                break;
        }

        if (Item is { IsValueCreated: true, Value.Media: { IsParsed: true } media })
        {
            if (Source is not IStorageItem &&
                media.Meta(MetadataType.Title) is { } title &&
                !string.IsNullOrEmpty(title) &&
                !Guid.TryParse(title, out Guid _))
            {
                Name = title;
            }

            VideoInfo videoProperties = MediaInfo.VideoProperties;
            videoProperties.ShowName = media.Meta(MetadataType.ShowName) ?? videoProperties.ShowName;
            videoProperties.Season = media.Meta(MetadataType.Season) ?? videoProperties.Season;
            videoProperties.Episode = media.Meta(MetadataType.Episode) ?? videoProperties.Episode;
        }

        if (Name == AltCaption)
            AltCaption = string.Empty;
    }

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail != null || IsThumbnailLoading) return;

        IsThumbnailLoading = true;
        try
        {
            if (Source is Uri uri && await TryGetStorageFileFromUri(uri) is { } storageFile)
            {
                UpdateSource(storageFile);
            }

            if (Source is StorageFile file)
            {
                using var source = await GetCachedCustomThumbnailSourceAsync()
                                   ?? await GetThumbnailSourceForCurrentSettingsAsync(file);
                if (source == null) return;
                BitmapImage image = new()
                {
                    DecodePixelType = DecodePixelType.Logical,
                    DecodePixelHeight = 300
                };

                try
                {
                    await image.SetSourceAsync(source);
                }
                catch (Exception)
                {
                    // WinRT component not found exception???
                    return;
                }

                Thumbnail = image;
            }
            else if (Item is { IsValueCreated: true, Value.Media: { } media } &&
                     media.Meta(MetadataType.ArtworkURL) is { } artworkUrl &&
                     Uri.TryCreate(artworkUrl, UriKind.Absolute, out Uri artworkUri))
            {
                Thumbnail = new BitmapImage(artworkUri)
                {
                    DecodePixelType = DecodePixelType.Logical,
                    DecodePixelHeight = 300
                };
            }
        }
        finally
        {
            IsThumbnailLoading = false;
        }
    }

    public void InvalidateThumbnail()
    {
        Thumbnail = null;
    }

    public async Task SetCustomThumbnailAsync(byte[] imageBytes)
    {
        if (!_settingsService.UseCustomVideoThumbnails || string.IsNullOrWhiteSpace(Location)) return;

        await _thumbnailService.SaveThumbnailAsync(Location, imageBytes);
        InvalidateThumbnail();
        await LoadThumbnailAsync();
    }

    public void InvalidateDisplayText()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(DisplayCaption));
    }

    public Task<IRandomAccessStream?> GetThumbnailSourceAsync()
    {
        return Source is not StorageFile file
            ? Task.FromResult<IRandomAccessStream?>(null)
            : GetThumbnailSourceForCurrentSettingsAsync(file);
    }

    private async Task<IRandomAccessStream?> GetCachedCustomThumbnailSourceAsync()
    {
        if (!_settingsService.UseCustomVideoThumbnails) return null;
        if (string.IsNullOrWhiteSpace(Location)) return null;
        StorageFile? thumbnailFile = await _thumbnailService.GetThumbnailFileAsync(Location);
        return thumbnailFile == null ? null : await thumbnailFile.OpenReadAsync();
    }

    private async Task<IRandomAccessStream?> GetCachedGeneratedThumbnailSourceAsync(StorageFile file)
    {
        if (MediaType == MediaPlaybackType.Video && !_settingsService.UseGeneratedVideoThumbnails) return null;
        if (string.IsNullOrWhiteSpace(Location)) return null;
        string cacheStamp = await GetGeneratedThumbnailCacheStampAsync(file);
        StorageFile? thumbnailFile = await _thumbnailService.GetGeneratedThumbnailFileAsync(Location, cacheStamp);
        return thumbnailFile == null ? null : await thumbnailFile.OpenReadAsync();
    }

    private async Task<IRandomAccessStream?> GetThumbnailSourceForCurrentSettingsAsync(StorageFile file)
    {
        if (MediaType == MediaPlaybackType.Video &&
            (!_settingsService.UseGeneratedVideoThumbnails || !_settingsService.AutoLoadThumbnails))
        {
            return await GetDefaultThumbnailSourceAsync(file);
        }

        return await GetCachedGeneratedThumbnailSourceAsync(file)
               ?? await GenerateAndCacheThumbnailSourceAsync(file);
    }

    private async Task<IRandomAccessStream?> GenerateAndCacheThumbnailSourceAsync(StorageFile file)
    {
        using IDisposable thumbnailOperation = _thumbnailLoadingService.BeginOperation();
        IRandomAccessStream? source = await GetGeneratedOrEmbeddedThumbnailSourceAsync(file);
        if (source == null || string.IsNullOrWhiteSpace(Location)) return source;

        try
        {
            string cacheStamp = await GetGeneratedThumbnailCacheStampAsync(file);
            await _thumbnailService.SaveGeneratedThumbnailAsync(Location, cacheStamp, source);
        }
        catch (Exception e)
        {
            e.Data[nameof(file)] = file.Path;
            LogService.Log(e);
        }

        try
        {
            source.Seek(0);
        }
        catch
        {
            // Let BitmapImage try to consume streams that do not support seeking.
        }

        return source;
    }

    private async Task<string> GetGeneratedThumbnailCacheStampAsync(StorageFile file)
    {
        BasicProperties properties = await StorageFilePropertiesGate.GetBasicPropertiesAsync(file);
        int captureTimeSeconds = MediaType == MediaPlaybackType.Video
            ? _settingsService.ThumbnailCaptureTimeSeconds
            : 0;
        return $"{GeneratedThumbnailCacheVersion}:{properties.DateModified.UtcTicks}:{properties.Size}:{MediaType}:{captureTimeSeconds}";
    }

    private async Task<IRandomAccessStream?> GetGeneratedOrEmbeddedThumbnailSourceAsync(StorageFile file)
    {
        if (MediaType == MediaPlaybackType.Video)
        {
            TimeSpan captureTime = GetClampedThumbnailCaptureTime();
            return await GetVlcFrameThumbnailAsync(file, captureTime)
                   ?? await GetVideoFrameThumbnailAsync(file, captureTime)
                   ?? await GetCoverFromTagAsync(file)
                   ?? await GetStorageFileThumbnailAsync(file);
        }

        return await GetCoverFromTagAsync(file) ?? await GetStorageFileThumbnailAsync(file);
    }

    private async Task<IRandomAccessStream?> GetDefaultThumbnailSourceAsync(StorageFile file)
    {
        if (MediaType == MediaPlaybackType.Video)
        {
            return await GetStorageFileThumbnailAsync(file) ?? await GetCoverFromTagAsync(file);
        }

        return await GetCoverFromTagAsync(file) ?? await GetStorageFileThumbnailAsync(file);
    }

    private TimeSpan GetClampedThumbnailCaptureTime()
    {
        TimeSpan captureTime = TimeSpan.FromSeconds(_settingsService.ThumbnailCaptureTimeSeconds);
        if (captureTime < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        TimeSpan duration = Duration;
        if (duration > TimeSpan.Zero && captureTime >= duration)
        {
            return duration > TimeSpan.FromSeconds(1)
                ? duration - TimeSpan.FromSeconds(1)
                : TimeSpan.Zero;
        }

        return captureTime;
    }

    private static async Task<IRandomAccessStream?> GetVideoFrameThumbnailAsync(StorageFile file, TimeSpan captureTime)
    {
        if (!file.IsAvailable) return null;
        try
        {
            MediaClip clip = await MediaClip.CreateFromFileAsync(file);
            MediaComposition composition = new();
            composition.Clips.Add(clip);

            TimeSpan duration = composition.Duration;
            if (captureTime < TimeSpan.Zero)
            {
                captureTime = TimeSpan.Zero;
            }
            else if (duration > TimeSpan.Zero && captureTime >= duration)
            {
                captureTime = duration > TimeSpan.FromSeconds(1)
                    ? duration - TimeSpan.FromSeconds(1)
                    : TimeSpan.Zero;
            }

            const int thumbnailWidth = 533;
            const int thumbnailHeight = 300;
            return await composition.GetThumbnailAsync(
                captureTime,
                thumbnailWidth,
                thumbnailHeight,
                VideoFramePrecision.NearestFrame);
        }
        catch (Exception e)
        {
            e.Data[nameof(file)] = file.Path;
            e.Data[nameof(captureTime)] = captureTime.ToString();
            LogService.Log(e);
            return null;
        }
    }

    private async Task<IRandomAccessStream?> GetVlcFrameThumbnailAsync(StorageFile file, TimeSpan captureTime)
    {
        if (!file.IsAvailable) return null;

        await ThumbnailCaptureSemaphore.WaitAsync();
        try
        {
            StorageFolder tempFolder = await ApplicationData.Current.TemporaryFolder.CreateFolderAsync(
                $"thumbnail_{DateTimeOffset.Now.Ticks}",
                CreationCollisionOption.GenerateUniqueName);

            try
            {
                string prefix = "screenbox_thumbnail";
                double startSeconds = Math.Max(0, captureTime.TotalSeconds);
                double stopSeconds = startSeconds + 1;
                string[] options =
                {
                    ":no-audio",
                    $":start-time={startSeconds:0.###}",
                    $":stop-time={stopSeconds:0.###}",
                    ":video-filter=scene",
                    ":scene-format=png",
                    ":scene-ratio=1",
                    ":scene-replace",
                    $":scene-prefix={prefix}",
                    $":scene-path={tempFolder.Path}"
                };

                IMediaPlayer? player = null;
                PlaybackItem? item = null;
                try
                {
                    player = _playerService.Initialize(Array.Empty<string>());
                    item = _playerService.CreatePlaybackItem(player, file, options);
                    player.PlaybackItem = item;
                    player.Play();

                    StorageFile? thumbnailFile = await WaitForVlcThumbnailAsync(tempFolder, TimeSpan.FromSeconds(8));
                    return thumbnailFile == null ? null : await CopyToMemoryStreamAsync(thumbnailFile);
                }
                finally
                {
                    if (player != null)
                    {
                        player.PlaybackItem = null;
                    }

                    if (item != null)
                    {
                        _playerService.DisposePlaybackItem(item);
                    }

                    if (player != null)
                    {
                        _playerService.DisposePlayer(player);
                    }
                }
            }
            finally
            {
                await tempFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);
            }
        }
        catch (Exception e)
        {
            e.Data[nameof(file)] = file.Path;
            e.Data[nameof(captureTime)] = captureTime.ToString();
            LogService.Log(e);
            return null;
        }
        finally
        {
            ThumbnailCaptureSemaphore.Release();
        }
    }

    private static async Task<StorageFile?> WaitForVlcThumbnailAsync(StorageFolder folder, TimeSpan timeout)
    {
        DateTimeOffset deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
            StorageFile? thumbnail = files.FirstOrDefault(file =>
                file.FileType.Equals(".png", StringComparison.OrdinalIgnoreCase));
            if (thumbnail != null)
            {
                BasicProperties properties = await StorageFilePropertiesGate.GetBasicPropertiesAsync(thumbnail);
                if (properties.Size > 0)
                {
                    return thumbnail;
                }
            }

            await Task.Delay(250);
        }

        return null;
    }

    private static async Task<IRandomAccessStream> CopyToMemoryStreamAsync(StorageFile file)
    {
        InMemoryRandomAccessStream memoryStream = new();
        using IRandomAccessStreamWithContentType source = await file.OpenReadAsync();
        await RandomAccessStream.CopyAsync(source, memoryStream);
        memoryStream.Seek(0);
        return memoryStream;
    }

    private static async Task<IRandomAccessStream?> GetCoverFromTagAsync(StorageFile file)
    {
        if (!file.IsAvailable) return null;
        try
        {
            using var stream = await file.OpenStreamForReadAsync(); // Throwable: FileNotFoundException
            var name = string.IsNullOrEmpty(file.Path) ? file.Name : file.Path;
            var fileAbstract = new StreamAbstraction(name, stream);
            using var tagFile = TagLib.File.Create(fileAbstract, ReadStyle.PictureLazy);
            if (tagFile.Tag.Pictures.Length == 0) return null;
            var cover =
                tagFile.Tag.Pictures.FirstOrDefault(p => p.Type is PictureType.FrontCover or PictureType.Media) ??
                tagFile.Tag.Pictures.FirstOrDefault(p => p.Type != PictureType.NotAPicture);
            if (cover == null) return null;
            if (cover.Data.IsEmpty)
            {
                if (cover is not ILazy or ILazy { IsLoaded: true }) return null;
                ((ILazy)cover).Load();
            }

            var inMemoryStream = new InMemoryRandomAccessStream();
            await inMemoryStream.WriteAsync(cover.Data.Data.AsBuffer());
            inMemoryStream.Seek(0);
            return inMemoryStream;
        }
        catch (Exception)
        {
            // FileNotFoundException
            // UnsupportedFormatException
            // CorruptFileException
            // pass
        }

        return null;
    }

    private static async Task<IRandomAccessStream?> GetStorageFileThumbnailAsync(StorageFile file)
    {
        if (!file.IsAvailable) return null;
        try
        {
            StorageItemThumbnail? source = await file.GetThumbnailAsync(ThumbnailMode.SingleItem);
            if (source is { Type: ThumbnailType.Image })
            {
                return source;
            }
        }
        catch (Exception)
        {
            //// System.Exception: The data necessary to complete this operation is not yet available.
            //if (e.HResult != unchecked((int)0x8000000A) &&
            //    // System.Exception: The RPC server is unavailable.
            //    e.HResult != unchecked((int)0x800706BA))
            //    LogService.Log(e);
        }

        return null;
    }

    private void UpdateCaptions()
    {
        if (Duration > TimeSpan.Zero)
        {
            Caption = Humanizer.ToDuration(Duration);
        }

        MusicInfo musicProperties = MediaInfo.MusicProperties;
        if (!string.IsNullOrEmpty(musicProperties.Artist))
        {
            Caption = musicProperties.Artist;
            AltCaption = string.IsNullOrEmpty(musicProperties.Album)
                ? musicProperties.Artist
                : $"{musicProperties.Artist} – {musicProperties.Album}";
        }
        else if (!string.IsNullOrEmpty(musicProperties.Album))
        {
            AltCaption = musicProperties.Album;
        }

        if (Item is { IsValueCreated: true, Value.Media: { IsParsed: true } media })
        {
            string artist = media.Meta(MetadataType.Artist) ?? string.Empty;
            if (!string.IsNullOrEmpty(artist))
            {
                Caption = artist;
            }

            if (media.Meta(MetadataType.Album) is { } album && !string.IsNullOrEmpty(album))
            {
                AltCaption = string.IsNullOrEmpty(artist) ? album : $"{artist} – {album}";
            }
        }
    }

    private static async Task<StorageFile?> TryGetStorageFileFromUri(Uri uri)
    {
        if (uri is { IsFile: true, IsLoopback: true, IsAbsoluteUri: true })
        {
            try
            {
                return await StorageFile.GetFileFromPathAsync(uri.LocalPath);
            }
            catch (Exception)
            {
                return null;
            }
        }

        return null;
    }
}
