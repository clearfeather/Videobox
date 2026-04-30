#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Screenbox.Core.Factories;
using Screenbox.Core.Models;
using Screenbox.Core.ViewModels;
using Windows.Storage;

namespace Screenbox.Core.Services;

public sealed class FavoritesService : IFavoritesService
{
    private const string FavoritesFileName = "Favorites.json";

    private readonly IFilesService _filesService;
    private readonly MediaViewModelFactory _mediaFactory;

    public FavoritesService(IFilesService filesService, MediaViewModelFactory mediaFactory)
    {
        _filesService = filesService;
        _mediaFactory = mediaFactory;
    }

    public async Task<IReadOnlyList<MediaViewModel>> LoadFavoritesAsync()
    {
        try
        {
            PersistentFavorites favorites = await _filesService.LoadFromDiskAsync<PersistentFavorites>(
                ApplicationData.Current.LocalFolder, FavoritesFileName);
            return favorites.Items.Select(ToMediaViewModel).ToList();
        }
        catch
        {
            return Array.Empty<MediaViewModel>();
        }
    }

    public async Task SaveFavoritesAsync(IReadOnlyList<MediaViewModel> favorites)
    {
        PersistentFavorites persistentFavorites = new()
        {
            Items = favorites.Select(m => new PersistentMediaRecord(
                m.Name,
                m.Location,
                m.MediaType == MediaPlaybackType.Music ? m.MediaInfo.MusicProperties : m.MediaInfo.VideoProperties,
                m.DateAdded)).ToList()
        };

        await _filesService.SaveToDiskAsync(ApplicationData.Current.LocalFolder, FavoritesFileName, persistentFavorites);
    }

    private MediaViewModel ToMediaViewModel(PersistentMediaRecord record)
    {
        MediaViewModel media;
        bool existing = false;
        if (Uri.TryCreate(record.Path, UriKind.Absolute, out Uri uri))
        {
            if (_mediaFactory.TryGetSingleton(uri, out MediaViewModel? existingMedia))
            {
                media = existingMedia!;
                existing = true;
            }
            else
            {
                media = _mediaFactory.GetSingleton(uri);
            }
        }
        else
        {
            media = _mediaFactory.GetTransient(new Uri("about:blank"));
            media.IsAvailable = false;
        }

        if (!existing)
        {
            if (!string.IsNullOrEmpty(record.Title))
                media.Name = record.Title;

            media.MediaInfo = record.Properties != null
                ? new MediaInfo(record.Properties)
                : new MediaInfo(record.MediaType, record.Title, record.Year, record.Duration);
        }

        if (record.DateAdded != default)
        {
            DateTimeOffset utcTime = DateTime.SpecifyKind(record.DateAdded, DateTimeKind.Utc);
            media.DateAdded = utcTime.ToLocalTime();
        }

        media.IsFavorite = true;
        return media;
    }
}
