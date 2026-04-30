#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using LibVLCSharp.Shared;
using Screenbox.Core.Enums;
using Screenbox.Core.Playback;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Screenbox.Core.Services;

public sealed class PlayerService : IPlayerService
{
    private readonly IVlcDialogService _vlcDialogService;
    private readonly ISettingsService _settingsService;
    private readonly bool _useFal;

    public PlayerService(IVlcDialogService vlcDialogService, ISettingsService settingsService)
    {
        _vlcDialogService = vlcDialogService;
        _settingsService = settingsService;

        // FutureAccessList lets the player resolve local StorageFiles without broad filesystem access.
        // If FutureAccessList is somehow unavailable, SharedStorageAccessManager will be the fallback.
        _useFal = true;

        try
        {
            // Clear FA periodically because of 1000 items limit 
            // Delete any entries with "media" metadata to avoid hitting the limit with stale entries
            var tokensToRemove = StorageApplicationPermissions.FutureAccessList.Entries
                .Where(entry => entry.Metadata == "media")
                .Select(entry => entry.Token)
                .ToList();
            foreach (var token in tokensToRemove)
            {
                StorageApplicationPermissions.FutureAccessList.Remove(token);
            }
        }
        catch (Exception)   // FileNotFoundException
        {
            // FutureAccessList is not available
            _useFal = false;
        }
    }

    public IMediaPlayer Initialize(string[] swapChainOptions)
    {
        LibVLC lib = InitializeLibVlc(swapChainOptions);
        VlcMediaPlayer mediaPlayer = new(lib);
        ApplyStartupVolume(mediaPlayer);
        return mediaPlayer;
    }

    private void ApplyStartupVolume(VlcMediaPlayer mediaPlayer)
    {
        int volume = _settingsService.StartupVolumeMode switch
        {
            StartupVolumeMode.Muted => 0,
            StartupVolumeMode.Fixed => _settingsService.StartupVolumePercent,
            _ => _settingsService.PersistentVolume
        };

        volume = Math.Clamp(volume, 0, _settingsService.MaxVolume);
        mediaPlayer.Volume = volume / 100d;
        mediaPlayer.IsMuted = _settingsService.StartupVolumeMode == StartupVolumeMode.Muted || volume == 0;
    }

    public PlaybackItem CreatePlaybackItem(IMediaPlayer player, object source, params string[] options)
    {
        if (player is not VlcMediaPlayer vlcMediaPlayer)
            throw new NotSupportedException("Only VlcMediaPlayer is supported");
        Media media = CreateMedia(vlcMediaPlayer, source, options);
        return new PlaybackItem(source, media);
    }

    public void DisposePlaybackItem(PlaybackItem item)
    {
        DisposeMedia(item.Media);
    }

    public void DisposePlayer(IMediaPlayer player)
    {
        if (player is VlcMediaPlayer vlcMediaPlayer)
        {
            vlcMediaPlayer.VlcPlayer.Dispose();
            vlcMediaPlayer.LibVlc.Dispose();
        }
    }

    private Media CreateMedia(VlcMediaPlayer player, object source, params string[] options)
    {
        return source switch
        {
            IStorageFile file => CreateMedia(player, file, options),
            string str => CreateMedia(player, str, options),
            Uri uri => CreateMedia(player, uri, options),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
    }

    private Media CreateMedia(VlcMediaPlayer player, string str, params string[] options)
    {
        if (Uri.TryCreate(str, UriKind.Absolute, out Uri uri))
        {
            return CreateMedia(player, uri, options);
        }

        return new Media(player.LibVlc, str, FromType.FromPath, options);
    }

    private Media CreateMedia(VlcMediaPlayer player, IStorageFile file, params string[] options)
    {
        if (file is StorageFile storageFile &&
            storageFile.Provider.Id.Equals("network", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("VideoBox only supports local media files.");
        }

        string token = _useFal
            ? StorageApplicationPermissions.FutureAccessList.Add(file, "media")
            : SharedStorageAccessManager.AddFile(file);
        string mrl = "winrt://" + token;
        return new Media(player.LibVlc, mrl, FromType.FromLocation, options);
    }

    private Media CreateMedia(VlcMediaPlayer player, Uri uri, params string[] options)
    {
        if (!IsLocalFileUri(uri))
        {
            throw new NotSupportedException("VideoBox only supports local media files.");
        }

        return new Media(player.LibVlc, uri, options);
    }

    private static bool IsLocalFileUri(Uri uri)
    {
        return uri is { IsAbsoluteUri: true, IsFile: true, IsLoopback: true };
    }

    private void DisposeMedia(Media media)
    {
        string mrl = media.Mrl;
        if (mrl.StartsWith("winrt://"))
        {
            string token = mrl.Substring(8);
            try
            {
                if (_useFal)
                {
                    StorageApplicationPermissions.FutureAccessList.Remove(token);
                }
                else
                {
                    SharedStorageAccessManager.RemoveFile(token);
                }
            }
            catch (Exception)
            {
                LogService.Log($"Failed to remove access token {token}");
            }
        }

        media.Dispose();
    }

    private LibVLC InitializeLibVlc(string[] swapChainOptions)
    {
        List<string> options = new(swapChainOptions.Length + 4)
        {
#if DEBUG
            "--verbose=3",
#else
            "--verbose=0",
#endif
            // "--aout=winstore",
            //"--sout-chromecast-conversion-quality=0",
            "--no-osd"
        };
        options.AddRange(swapChainOptions);
#if DEBUG
        LibVLC libVlc = new(true, options.ToArray());
#else
        LibVLC libVlc = new(false, options.ToArray());
#endif
        LogService.RegisterLibVlcLogging(libVlc);
        _vlcDialogService.SetVlcDialogHandlers(libVlc);
        return libVlc;
    }
}
