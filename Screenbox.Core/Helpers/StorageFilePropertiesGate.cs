#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Screenbox.Core.Helpers;

internal static class StorageFilePropertiesGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task<BasicProperties> GetBasicPropertiesAsync(StorageFile file)
    {
        string key = string.IsNullOrWhiteSpace(file.Path)
            ? file.Name
            : file.Path;
        SemaphoreSlim gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync();
        try
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    return await file.GetBasicPropertiesAsync();
                }
                catch (InvalidOperationException) when (attempt < 7)
                {
                    // WinRT throws if another metadata request for this item is still completing.
                    await Task.Delay(75 * (attempt + 1));
                }
            }

            return await file.GetBasicPropertiesAsync();
        }
        finally
        {
            gate.Release();
        }
    }
}
