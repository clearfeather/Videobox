#nullable enable

namespace Screenbox.Core.Messages;

public sealed class CustomThumbnailSetNotificationMessage
{
    public string MediaName { get; }

    public CustomThumbnailSetNotificationMessage(string mediaName)
    {
        MediaName = mediaName;
    }
}
