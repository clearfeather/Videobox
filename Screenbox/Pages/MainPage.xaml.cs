#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;
using NavigationViewBackRequestedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewBackRequestedEventArgs;
using NavigationViewDisplayMode = Microsoft.UI.Xaml.Controls.NavigationViewDisplayMode;
using NavigationViewDisplayModeChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewDisplayModeChangedEventArgs;
using NavigationViewItem = Microsoft.UI.Xaml.Controls.NavigationViewItem;
using NavigationViewItemInvokedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewItemInvokedEventArgs;
using NavigationViewItemSeparator = Microsoft.UI.Xaml.Controls.NavigationViewItemSeparator;
using NavigationViewSelectionChangedEventArgs = Microsoft.UI.Xaml.Controls.NavigationViewSelectionChangedEventArgs;

namespace Screenbox.Pages
{
    public sealed partial class MainPage : Page, IContentFrame
    {
        public Type ContentSourcePageType => ContentFrame.SourcePageType;

        public object? FrameContent => ContentFrame.Content;

        public bool CanGoBack => ContentFrame.CanGoBack;

        private MainPageViewModel ViewModel => (MainPageViewModel)DataContext;

        private readonly Dictionary<string, Type> _pages;
        private readonly ISettingsService _settingsService;
        private readonly ITagsService _tagsService;
        private bool _appUnlocked;
        private bool _isResizingPane;
        private uint _paneResizePointerId;

        private const double MinNavigationPaneWidth = 240;
        private const double MaxNavigationPaneWidth = 520;
        private const double PaneResizeHandleWidth = 8;

        private sealed class TagNavigationMetadata
        {
            public TagNavigationMetadata(string name)
            {
                Name = name;
            }

            public string Name { get; }
        }

        public MainPage()
        {
            InitializeComponent();

            _pages = new Dictionary<string, Type>
            {
                { "home", typeof(HomePage) },
                { "videos", typeof(VideosPage) },
                { "recent", typeof(RecentPage) },
                { "favorites", typeof(FavoritesPage) },
                { "tag", typeof(TagPage) },
                { "queue", typeof(PlayQueuePage) },
                { "playlists", typeof(PlaylistsPage) },
                { "settings", typeof(SettingsPage) }
            };

            _tagsService = Ioc.Default.GetRequiredService<ITagsService>();
            _settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            DataContext = Ioc.Default.GetRequiredService<MainPageViewModel>();
            NavView.OpenPaneLength = ClampNavigationPaneWidth(_settingsService.NavigationPaneWidth);
            UpdatePaneResizeHandle();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            ContentFrame.Navigating += ContentFrame_Navigating;
            WeakReferenceMessenger.Default.Register<MainPage, TagsChangedMessage>(
                this,
                static (recipient, message) => _ = recipient.RefreshTagNavigationItemsAsync());
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            PlayerFrame.Navigate(typeof(PlayerPage), e.Parameter);
            if (e.Parameter is true)
            {
                ViewModel.PlayerVisible = true;
            }

            // NavView remembers if the pane was open last time
            ViewModel.IsPaneOpen = NavView.IsPaneOpen;
        }

        protected override void OnKeyDown(KeyRoutedEventArgs e)
        {
            e.Handled = ViewModel.ProcessGamepadKeyDown(e.Key);
            base.OnKeyDown(e);
        }

        public void GoBack()
        {
            TryGoBack();
        }

        public void NavigateContent(Type pageType, object? parameter)
        {
            ViewModel.PlayerVisible = false;
            ContentFrame.Navigate(pageType, parameter, new SuppressNavigationTransitionInfo());
        }

