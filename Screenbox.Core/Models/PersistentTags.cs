#nullable enable

using System.Collections.Generic;

namespace Screenbox.Core.Models;

public sealed class PersistentTags
{
    public List<PersistentTag> Tags { get; set; } = new();
}
