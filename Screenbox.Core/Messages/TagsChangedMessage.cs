#nullable enable

using System.Collections.Generic;

namespace Screenbox.Core.Messages;

public sealed class TagsChangedMessage
{
    public IReadOnlyList<string> Tags { get; }

    public TagsChangedMessage(IReadOnlyList<string> tags)
    {
        Tags = tags;
    }
}