        private async void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Window.Current.Dispatcher.AcceleratorKeyActivated += CoreDispatcher_AcceleratorKeyActivated;
            SystemNavigationManager.GetForCurrentView().BackRequested += System_BackRequested;
            Window.Current.CoreWindow.PointerPressed += CoreWindow_PointerPressed;
            ViewModel.NavigationViewDisplayMode = (Windows.UI.Xaml.Controls.NavigationViewDisplayMode)NavView.DisplayMode;
            UpdatePaneResizeHandle();
            if (!ViewModel.PlayerVisible)
            {
                if (!await UnlockAppIfNeededAsync())
                {
                    return;
                }

                AppTitleBar.SetDragRegion();
                await ViewModel.FetchLibraries();
                await RefreshTagNavigationItemsAsync();
                ResetNavigationMenuScrollOffset();
                NavigateToStartupDestination();
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.PlayerVisible))
            {
                if (!ViewModel.PlayerVisible)
                {
                    AppTitleBar.SetDragRegion();
                    if (ContentFrame.Content == null)
                    {
                        NavView.SelectedItem = NavView.MenuItems[0];
                        _ = ViewModel.FetchLibraries();
                    }
                }

                UpdateNavigationViewState(NavView.DisplayMode, NavView.IsPaneOpen);
                UpdatePaneResizeHandle();
                AppTitleBar.SetCaptionButtonColor(); // We invoke it as late as possible to ensure the title bar is visible.
            }
            else if (e.PropertyName == nameof(ViewModel.ShowRecent) &&
                     !ViewModel.ShowRecent &&
                     ContentFrame.CurrentSourcePageType == typeof(RecentPage))
            {
                NavView_Navigate("home");
            }
        }

        private void ContentFrame_Navigating(object sender, NavigatingCancelEventArgs e)
        {
        }

        private void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            throw new Exception("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.IsSettingsSelected)
            {
                NavView_Navigate("settings");
            }
            else if (args.SelectedItemContainer != null)
            {
                NavView_Navigate(args.SelectedItemContainer.Tag);
            }
        }

        private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
        {
            if (args.InvokedItemContainer?.Tag is string navItemTag &&
                navItemTag == "videos" &&
                ReferenceEquals(args.InvokedItemContainer, NavView.SelectedItem))
            {
                NavigateToVideosRoot();
            }
        }

        private void NavView_Navigate(object? navItemTag)
        {
            if (navItemTag is TagNavigationMetadata tagMetadata)
            {
                if (ContentFrame.CurrentSourcePageType != typeof(TagPage) || !Equals(ContentFrame.Tag, tagMetadata.Name))
                {
                    ContentFrame.Tag = tagMetadata.Name;
                    ContentFrame.Navigate(typeof(TagPage), tagMetadata.Name, new SuppressNavigationTransitionInfo());
                }

                return;
            }

            if (navItemTag is not string navItemTagString)
            {
                return;
            }

            if (navItemTagString == "videos")
            {
                NavigateToVideosRoot();
                return;
            }

            Type pageType = navItemTagString == "settings"
                ? typeof(SettingsPage)
                : _pages.GetValueOrDefault(navItemTagString);
            // Get the page type before navigation so you can prevent duplicate
            // entries in the backstack.
            Type? preNavPageType = ContentFrame.CurrentSourcePageType;

            // Only navigate if the selected page isn't currently loaded.
            if (!(pageType is null) && !Type.Equals(preNavPageType, pageType))
            {
                ContentFrame.Tag = null;
                ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
            }
        }

        private void NavigateToVideosRoot()
        {
            Ioc.Default.GetRequiredService<CommonViewModel>().NavigationStates.Remove(typeof(VideosPage));

            if (ContentFrame.Content is VideosPage videosPage)
            {
                videosPage.NavigateToRoot();
                return;
            }

            if (ContentFrame.CurrentSourcePageType != typeof(VideosPage))
            {
                ContentFrame.Tag = null;
                ContentFrame.Navigate(typeof(VideosPage), null, new SuppressNavigationTransitionInfo());
            }
        }

        private void NavigateToStartupDestination()
        {
            switch (_settingsService.StartupDestination)
            {
                case StartupDestinationOption.VideoFolders:
                    NavView_Navigate("videos");
                    break;
                case StartupDestinationOption.Recent when _settingsService.ShowRecent:
                    NavView_Navigate("recent");
                    break;
                case StartupDestinationOption.Favorites:
                    NavView_Navigate("favorites");
                    break;
                case StartupDestinationOption.Tag when TryNavigateToStartupTag():
                    break;
                default:
                    NavView_Navigate("home");
                    break;
            }
        }

        private bool TryNavigateToStartupTag()
        {
            string tagName = _settingsService.StartupTag;
            if (string.IsNullOrWhiteSpace(tagName) || !HasTagNavigationItem(tagName))
            {
                return false;
            }

            ContentFrame.Tag = tagName;
            ContentFrame.Navigate(typeof(TagPage), tagName, new SuppressNavigationTransitionInfo());
            return true;
        }

        private bool HasTagNavigationItem(string tagName)
        {
            return NavView.MenuItems
                .OfType<NavigationViewItem>()
                .Any(item => item.Tag is TagNavigationMetadata metadata &&
                             metadata.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase));
        }

        private async Task<bool> UnlockAppIfNeededAsync()
        {
            if (_appUnlocked)
            {
                return true;
            }

            if (!_settingsService.AppLockEnabled ||
                string.IsNullOrWhiteSpace(_settingsService.AppLockPinHash) ||
                string.IsNullOrWhiteSpace(_settingsService.AppLockPinSalt))
            {
                _appUnlocked = true;
                return true;
            }

            PasswordBox pinBox = new()
            {
                MaxLength = 4,
                PlaceholderText = "4-digit PIN"
            };
            InputScope inputScope = new();
            inputScope.Names.Add(new InputScopeName(InputScopeNameValue.Number));
            pinBox.InputScope = inputScope;

            TextBlock errorText = new()
            {
                Margin = new Thickness(0, 8, 0, 0),
                Text = string.Empty
            };

            StackPanel panel = new()
            {
                Children =
                {
                    pinBox,
                    errorText
                }
            };

            ContentDialog dialog = new()
            {
                Title = "Enter PIN",
                Content = panel,
                PrimaryButtonText = "Unlock",
                CloseButtonText = "Exit",
                DefaultButton = ContentDialogButton.Primary
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (PinLockHelper.VerifyPin(
                        pinBox.Password,
                        _settingsService.AppLockPinSalt,
                        _settingsService.AppLockPinHash))
                {
                    _appUnlocked = true;
                    return;
                }

                args.Cancel = true;
                pinBox.Password = string.Empty;
                errorText.Text = "That PIN didn't match.";
            };

            ContentDialogResult result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && _appUnlocked)
            {
                return true;
            }

            Application.Current.Exit();
            return false;
        }

        private void CoreDispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
        {
            if (args is
                {
                    EventType: CoreAcceleratorKeyEventType.SystemKeyDown,
                    VirtualKey: VirtualKey.Left,
                    KeyStatus.IsMenuKeyDown: true,
                    Handled: false
                })
            {
                args.Handled = TryGoBack();
            }
        }

        private void System_BackRequested(object sender, BackRequestedEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = TryGoBack();
            }
        }

        private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            TryGoBack();
        }

        private void CoreWindow_PointerPressed(CoreWindow sender, PointerEventArgs e)
        {
            // Handle mouse back button.
            if (e.CurrentPoint.Properties.IsXButton1Pressed)
            {
                e.Handled = TryGoBack();
            }
        }

        private bool TryGoBack()
        {
            // Don't go back if the nav pane is overlayed.
            if (NavView.IsPaneOpen &&
                NavView.DisplayMode is NavigationViewDisplayMode.Compact or NavigationViewDisplayMode.Minimal)
                NavView.IsPaneOpen = false;

            if (ViewModel.PlayerVisible && PlayerFrame.Content is PlayerPage { ViewModel: { } vm })
            {
                vm.GoBack();
                return true;
            }

            if (ContentFrame.Content is IContentFrame { CanGoBack: true } page)
            {
                page.GoBack();
                return true;
            }

            if (!ContentFrame.CanGoBack)
                return false;

            ContentFrame.GoBack();
            return true;
        }

        private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
        {
            NavView.IsBackEnabled = ContentFrame.CanGoBack;

            if (ContentFrame.SourcePageType == typeof(SettingsPage))
            {
                // SettingsItem is not part of NavView.MenuItems, and doesn't have a Tag.
                NavView.SelectedItem = (NavigationViewItem)NavView.SettingsItem;
            }
            else if (ContentFrame.SourcePageType != null)
            {
                NavigationViewItem? selectedItem = GetNavigationItemForPageType(e.SourcePageType);

                if (selectedItem == null && ViewModel.TryGetPageTypeFromParameter(e.Parameter, out Type pageType))
                {
                    selectedItem = GetNavigationItemForPageType(pageType);
                }

                NavView.SelectedItem = selectedItem;
            }
        }

        private NavigationViewItem? GetNavigationItemForPageType(Type pageType)
        {
            if (pageType == typeof(TagPage) && ContentFrame.Tag is string tagName)
            {
                return NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(n => n.Tag is TagNavigationMetadata metadata &&
                                         metadata.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase));
            }

            KeyValuePair<string, Type> item = _pages.FirstOrDefault(p => p.Value == pageType);

            NavigationViewItem? selectedItem = NavView.MenuItems
                .OfType<NavigationViewItem>()
                .FirstOrDefault(n => n.Tag.Equals(item.Key));

            return selectedItem;
        }

        private async System.Threading.Tasks.Task RefreshTagNavigationItemsAsync()
        {
            UpdateTagNavigationItems(await _tagsService.LoadTagNamesAsync());
            ResetNavigationMenuScrollOffset();
        }

        private void UpdateTagNavigationItems(IReadOnlyList<string> tagNames)
        {
            string? selectedTag = (NavView.SelectedItem as NavigationViewItem)?.Tag is TagNavigationMetadata selectedTagMetadata
                ? selectedTagMetadata.Name
                : null;

            for (int i = NavView.MenuItems.Count - 1; i >= 0; i--)
            {
                if (NavView.MenuItems[i] is NavigationViewItem item &&
                    item.Tag is TagNavigationMetadata)
                {
                    NavView.MenuItems.RemoveAt(i);
                }
            }

            int insertIndex = NavView.MenuItems
                .Select((item, index) => new { item, index })
                .FirstOrDefault(pair => pair.item is NavigationViewItemSeparator)?.index
                ?? NavView.MenuItems.Count;

            foreach (string tagName in tagNames
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            {
                NavigationViewItem item = new()
                {
                    Icon = new FontIcon { Glyph = "\uE8EC" },
                    Content = tagName,
                    Tag = new TagNavigationMetadata(tagName)
                };
                ToolTipService.SetToolTip(item, tagName);
                NavView.MenuItems.Insert(insertIndex++, item);
            }

            if (!string.IsNullOrWhiteSpace(selectedTag))
            {
                NavView.SelectedItem = NavView.MenuItems
                    .OfType<NavigationViewItem>()
                    .FirstOrDefault(item => item.Tag is TagNavigationMetadata metadata &&
                                            metadata.Name.Equals(selectedTag, StringComparison.CurrentCultureIgnoreCase))
                    ?? NavView.SelectedItem;
            }

            ResetNavigationMenuScrollOffset();
        }

        private void ResetNavigationMenuScrollOffset()
        {
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Low, () =>
            {
                ScrollViewer? menuScrollViewer = FindDescendant<ScrollViewer>(NavView, viewer =>
                    !string.IsNullOrWhiteSpace(viewer.Name) &&
                    viewer.Name.IndexOf("MenuItems", StringComparison.OrdinalIgnoreCase) >= 0);

                menuScrollViewer?.ChangeView(null, 0, null, true);
            });
        }

        private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
            where T : DependencyObject
        {
            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match && predicate(match))
                {
                    return match;
                }

                T? descendant = FindDescendant(child, predicate);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private void NavView_OnDisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateNavigationViewState(args.DisplayMode, NavView.IsPaneOpen);
            ViewModel.NavigationViewDisplayMode = (Windows.UI.Xaml.Controls.NavigationViewDisplayMode)args.DisplayMode;
            UpdatePaneResizeHandle();
            ResetNavigationMenuScrollOffset();
        }

        private void UpdateNavigationViewState(NavigationViewDisplayMode displayMode, bool paneOpen)
        {
            if (ViewModel.PlayerVisible)
            {
                VisualStateManager.GoToState(this, "Hidden", true);
                UpdatePaneResizeHandle();
                return;
            }

            switch (displayMode)
            {
                case NavigationViewDisplayMode.Minimal:
                    VisualStateManager.GoToState(this, "Minimal", true);
                    break;
                case NavigationViewDisplayMode.Compact when paneOpen:
                    VisualStateManager.GoToState(this, "CompactPaneOverlay", true);
                    break;
                case NavigationViewDisplayMode.Expanded when paneOpen:
                    VisualStateManager.GoToState(this, "Expanded", true);
                    break;
                case NavigationViewDisplayMode.Expanded:
                case NavigationViewDisplayMode.Compact:
                    VisualStateManager.GoToState(this, "Compact", true);
                    break;
            }

            UpdatePaneResizeHandle();
        }

        private void NavView_OnPaneOpening(NavigationView sender, object args)
        {
            UpdateNavigationViewState(sender.DisplayMode, sender.IsPaneOpen);
            UpdatePaneResizeHandle();
        }

        private void NavView_OnPaneClosing(NavigationView sender, object args)
        {
            // Deferred to ensure IsPaneOpen reports the correct state
            // when closing the pane via gamepad.
            _ = Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    UpdateNavigationViewState(sender.DisplayMode, sender.IsPaneOpen);
                    UpdatePaneResizeHandle();
                });
        }

        private void PaneResizeHandle_OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.SizeWestEast, 0);
        }

        private void PaneResizeHandle_OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizingPane)
            {
                Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
            }
        }

        private void PaneResizeHandle_OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isResizingPane = true;
            _paneResizePointerId = e.Pointer.PointerId;
            PaneResizeHandle.CapturePointer(e.Pointer);
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.SizeWestEast, 0);
            e.Handled = true;
        }

        private void PaneResizeHandle_OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizingPane || e.Pointer.PointerId != _paneResizePointerId)
            {
                return;
            }

            double requestedWidth = e.GetCurrentPoint(this).Position.X;
            double width = ClampNavigationPaneWidth(requestedWidth);
            NavView.OpenPaneLength = width;
            _settingsService.NavigationPaneWidth = (int)Math.Round(width);
            UpdatePaneResizeHandle();
            e.Handled = true;
        }

        private void PaneResizeHandle_OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (!_isResizingPane || e.Pointer.PointerId != _paneResizePointerId)
            {
                return;
            }

            _isResizingPane = false;
            PaneResizeHandle.ReleasePointerCapture(e.Pointer);
            Window.Current.CoreWindow.PointerCursor = new CoreCursor(CoreCursorType.Arrow, 0);
            e.Handled = true;
        }

        private void UpdatePaneResizeHandle()
        {
            if (PaneResizeHandle == null)
            {
                return;
            }

            bool canResize = !ViewModel.PlayerVisible &&
                             NavView.DisplayMode == NavigationViewDisplayMode.Expanded &&
                             NavView.IsPaneOpen;
            PaneResizeHandle.Visibility = canResize ? Visibility.Visible : Visibility.Collapsed;
            PaneResizeHandle.Margin = new Thickness(NavView.OpenPaneLength - PaneResizeHandleWidth / 2, 0, 0, 0);
        }

        private static double ClampNavigationPaneWidth(double width)
        {
            if (width < MinNavigationPaneWidth)
            {
                return MinNavigationPaneWidth;
            }

            if (width > MaxNavigationPaneWidth)
            {
                return MaxNavigationPaneWidth;
            }

            return width;
        }

        private void NavViewSearchBox_OnTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                ViewModel.UpdateSearchSuggestions(sender.Text);
            }
        }

        private void NavViewSearchBox_OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
        {
            // Update the text box when navigating through the suggestion list using the keyboard.
            if (args.SelectedItem is SearchSuggestionItem suggestion)
            {
                // We set sender.Text directly instead of ViewModel.SearchQuery
                // to avoid triggering TextChanged event.
                sender.Text = suggestion.Name;
            }
        }

        private void NavViewSearchBox_OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            if (args.ChosenSuggestion is SearchSuggestionItem suggestion)
            {
                ViewModel.SelectSuggestion(suggestion);
            }
            else
            {
                ViewModel.SubmitSearch(args.QueryText);
            }

            ViewModel.SearchQuery = string.Empty;
            ViewModel.SearchSuggestions.Clear();
            if (NavView.IsPaneOpen && NavView.DisplayMode != NavigationViewDisplayMode.Expanded)
            {
                ViewModel.IsPaneOpen = false;
            }
        }

        /// <summary>
        /// Give the <see cref="NavViewSearchBox"/> text entry box focus ("Focused" visual state) through the keyboard shortcut combination.
        /// </summary>
        private void NavViewSearchBoxKeyboardAcceleratorFocus_OnInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            NavViewSearchBox.Focus(FocusState.Keyboard);
            args.Handled = true;
        }

        /// <summary>
        /// Give the <see cref="NavViewSearchBox"/> text entry box focus ("Focused" visual state) through the access key combination.
        /// </summary
        private void NavViewSearchBox_OnAccessKeyInvoked(UIElement sender, AccessKeyInvokedEventArgs args)
        {
            NavViewSearchBox.Focus(FocusState.Keyboard);
            args.Handled = true;
        }

        private void NavView_DragOver(object sender, DragEventArgs e)
        {
            e.Handled = true;
            e.AcceptedOperation = DataPackageOperation.Link;
            if (e.DragUIOverride != null) e.DragUIOverride.Caption = Strings.Resources.Play;
        }

        private void NavView_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
            ViewModel.OnDrop(e.DataView);
        }
    }
}
