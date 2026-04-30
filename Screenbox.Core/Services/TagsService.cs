#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Core.Models;
using Windows.Storage;
using Windows.Storage.Search;

namespace Screenbox.Core.Services;

public sealed class TagsService : ITagsService
{
    private const string TagsFileName = "Tags.json";

    private readonly IFilesService _filesService;

    public TagsService(IFilesService filesService)
    {
        _filesService = filesService;
    }

    public async Task<IReadOnlyList<string>> LoadTagNamesAsync()
    {
        PersistentTags tags = await LoadTagsAsync();
        return tags.Tags
            .Select(tag => tag.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<IStorageItem>> LoadTaggedItemsAsync(string tagName)
    {
        PersistentTags tags = await LoadTagsAsync();
        PersistentTag? tag = FindTag(tags, tagName);
        if (tag == null) return Array.Empty<IStorageItem>();

        List<IStorageItem> items = new();
        foreach (PersistentTaggedItem item in tag.Items)
        {
            try
            {
                IStorageItem storageItem = item.IsFolder
                    ? await StorageFolder.GetFolderFromPathAsync(item.Path)
                    : await StorageFile.GetFileFromPathAsync(item.Path);
                items.Add(storageItem);
            }
            catch
            {
                // Skip moved or inaccessible items. The persisted tag stays intact
                // so it can reappear if the drive or library path comes back.
            }
        }

        return items;
    }

    public async Task<IReadOnlyList<string>> LoadTagsForItemAsync(IStorageItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return Array.Empty<string>();
        }

        PersistentTags tags = await LoadTagsAsync();
        if (item is StorageFolder folder)
        {
            return tags.Tags
                .Where(tag => tag.Items.Any(existing => IsPathInFolder(existing.Path, folder.Path)))
                .Select(tag => tag.Name)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        return tags.Tags
            .Where(tag => tag.Items.Any(existing => SamePath(existing.Path, item.Path)))
            .Select(tag => tag.Name)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadItemTagMapAsync()
    {
        PersistentTags tags = await LoadTagsAsync();
        Dictionary<string, string> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (PersistentTag tag in tags.Tags
                     .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
                     .OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            foreach (PersistentTaggedItem item in tag.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.Path) && !map.ContainsKey(item.Path))
                {
                    map[item.Path] = tag.Name;
                }
            }
        }

        return map;
    }

    public async Task<IReadOnlyList<string>> AddTagAsync(string tagName, IStorageItem item)
    {
        string normalizedTagName = NormalizeTagName(tagName);
        if (string.IsNullOrWhiteSpace(normalizedTagName) || string.IsNullOrWhiteSpace(item.Path))
        {
            return await LoadTagNamesAsync();
        }

        PersistentTags tags = await LoadTagsAsync();
        IReadOnlyList<PersistentTaggedItem> taggedItems = await GetTaggedItemsAsync(item);
        foreach (PersistentTag existingTag in tags.Tags)
        {
            foreach (PersistentTaggedItem taggedItem in taggedItems)
            {
                existingTag.Items.RemoveAll(existing => SamePath(existing.Path, taggedItem.Path));
            }
        }

        PersistentTag? tag = FindTag(tags, normalizedTagName);
        if (tag == null)
        {
            tag = new PersistentTag { Name = normalizedTagName };
            tags.Tags.Add(tag);
        }

        foreach (PersistentTaggedItem taggedItem in taggedItems)
        {
            if (tag.Items.Any(existing => SamePath(existing.Path, taggedItem.Path)))
            {
                continue;
            }

            tag.Items.Add(taggedItem);
        }

        RemoveEmptyTags(tags);
        await SaveTagsAsync(tags);
        return await LoadTagNamesAsync();
    }

    public async Task<IReadOnlyList<string>> AddTagsAsync(IEnumerable<string> tagNames, IEnumerable<IStorageItem> items)
    {
        HashSet<string> requestedTags = tagNames
            .Select(NormalizeTagName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        if (requestedTags.Count == 0)
        {
            return await LoadTagNamesAsync();
        }

        List<PersistentTaggedItem> taggedItems = new();
        foreach (IStorageItem item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Path))
            {
                continue;
            }

            foreach (PersistentTaggedItem taggedItem in await GetTaggedItemsAsync(item))
            {
                if (!taggedItems.Any(existing => SamePath(existing.Path, taggedItem.Path)))
                {
                    taggedItems.Add(taggedItem);
                }
            }
        }

        if (taggedItems.Count == 0)
        {
            return await LoadTagNamesAsync();
        }

        PersistentTags tags = await LoadTagsAsync();
        foreach (PersistentTag existingTag in tags.Tags)
        {
            foreach (PersistentTaggedItem taggedItem in taggedItems)
            {
                existingTag.Items.RemoveAll(existing => SamePath(existing.Path, taggedItem.Path));
            }
        }

        foreach (string tagName in requestedTags)
        {
            PersistentTag? tag = FindTag(tags, tagName);
            if (tag == null)
            {
                tag = new PersistentTag { Name = tagName };
                tags.Tags.Add(tag);
            }

            foreach (PersistentTaggedItem taggedItem in taggedItems)
            {
                if (!tag.Items.Any(existing => SamePath(existing.Path, taggedItem.Path)))
                {
                    tag.Items.Add(taggedItem);
                }
            }
        }

        RemoveEmptyTags(tags);
        await SaveTagsAsync(tags);
        return await LoadTagNamesAsync();
    }

