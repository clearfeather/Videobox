#nullable enable

using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Screenbox.Core.Services;

public sealed partial class ThumbnailLoadingService : ObservableObject, IThumbnailLoadingService
{
    private int _activeCount;

    public int ActiveCount
    {
        get => _activeCount;
    }

    public bool IsBusy => ActiveCount > 0;

    public bool ShouldShowStatus => IsBusy;

    public string StatusText => ActiveCount == 1
        ? "Loading thumbnail"
        : $"Loading thumbnails ({ActiveCount})";

    public IDisposable BeginOperation()
    {
        Interlocked.Increment(ref _activeCount);
        RaiseActiveCountChanged();
        return new ThumbnailOperation(this);
    }

    private void EndOperation()
    {
        if (Interlocked.Decrement(ref _activeCount) < 0)
        {
            Interlocked.Exchange(ref _activeCount, 0);
        }

        RaiseActiveCountChanged();
    }

    private void RaiseActiveCountChanged()
    {
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShouldShowStatus));
        OnPropertyChanged(nameof(StatusText));
    }

    private sealed class ThumbnailOperation : IDisposable
    {
        private ThumbnailLoadingService? _owner;

        public ThumbnailOperation(ThumbnailLoadingService owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _owner, null)?.EndOperation();
        }
    }
}
