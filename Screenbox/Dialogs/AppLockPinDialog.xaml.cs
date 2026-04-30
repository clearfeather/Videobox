#nullable enable

using System;
using System.Threading.Tasks;
using Screenbox.Core.Helpers;
using Screenbox.Helpers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Screenbox.Dialogs;

public sealed partial class AppLockPinDialog : ContentDialog
{
    public string DialogTitle { get; }
    public string PrimaryText { get; }
    public string CloseText { get; }

    private readonly string? _pinSalt;
    private readonly string? _pinHash;
    private readonly bool _verifyExistingPin;

    private AppLockPinDialog(
        string title,
        string primaryText,
        string closeText,
        string? pinSalt = null,
        string? pinHash = null)
    {
        DialogTitle = title;
        PrimaryText = primaryText;
        CloseText = closeText;
        _pinSalt = pinSalt;
        _pinHash = pinHash;
        _verifyExistingPin = !string.IsNullOrWhiteSpace(pinSalt) && !string.IsNullOrWhiteSpace(pinHash);

        this.DefaultStyleKey = typeof(ContentDialog);
        this.InitializeComponent();
        FlowDirection = GlobalizationHelper.GetFlowDirection();
        RequestedTheme = ((FrameworkElement)Window.Current.Content).RequestedTheme;
        IsPrimaryButtonEnabled = false;
        PrimaryButtonClick += AppLockPinDialog_PrimaryButtonClick;
    }

    public static async Task<string?> PromptNewPinAsync()
    {
        AppLockPinDialog dialog = new("Set app lock PIN", "Save", "Cancel");
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? dialog.PinBox.Password : null;
    }

    public static async Task<bool> PromptUnlockAsync(string pinSalt, string pinHash)
    {
        AppLockPinDialog dialog = new("Enter PIN", "Unlock", "Exit", pinSalt, pinHash);
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void PinBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        IsPrimaryButtonEnabled = PinLockHelper.IsValidPin(PinBox.Password);
    }

    private void AppLockPinDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!PinLockHelper.IsValidPin(PinBox.Password))
        {
            args.Cancel = true;
            ErrorText.Text = "Enter exactly 4 digits.";
            return;
        }

        if (!_verifyExistingPin)
        {
            return;
        }

        if (_pinSalt != null &&
            _pinHash != null &&
            PinLockHelper.VerifyPin(PinBox.Password, _pinSalt, _pinHash))
        {
            return;
        }

        args.Cancel = true;
        PinBox.Password = string.Empty;
        ErrorText.Text = "That PIN didn't match.";
    }
}
