#nullable enable

using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Services;

public interface IThumbnailService
{
    Task SaveThumbnailAsync(string mediaLocation, byte[] imageBytes);

    Task<StorageFile?> GetThumbnailFileAsync(string mediaLocation);

    Task DeleteThumbnailAsync(string mediaLocation);

    Task SaveGeneratedThumbnailAsync(string mediaLocation, string cacheStamp, IRandomAccessStream imageStream);

    Task<StorageFile?> GetGeneratedThumbnailFileAsync(string mediaLocation, string cacheStamp);

    Task ClearGeneratedThumbnailsAsync();
}
