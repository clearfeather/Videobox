#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;

namespace Screenbox.Core.Services;

public interface ITagsService
{
    Task<IReadOnlyList<string>> LoadTagNamesAsync();

    Task<IReadOnlyList<IStorageItem>> LoadTaggedItemsAsync(string tagName);

    Task<IReadOnlyList<string>> LoadTagsForItemAsync(IStorageItem item);

    Task<IReadOnlyDictionary<string, string>> LoadItemTagMapAsync();

    Task<IReadOnlyList<string>> AddTagAsync(string tagName, IStorageItem item);

    Task<IReadOnlyList<string>> AddTagsAsync(IEnumerable<string> tagNames, IEnumerable<IStorageItem> items);

    Task<IReadOnlyList<string>> RemoveTagAsync(string tagName, IStorageItem item);

    Task<IReadOnlyList<string>> SetTagsAsync(IStorageItem item, IEnumerable<string> tagNames);

    Task<IReadOnlyList<string>> ClearAllTagsAsync();
}
