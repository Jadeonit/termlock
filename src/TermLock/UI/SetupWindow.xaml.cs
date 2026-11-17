using System.Windows;
using System.Windows.Input;
using TermLock.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TermLock.UI;

/// <summary>
/// First-run dialog. Day 1 only asks for the passphrase; the full wizard
/// (recovery code, USB key, ntfy topic) arrives on Day 7.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly AppConfig _config;

    public SetupWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        Loaded += (_, _) => Pass1.Focus();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnPass1KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Pass2.Focus();
            e.Handled = true;
        }
    }

    private void OnPass2KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            TryCreate();
            e.Handled = true;
        }
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        TryCreate();
    }

    private void TryCreate()
    {
        var first = Pass1.Password;
        var second = Pass2.Password;

        if (first.Length < 8)
        {
            ErrorText.Text = "passphrase must be at least 8 characters";
            Pass1.Focus();
            return;
        }

        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            ErrorText.Text = "passphrases do not match";
            Pass2.Focus();
            return;
        }

        var (hash, salt, iterations) = Passphrase.Create(first);
        _config.PassphraseHash = hash;
        _config.PassphraseSalt = salt;
        _config.PassphraseIterations = iterations;

        DialogResult = true;
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
