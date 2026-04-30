using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Helpers;
using Screenbox.Core.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace Screenbox.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        internal SettingsPageViewModel ViewModel => (SettingsPageViewModel)DataContext;

        internal CommonViewModel Common { get; }

        private string[] VlcCommandLineHelpTextParts { get; }
        private bool _isAppLockDialogOpen;

        public SettingsPage()
        {
            this.InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<SettingsPageViewModel>();
            Common = Ioc.Default.GetRequiredService<CommonViewModel>();

            var helpText = Strings.Resources.VlcCommandLineHelpText;
            VlcCommandLineHelpTextParts = helpText.Contains("{0}")
                ? helpText.Split("{0}").Select(s => s.Trim()).Take(2).ToArray()
                : new[] { helpText, string.Empty };

            // Set the "System default" language option string
            var systemLanguageOption = ViewModel.AvailableLanguages[0];
            systemLanguageOption.NativeName = Strings.Resources.LanguageSystemDefault;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.LoadStartupTagsAsync();
            await ViewModel.LoadLibraryLocations();
            await AudioVisualSelector.ViewModel.InitializeVisualizers();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            ViewModel.OnNavigatedFrom();
        }

        private async void SetAppLockPinButton_Click(object sender, RoutedEventArgs e)
        {
            string? pin = await PromptForPinAsync("Set app lock PIN", "Save");
            if (pin == null)
            {
                return;
            }

            ViewModel.SetAppLockPin(pin);
        }

        private async void ClearAppLockPinButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.HasAppLockPin || _isAppLockDialogOpen)
            {
                return;
            }

            _isAppLockDialogOpen = true;
            try
            {
                ContentDialog dialog = new()
                {
                    Title = "Clear app lock PIN?",
                    Content = "VideoBox will no longer ask for a PIN when it opens.",
                    PrimaryButtonText = "Clear",
                    CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close
                };

                ContentDialogResult result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    ViewModel.ClearAppLockPin();
                }
            }
            finally
            {
                _isAppLockDialogOpen = false;
            }
        }

        private async Task<string?> PromptForPinAsync(string title, string primaryButtonText)
        {
            if (_isAppLockDialogOpen)
            {
                return null;
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

            StackPanel panel = new();
            panel.Children.Add(pinBox);
            panel.Children.Add(errorText);

            ContentDialog dialog = new()
            {
                Title = title,
                Content = panel,
                PrimaryButtonText = primaryButtonText,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (PinLockHelper.IsValidPin(pinBox.Password))
                {
                    return;
                }

                args.Cancel = true;
                errorText.Text = "Enter exactly 4 digits.";
            };

            _isAppLockDialogOpen = true;
            try
            {
                ContentDialogResult result = await dialog.ShowAsync();
                return result == ContentDialogResult.Primary ? pinBox.Password : null;
            }
            finally
            {
                _isAppLockDialogOpen = false;
            }
        }
    }
}
