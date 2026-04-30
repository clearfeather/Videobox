#nullable enable

namespace Screenbox.Core.Models;

public sealed class PersistentTaggedItem
{
    public string Name { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public bool IsFolder { get; set; }
}
