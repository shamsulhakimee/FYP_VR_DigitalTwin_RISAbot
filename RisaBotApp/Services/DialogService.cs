using System.Windows;
using RisaBotApp.Views.Dialogs;

namespace RisaBotApp.Services;

/// <summary>
/// Replacement for <c>Shell.Current.DisplayAlertAsync</c> / <c>DisplayPromptAsync</c>.
///
/// Every method marshals to the UI thread before showing anything. View-models call these
/// from inside async service work that has already hopped off the dispatcher, and WPF
/// throws outright if a Window is constructed on a background thread — MAUI was forgiving
/// here and the phone code relied on that.
///
/// The API stays Task-returning even though WPF's MessageBox is synchronous, so the call
/// sites port over from MAUI without restructuring.
/// </summary>
public sealed class DialogService
{
    /// <summary>Single-button informational alert.</summary>
    public Task AlertAsync(string title, string message)
        => OnUiThread(() =>
        {
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
            return true;
        });

    /// <summary>Single-button error alert.</summary>
    public Task ErrorAsync(string title, string message)
        => OnUiThread(() =>
        {
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            return true;
        });

    /// <summary>Two-button confirmation. Returns true if the user accepted.</summary>
    public Task<bool> ConfirmAsync(string title, string message, string accept = "Yes", string cancel = "No")
        => OnUiThread(() =>
        {
            // WPF's MessageBox buttons are fixed strings; the accept/cancel captions from the
            // MAUI signature are folded into the body so the wording is not silently lost.
            var body = accept is "Yes" && cancel is "No"
                ? message
                : $"{message}\n\n{accept} = Yes     {cancel} = No";

            return MessageBox.Show(Owner, body, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        });

    /// <summary>
    /// Text prompt. Returns null if the user cancelled, which is how the Wi-Fi flow
    /// distinguishes "cancelled" from "submitted an empty password" (open networks).
    /// </summary>
    public Task<string?> PromptAsync(
        string title,
        string message,
        string placeholder = "",
        bool isPassword = false,
        string initialValue = "")
        => OnUiThread<string?>(() =>
        {
            var dialog = new PromptDialog(title, message, placeholder, isPassword, initialValue)
            {
                Owner = Owner
            };
            return dialog.ShowDialog() == true ? dialog.Value : null;
        });

    // ── Internals ───────────────────────────────────────────────────────────

    private static Window? Owner
        => Application.Current?.Windows
                      .OfType<Window>()
                      .FirstOrDefault(w => w.IsActive)
           ?? Application.Current?.MainWindow;

    private static Task<T> OnUiThread<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
            return Task.FromResult(action());

        return dispatcher.InvokeAsync(action).Task;
    }
}
