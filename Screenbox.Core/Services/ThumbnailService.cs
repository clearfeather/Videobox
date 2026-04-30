#nullable enable

using System;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Screenbox.Core.Services;

public sealed class ThumbnailService : IThumbnailService
{
    private const string ThumbnailsFolderName = "Thumbnails";
    private const string GeneratedThumbnailsFolderName = "GeneratedThumbnails";

    public async Task SaveThumbnailAsync(string mediaLocation, byte[] imageBytes)
    {
        StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
            ThumbnailsFolderName,
            CreationCollisionOption.OpenIfExists);
        string hash = GetHash(mediaLocation);
        StorageFile file = await thumbnailsFolder.CreateFileAsync(hash + ".png", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteBytesAsync(file, imageBytes);
    }

    public async Task<StorageFile?> GetThumbnailFileAsync(string mediaLocation)
    {
        StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
            ThumbnailsFolderName,
            CreationCollisionOption.OpenIfExists);
        string hash = GetHash(mediaLocation);
        try
        {
            return await thumbnailsFolder.GetFileAsync(hash + ".png");
        }
        catch
        {
            return null;
        }
    }

    public async Task DeleteThumbnailAsync(string mediaLocation)
    {
        try
        {
            StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync(
                ThumbnailsFolderName);
            string hash = GetHash(mediaLocation);
            StorageFile file = await thumbnailsFolder.GetFileAsync(hash + ".png");
            await file.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch
        {
            // No custom thumbnail to delete.
        }
    }

    public async Task SaveGeneratedThumbnailAsync(string mediaLocation, string cacheStamp, IRandomAccessStream imageStream)
    {
        StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
            GeneratedThumbnailsFolderName,
            CreationCollisionOption.OpenIfExists);
        string hash = GetGeneratedHash(mediaLocation, cacheStamp);
        StorageFile file = await thumbnailsFolder.CreateFileAsync(hash + ".png", CreationCollisionOption.ReplaceExisting);

        try
        {
            imageStream.Seek(0);
        }
        catch
        {
            // Some WinRT streams may already be at the start but reject Seek.
        }

        using IRandomAccessStream output = await file.OpenAsync(FileAccessMode.ReadWrite);
        await RandomAccessStream.CopyAsync(imageStream, output);

        try
        {
            imageStream.Seek(0);
        }
        catch
        {
            // The caller can still use streams that were not seekable.
        }
    }

    public async Task<StorageFile?> GetGeneratedThumbnailFileAsync(string mediaLocation, string cacheStamp)
    {
        StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.CreateFolderAsync(
            GeneratedThumbnailsFolderName,
            CreationCollisionOption.OpenIfExists);
        string hash = GetGeneratedHash(mediaLocation, cacheStamp);
        try
        {
            return await thumbnailsFolder.GetFileAsync(hash + ".png");
        }
        catch
        {
            return null;
        }
    }

    public async Task ClearGeneratedThumbnailsAsync()
    {
        try
        {
            StorageFolder thumbnailsFolder = await ApplicationData.Current.LocalCacheFolder.GetFolderAsync(
                GeneratedThumbnailsFolderName);
            await thumbnailsFolder.DeleteAsync(StorageDeleteOption.PermanentDelete);
        }
        catch
        {
            // No generated cache to clear.
        }
    }

    private static string GetHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(input.ToLowerInvariant());
        byte[] hashBytes = sha256.ComputeHash(bytes);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private static string GetGeneratedHash(string mediaLocation, string cacheStamp)
    {
        return GetHash(mediaLocation + "|" + cacheStamp);
    }
}
