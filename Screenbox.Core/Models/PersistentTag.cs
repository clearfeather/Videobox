#nullable enable

using System.Collections.Generic;

namespace Screenbox.Core.Models;

public sealed class PersistentTag
{
    public string Name { get; set; } = string.Empty;

    public List<PersistentTaggedItem> Items { get; set; } = new();
}
