using System.Windows;
using System.Windows.Input;
using TermLock.Core;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TermLock.UI;

/// <summary>
/// Profile dialog: display name, device type and passphrase. The same window
/// is used on first run, from the tray and from `--setup`.
/// </summary>
public partial class SetupWindow : Window
{
    private readonly AppConfig _config;

    public SetupWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        UserNameInput.Text = string.IsNullOrWhiteSpace(config.UserName)
            ? "Jadeonit"
            : config.UserName;

        WindowsRadio.Content = SystemInfo.WindowsLabel();

        if (config.DeviceType.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
        {
            WindowsRadio.IsChecked = true;
        }
        else if (string.Equals(config.DeviceType, "Laptop", StringComparison.OrdinalIgnoreCase))
        {
            LaptopRadio.IsChecked = true;
        }
        else
        {
            PcRadio.IsChecked = true;
        }

        Loaded += (_, _) =>
        {
            UserNameInput.Focus();
            UserNameInput.SelectAll();
        };
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnUserNameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Pass1.Focus();
            e.Handled = true;
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
        var user = UserNameInput.Text.Trim();
        if (user.Length == 0)
        {
            ErrorText.Text = "username is required";
            UserNameInput.Focus();
            return;
        }

        var device = WindowsRadio.IsChecked == true
            ? SystemInfo.WindowsLabel()
            : LaptopRadio.IsChecked == true ? "Laptop" : "PC";

        var first = Pass1.Password;
        var second = Pass2.Password;

        // Name-only change: leave both passphrase fields empty.
        if (first.Length == 0 && second.Length == 0 && _config.HasPassphrase)
        {
            SaveProfile(user, device);
            DialogResult = true;
            return;
        }

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

        SaveProfile(user, device);
        DialogResult = true;
    }

    private void SaveProfile(string user, string device)
    {
        _config.UserName = user;
        _config.DeviceType = device;
        _config.DisplayName = $"{user} @ {device}";
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
