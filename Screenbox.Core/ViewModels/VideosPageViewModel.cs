#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using Windows.Storage;
using Windows.System;
using Windows.UI.Xaml.Navigation;

namespace Screenbox.Core.ViewModels;

public sealed partial class VideosPageViewModel : ObservableRecipient,
    IRecipient<LibraryContentChangedMessage>
{
    public ObservableCollection<StorageFolder> Breadcrumbs { get; }

    [ObservableProperty] private bool _hasVideos;

    /// <summary>Gets a value indicating whether the Videos library is available, used to enable the add-folder command.</summary>
    public bool HasLibrary => true;

    private readonly LibraryContext _libraryContext;
    private readonly ILibraryService _libraryService;
    private readonly DispatcherQueue _dispatcherQueue;

    public VideosPageViewModel(LibraryContext libraryContext, ILibraryService libraryService)
    {
        _libraryContext = libraryContext;
        _libraryService = libraryService;
        _hasVideos = true;
        Breadcrumbs = new ObservableCollection<StorageFolder>();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        IsActive = true;
    }

    public void Receive(LibraryContentChangedMessage message)
    {
        if (message.LibraryId != KnownLibraryId.Videos) return;
        _dispatcherQueue.TryEnqueue(UpdateVideos);
    }

    public void UpdateVideos()
    {
        if (Breadcrumbs.Count == 0 && TryGetFirstFolder(out StorageFolder firstFolder))
            Breadcrumbs.Add(firstFolder);
        HasVideos = _libraryContext.VideoFolders.Count > 0 || _libraryContext.Videos.Count > 0;
        AddFolderCommand.NotifyCanExecuteChanged();
    }

    public void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        IReadOnlyList<StorageFolder>? crumbs = e.Parameter as IReadOnlyList<StorageFolder>;
        UpdateBreadcrumbs(crumbs);
    }

    private bool TryGetFirstFolder(out StorageFolder folder)
    {
        try
        {
            folder = SystemInformation.IsXbox ? KnownFolders.RemovableDevices : KnownFolders.VideosLibrary;
            return true;
        }
        catch (Exception e)
        {
            folder = ApplicationData.Current.TemporaryFolder;
            Messenger.Send(new ErrorMessage(null, e.Message));
            LogService.Log(e);
            return false;
        }
    }

    private void UpdateBreadcrumbs(IReadOnlyList<StorageFolder>? crumbs)
    {
        Breadcrumbs.Clear();
        if (crumbs == null)
        {
            if (TryGetFirstFolder(out StorageFolder firstFolder))
                Breadcrumbs.Add(firstFolder);
        }
        else
        {
            foreach (StorageFolder storageFolder in crumbs)
            {
                Breadcrumbs.Add(storageFolder);
            }
        }
    }

    /// <summary>
    /// Requests adding a new folder to the Videos library.
    /// Sends a <see cref="Core.Messages.FailedToAddFolderNotificationMessage"/> on failure.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasLibrary))]
    private async Task AddFolderAsync()
    {
        try
        {
            StorageFolder? folder = await _libraryService.AddVideoLibraryFolderAsync();
            if (folder == null) return;

            _libraryContext.VideoFolders = (await _libraryService.GetVideoLibraryFoldersAsync()).ToList();
            UpdateVideos();
            Messenger.Send(new RefreshFolderMessage());
            await _libraryService.FetchVideosAsync(_libraryContext, false);
        }
        catch (Exception e)
        {
            Messenger.Send(new FailedToAddFolderNotificationMessage(e.Message));
        }
    }

}