    public async Task<IReadOnlyList<string>> RemoveTagAsync(string tagName, IStorageItem item)
    {
        PersistentTags tags = await LoadTagsAsync();
        PersistentTag? tag = FindTag(tags, tagName);
        if (tag == null) return await LoadTagNamesAsync();

        IReadOnlyList<PersistentTaggedItem> taggedItems = await GetTaggedItemsAsync(item);
        foreach (PersistentTaggedItem taggedItem in taggedItems)
        {
            tag.Items.RemoveAll(existing => SamePath(existing.Path, taggedItem.Path));
        }

        RemoveEmptyTags(tags);
        await SaveTagsAsync(tags);
        return await LoadTagNamesAsync();
    }

    public async Task<IReadOnlyList<string>> SetTagsAsync(IStorageItem item, IEnumerable<string> tagNames)
    {
        if (string.IsNullOrWhiteSpace(item.Path))
        {
            return await LoadTagNamesAsync();
        }

        PersistentTags tags = await LoadTagsAsync();
        HashSet<string> requestedTags = tagNames
            .Select(NormalizeTagName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        IReadOnlyList<PersistentTaggedItem> taggedItems = await GetTaggedItemsAsync(item);
        foreach (PersistentTag tag in tags.Tags)
        {
            foreach (PersistentTaggedItem taggedItem in taggedItems)
            {
                tag.Items.RemoveAll(existing => SamePath(existing.Path, taggedItem.Path));
            }
        }

        foreach (string tagName in requestedTags)
        {
            PersistentTag? tag = FindTag(tags, tagName);
            if (tag == null)
            {
                tag = new PersistentTag { Name = tagName };
                tags.Tags.Add(tag);
            }

            foreach (PersistentTaggedItem taggedItem in taggedItems)
            {
                if (!tag.Items.Any(existing => SamePath(existing.Path, taggedItem.Path)))
                {
                    tag.Items.Add(taggedItem);
                }
            }
        }

        RemoveEmptyTags(tags);
        await SaveTagsAsync(tags);
        return await LoadTagNamesAsync();
    }

    public async Task<IReadOnlyList<string>> ClearAllTagsAsync()
    {
        await SaveTagsAsync(new PersistentTags());
        return Array.Empty<string>();
    }

    private async Task<PersistentTags> LoadTagsAsync()
    {
        try
        {
            PersistentTags tags = await _filesService.LoadFromDiskAsync<PersistentTags>(
                ApplicationData.Current.LocalFolder, TagsFileName);
            tags.Tags.RemoveAll(tag => string.IsNullOrWhiteSpace(tag.Name));
            return tags;
        }
        catch
        {
            return new PersistentTags();
        }
    }

    private Task SaveTagsAsync(PersistentTags tags)
    {
        tags.Tags = tags.Tags
            .OrderBy(tag => tag.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return _filesService.SaveToDiskAsync(ApplicationData.Current.LocalFolder, TagsFileName, tags);
    }

    private static PersistentTag? FindTag(PersistentTags tags, string tagName)
    {
        return tags.Tags.FirstOrDefault(tag => tag.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string NormalizeTagName(string tagName)
    {
        return tagName.Trim();
    }

    private static bool SamePath(string left, string right)
    {
        return left.Equals(right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPathInFolder(string path, string folderPath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        string normalizedFolder = folderPath.TrimEnd('\\', '/');
        return path.StartsWith(normalizedFolder + "\\", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(normalizedFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static void RemoveEmptyTags(PersistentTags tags)
    {
        tags.Tags.RemoveAll(tag => tag.Items.Count == 0);
    }

    private static async Task<IReadOnlyList<PersistentTaggedItem>> GetTaggedItemsAsync(IStorageItem item)
    {
        if (item is StorageFile file)
        {
            return FilesHelpers.SupportedVideoFormats.Contains(file.FileType.ToLowerInvariant())
                ? new[] { ToPersistentTaggedItem(file) }
                : Array.Empty<PersistentTaggedItem>();
        }

        if (item is not StorageFolder folder)
        {
            return Array.Empty<PersistentTaggedItem>();
        }

        return await GetFolderVideoItemsAsync(folder);
    }

    private static async Task<IReadOnlyList<PersistentTaggedItem>> GetFolderVideoItemsAsync(StorageFolder folder)
    {
        List<PersistentTaggedItem> taggedItems = new();
        try
        {
            QueryOptions queryOptions = new(CommonFileQuery.DefaultQuery, FilesHelpers.SupportedVideoFormats)
            {
                FolderDepth = FolderDepth.Deep
            };

            StorageFileQueryResult query = folder.CreateFileQueryWithOptions(queryOptions);
            uint fetchIndex = 0;
            while (true)
            {
                IReadOnlyList<StorageFile> files = await query.GetFilesAsync(fetchIndex, 200);
                if (files.Count == 0) break;
                fetchIndex += (uint)files.Count;

                foreach (StorageFile storageFile in files)
                {
                    taggedItems.Add(ToPersistentTaggedItem(storageFile));
                }
            }
        }
        catch
        {
            await AddFolderVideoItemsRecursivelyAsync(folder, taggedItems);
        }

        return taggedItems;
    }

    private static async Task AddFolderVideoItemsRecursivelyAsync(
        StorageFolder folder,
        ICollection<PersistentTaggedItem> taggedItems)
    {
        try
        {
            IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
            foreach (StorageFile file in files)
            {
                if (FilesHelpers.SupportedVideoFormats.Contains(file.FileType.ToLowerInvariant()))
                {
                    taggedItems.Add(ToPersistentTaggedItem(file));
                }
            }

            IReadOnlyList<StorageFolder> folders = await folder.GetFoldersAsync();
            foreach (StorageFolder subfolder in folders)
            {
                await AddFolderVideoItemsRecursivelyAsync(subfolder, taggedItems);
            }
        }
        catch
        {
            // Skip subfolders that Windows will not let us enumerate.
        }
    }

    private static PersistentTaggedItem ToPersistentTaggedItem(StorageFile file)
    {
        return new PersistentTaggedItem
        {
            Name = file.Name,
            Path = file.Path,
            IsFolder = false
        };
    }
}
