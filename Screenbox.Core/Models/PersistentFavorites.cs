#nullable enable

using System.Collections.Generic;

namespace Screenbox.Core.Models;

public sealed class PersistentFavorites
{
    public List<PersistentMediaRecord> Items { get; set; } = new();
}
