using System.Windows;

namespace RisaBotApp.Views.Dialogs;

/// <summary>
/// Themed stand-in for MAUI's <c>DisplayPromptAsync</c>. Used for the Wi-Fi password
/// entry and for manual host/IP entry on the discovery screen.
/// </summary>
public partial class PromptDialog : Window
{
    /// <summary>The text the user entered. Only meaningful when ShowDialog returned true.</summary>
    public string Value { get; private set; } = string.Empty;

    public PromptDialog(
        string title,
        string message,
        string placeholder,
        bool isPassword,
        string initialValue)
    {
        InitializeComponent();

        Title           = title;
        TitleText.Text  = title;
        MessageText.Text = message;

        // Placeholder rendering lives in the DarkTextBox template, which reads it from Tag
        // — WPF has no native watermark. PasswordBox gets none: it exposes no Text property
        // for a trigger to test, and the workarounds all involve holding the secret somewhere
        // extra. The message line carries the hint instead.
        ValueInput.Tag = placeholder;

        if (isPassword)
        {
            ValueInput.Visibility     = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordInput.Password   = initialValue;
        }
        else
        {
            ValueInput.Text = initialValue;
            ValueInput.SelectAll();
        }

        // Focus has to wait for the window to be shown; setting it in the constructor
        // targets a control that is not yet in the visual tree and silently does nothing.
        Loaded += (_, _) =>
        {
            if (isPassword) PasswordInput.Focus();
            else            ValueInput.Focus();
        };
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        Value = PasswordInput.Visibility == Visibility.Visible
            ? PasswordInput.Password
            : ValueInput.Text;

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
