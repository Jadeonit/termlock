using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TermLock.Core;

namespace TermLock.UI;

public partial class NotificationsWindow : Window
{
    private readonly AppConfig _config;

    public NotificationsWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;

        UrlBox.Text = config.NtfyUrl;
        TopicBox.Text = config.NtfyTopic;
        EnabledBox.IsChecked = config.NtfyEnabled;
        IntervalBox.Text = config.NtfyMinIntervalSeconds.ToString();
        FailSecondsBox.Text = config.FailActionSeconds.ToString();
        SelectFailAction(config.FailAction);
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void SelectFailAction(string action)
    {
        foreach (var item in FailActionBox.Items)
        {
            if (item is ComboBoxItem combo &&
                string.Equals(combo.Content?.ToString(), action, StringComparison.OrdinalIgnoreCase))
            {
                FailActionBox.SelectedItem = combo;
                return;
            }
        }

        FailActionBox.SelectedIndex = 0;
    }

    private void SaveConfig()
    {
        _config.NtfyUrl = string.IsNullOrWhiteSpace(UrlBox.Text)
            ? "https://ntfy.sh"
            : UrlBox.Text.Trim();
        _config.NtfyTopic = TopicBox.Text.Trim();
        _config.NtfyEnabled = EnabledBox.IsChecked == true;
        _config.NtfyMinIntervalSeconds = ParseInt(IntervalBox.Text, 10, 1, 600);
        _config.FailAction = (FailActionBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Shutdown";
        _config.FailActionSeconds = ParseInt(FailSecondsBox.Text, 15, 3, 300);
        _config.Save();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SaveConfig();
        StatusText.Text = "saved";
    }

    private void OnTest(object sender, RoutedEventArgs e)
    {
        SaveConfig();

        if (string.IsNullOrWhiteSpace(_config.NtfyTopic))
        {
            StatusText.Text = "enter a topic first";
            return;
        }

        var notifier = new NtfyNotifier(_config);
        notifier.Notify(
            "TermLock",
            "test notification",
            "white_check_mark",
            "default",
            force: true);

        StatusText.Text = "test sent — check the phone; errors are in the log";
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static int ParseInt(string text, int fallback, int min, int max)
    {
        return int.TryParse(text.Trim(), out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }
}
