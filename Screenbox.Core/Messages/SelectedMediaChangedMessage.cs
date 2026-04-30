using CommunityToolkit.Mvvm.Messaging.Messages;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Messages;

public sealed class SelectedMediaChangedMessage : ValueChangedMessage<MediaViewModel>
{
    public SelectedMediaChangedMessage(MediaViewModel value) : base(value)
    {
    }
}
