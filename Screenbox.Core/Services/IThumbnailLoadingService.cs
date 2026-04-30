#nullable enable

using System;

namespace Screenbox.Core.Services;

public interface IThumbnailLoadingService
{
    bool IsBusy { get; }
    bool ShouldShowStatus { get; }
    int ActiveCount { get; }
    string StatusText { get; }

    IDisposable BeginOperation();
}
