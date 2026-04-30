#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Screenbox.Core.Contexts;
using Windows.Storage;
using Windows.Storage.Search;
using MediaViewModel = Screenbox.Core.ViewModels.MediaViewModel;

namespace Screenbox.Core.Services;

/// <summary>
/// Stateless service for library management operations
/// </summary>
public interface ILibraryService
{
    /// <summary>
    /// Initialize the music library
    /// </summary>
    Task<StorageLibrary> InitializeMusicLibraryAsync();

    /// <summary>
    /// Initialize the videos library
    /// </summary>
    Task<StorageLibrary> InitializeVideosLibraryAsync();

    /// <summary>
    /// Fetch music from the library
    /// </summary>
    Task FetchMusicAsync(LibraryContext context, bool useCache = true);

    /// <summary>
    /// Fetch videos from the library
    /// </summary>
    Task FetchVideosAsync(LibraryContext context, bool useCache = true);

    /// <summary>
    /// Gets VideoBox's app-local video folders.
    /// </summary>
    Task<IReadOnlyList<StorageFolder>> GetVideoLibraryFoldersAsync();

    /// <summary>
    /// Adds a folder to VideoBox's app-local video folders.
    /// </summary>
    Task<StorageFolder?> AddVideoLibraryFolderAsync();

    /// <summary>
    /// Removes a folder from VideoBox's app-local video folders.
    /// </summary>
    Task RemoveVideoLibraryFolderAsync(StorageFolder folder);

    /// <summary>
    /// Remove media from the library
    /// </summary>
    void RemoveMedia(LibraryContext context, MediaViewModel media);

    /// <summary>
    /// Creates a query for the user's music library.
    /// </summary>
    StorageFileQueryResult CreateMusicLibraryQuery(bool useIndexer);

    /// <summary>
    /// Creates a query for the user's videos library.
    /// </summary>
    StorageFileQueryResult CreateVideosLibraryQuery(bool useIndexer);
}
