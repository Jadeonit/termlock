using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using TermLock.Core;
using TermLock.Platform;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace TermLock.UI;

/// <summary>
/// The curtain itself: a borderless topmost window that covers the whole
/// virtual desktop. It never locks the Windows session, so background work
/// (servers, agents, RDP) keeps running while the screen is covered.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly SystemStats _stats;
    private readonly NtfyNotifier _notifier;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _cursorBlink;
    private readonly DispatcherTimer _keepOnTop;
    private readonly DispatcherTimer _usbPoll;
    private readonly DispatcherTimer _failTimer;
    private readonly KeyboardHook _keyboard;

    private int _attempts;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private DateTime _failAt = DateTime.MinValue;
    private string _failAction = "shutdown";
    private bool _cursorVisible = true;
    private bool _allowClose;
    private bool _usbPresent;
    private string _layoutCode = "EN";

    private static readonly System.Windows.Media.Brush DimLayoutBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0A, 0x8F, 0x3C));

    private static readonly System.Windows.Media.Brush WarnLayoutBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xB0, 0x00));

    /// <summary>Raised after a correct passphrase, on the UI thread.</summary>
    public event EventHandler? Unlocked;

    public MainWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        _stats = new SystemStats();
        _notifier = new NtfyNotifier(config);

        _keyboard = new KeyboardHook(() => IsVisible, ShowLock);

        _clock = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) =>
            {
                _stats.Refresh();
                UpdateHeader();
                UpdateStats();
            },
            Dispatcher);

        _cursorBlink = new DispatcherTimer(
            TimeSpan.FromMilliseconds(530),
            DispatcherPriority.Background,
            (_, _) => ToggleCursor(),
            Dispatcher);

        _keepOnTop = new DispatcherTimer(
            TimeSpan.FromMilliseconds(750),
            DispatcherPriority.Background,
            (_, _) => KeepOnTop(),
            Dispatcher);

        _usbPoll = new DispatcherTimer(
            TimeSpan.FromMilliseconds(1500),
            DispatcherPriority.Background,
            (_, _) => PollUsb(),
            Dispatcher);

        _failTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => TickFailAction(),
            Dispatcher);

        HiddenInput.PasswordChanged += (_, _) => UpdatePrompt();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        Activated += (_, _) => UpdateLayoutIndicator();

        BuildBanner();
        BuildArt();
        UpdateHeader();
        UpdateLayoutIndicator();
        UpdatePrompt();
        UpdateStatus("awaiting passphrase");
    }

    /// <summary>Show the curtain. Safe to call when it is already visible.</summary>
    public void ShowLock()
    {
        Log.Write("show lock requested");
        FitToVirtualScreen();
        Show();
        Activate();

        HiddenInput.Clear();
        HiddenInput.Focus();
        Keyboard.Focus(HiddenInput);

        Topmost = true;
        _failTimer.Stop();
        _failAt = DateTime.MinValue;
        UpdateStatus("awaiting passphrase");
        UpdateLayoutIndicator();
        _stats.ResetCpuBaseline();
        UpdateStats();

        _usbPresent = UsbKey.IsPresent(_config);
        _usbPoll.Start();

        _clock.Start();
        _cursorBlink.Start();
        _keepOnTop.Start();

        Log.Write($"show lock done visible={IsVisible} active={IsActive}");
    }

    /// <summary>
    /// Dev/CI helper: render the visual tree to a PNG without relying on DWM.
    /// Used by `--shot` and by the README screenshot pipeline.
    /// </summary>
    public void RenderToFile(string path)
    {
        _stats.Refresh();
        UpdateStats();
        UpdateLayout();

        var width = (int)Math.Max(1, ActualWidth);
        var height = (int)Math.Max(1, ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Used by the app when it is really shutting down.</summary>
    public void ForceClose()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 is not an unlock. The curtain can only be dismissed with the
        // passphrase (or the process being killed by the owner).
        if (!_allowClose)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void FitToVirtualScreen()
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    private void BuildBanner()
    {
        BannerText.Text = """
 ████████╗███████╗██████╗ ███╗   ███╗██╗      ██████╗  ██████╗██╗  ██╗
 ╚══██╔══╝██╔════╝██╔══██╗████╗ ████║██║     ██╔═══██╗██╔════╝██║ ██╔╝
    ██║   █████╗  ██████╔╝██╔████╔██║██║     ██║   ██║██║     █████╔╝
    ██║   ██╔══╝  ██╔══██╗██║╚██╔╝██║██║     ██║   ██║██║     ██╔═██╗
    ██║   ███████╗██║  ██║██║ ╚═╝ ██║███████╗╚██████╔╝╚██████╗██║  ██╗
    ╚═╝   ╚══════╝╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝ ╚═════╝  ╚═════╝╚═╝  ╚═╝
""";
    }

    private void BuildArt()
    {
        ArtText.Text = """
   .---------------------------.
   |  >_ termlock --status     |
   |                           |
   |    .-----------------.    |
   |    |  #############  |    |
   |    |  #############  |    |
   |    |  #############  |    |
   |    '-----------------'    |
   |         \_______/         |
   |                           |
   |  curtain ......... ARMED  |
   '---------------------------'
""";
    }

    private void UpdateHeader()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        MetaText.Text =
            $"v0.1  ·  {_config.DisplayNameFull}  ·  {DateTime.Now:HH:mm:ss}";
    }

    private void UpdateStats()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var cooldown = _cooldownUntil > DateTime.UtcNow
            ? $"{Math.Ceiling((_cooldownUntil - DateTime.UtcNow).TotalSeconds):0}s"
            : "-";

        var secureBoot = _stats.SecureBoot switch
        {
            true => "ON",
            false => "OFF",
            _ => "N/A"
        };

        StatsText.Text = $"""
SYSTEM
  user       {_config.UserName}
  os         {SystemInfo.WindowsProduct()}
  uptime     {uptime.Days}d {uptime.Hours:00}h {uptime.Minutes:00}m
  cpu        [{SystemStats.Bar(_stats.CpuPercent)}] {_stats.CpuPercent:0}%
  ram        [{SystemStats.Bar(_stats.RamPercent)}] {_stats.RamPercent:0}%  {_stats.RamUsedGb:0.0}/{_stats.RamTotalGb:0.0} GB
  disk       [{SystemStats.Bar(_stats.DiskPercent)}] {_stats.DiskPercent:0}%  {_stats.DiskFreeGb:0.0}/{_stats.DiskTotalGb:0.0} GB free

SECURITY
  defender   ....... {(_stats.DefenderActive ? "ACTIVE" : "OFF")}
  firewall   ....... {(_stats.FirewallActive ? "ACTIVE" : "OFF")}
  secureboot ....... {secureBoot}
  curtain    ....... ARMED

AUTH
  method     passphrase / PBKDF2-SHA256
  layout     {_layoutCode}
  recovery   {(_config.HasRecovery ? "enrolled" : "off")}
  usb        {(!_config.HasUsbKey ? "off" : (_usbPresent ? "inserted" : "enrolled"))}
  attempts   {_attempts} / {_config.MaxAttempts}
  cooldown   {cooldown}
""";
    }

    private void UpdatePrompt()
    {
        InputText.Text = new string('●', HiddenInput.Password.Length);
        UpdateLayoutIndicator();
        UpdateStats();
    }

    private void UpdateLayoutIndicator()
    {
        var culture = InputLanguageManager.Current.CurrentInputLanguage;
        _layoutCode = culture?.TwoLetterISOLanguageName?.ToUpperInvariant() ?? "??";

        var caps = Keyboard.IsKeyToggled(Key.CapsLock);
        var hasCyrillic = HiddenInput.Password.Any(c => c >= '\u0400' && c <= '\u04FF');

        var text = $"[{_layoutCode}]";
        if (caps)
        {
            text += " CAPS";
        }

        if (hasCyrillic)
        {
            text += " CYR";
        }

        LayoutText.Text = text;
        LayoutText.Foreground = caps || hasCyrillic ? WarnLayoutBrush : DimLayoutBrush;
    }

    private void OnInputLanguageChanged(object? sender, InputLanguageEventArgs e)
    {
        UpdateLayoutIndicator();
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.CapsLock)
        {
            UpdateLayoutIndicator();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _failTimer.Stop();
        _usbPoll.Stop();
        _keyboard.Dispose();
        InputLanguageManager.Current.InputLanguageChanged -= OnInputLanguageChanged;
        base.OnClosed(e);
    }

    private void UpdateStatus(string message)
    {
        StatusText.Text = "  " + message;
    }

    private void ToggleCursor()
    {
        _cursorVisible = !_cursorVisible;
        CursorText.Visibility = _cursorVisible ? Visibility.Visible : Visibility.Hidden;
    }

    private void KeepOnTop()
    {
        if (!IsVisible)
        {
            return;
        }

        Topmost = true;

        if (!IsActive)
        {
            Activate();
        }

        if (!HiddenInput.IsKeyboardFocused)
        {
            HiddenInput.Focus();
            Keyboard.Focus(HiddenInput);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HiddenInput.Clear();
            UpdateStatus("input cleared");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control)
        {
            HiddenInput.Clear();
            UpdateStatus("input cleared");
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            TryUnlock();
            e.Handled = true;
        }
    }

    private void TryUnlock()
    {
        if (DateTime.UtcNow < _cooldownUntil)
        {
            var seconds = Math.Ceiling((_cooldownUntil - DateTime.UtcNow).TotalSeconds);
            UpdateStatus($"cooldown · {seconds:0}s");
            return;
        }

        var phrase = HiddenInput.Password;
        if (phrase.Length == 0)
        {
            UpdateStatus("empty input");
            return;
        }

        if (phrase.StartsWith(":recovery", StringComparison.OrdinalIgnoreCase))
        {
            var code = phrase.Length > ":recovery".Length
                ? phrase[":recovery".Length..].Trim()
                : "";

            if (!_config.HasRecovery)
            {
                UpdateStatus("no recovery code enrolled");
                return;
            }

            if (code.Length == 0)
            {
                UpdateStatus("usage: :recovery CODE");
                return;
            }

            if (RecoveryCode.Verify(code, _config.RecoveryHash))
            {
                Unlock("recovery");
            }
            else
            {
                Fail("recovery code rejected");
            }

            return;
        }

        if (Passphrase.Verify(
                phrase,
                _config.PassphraseHash,
                _config.PassphraseSalt,
                _config.PassphraseIterations))
        {
            Unlock("passphrase");
            return;
        }

        Fail("passphrase rejected");
    }

    private void Unlock(string method)
    {
        var hadFailures = _attempts > 0;
        var wasPending = _failAt > DateTime.UtcNow;
        _attempts = 0;
        _failTimer.Stop();
        _failAt = DateTime.MinValue;
        HiddenInput.Clear();
        Log.Write($"unlock ok ({method})");
        UpdateStatus("access granted");

        _clock.Stop();
        _cursorBlink.Stop();
        _keepOnTop.Stop();
        _usbPoll.Stop();

        if (method != "passphrase" || hadFailures || wasPending)
        {
            _notifier.Notify("TermLock", $"Unlocked with {method}", "white_check_mark", "high");
        }

        Hide();
        Unlocked?.Invoke(this, EventArgs.Empty);
    }

    private void Fail(string reason)
    {
        if (_failAt > DateTime.UtcNow)
        {
            HiddenInput.Clear();
            var pending = Math.Ceiling((_failAt - DateTime.UtcNow).TotalSeconds);
            UpdateStatus($"FATAL · {ActionLabel(_failAction)} in {pending:0}s · passphrase cancels");
            return;
        }

        _attempts++;
        Log.Write($"unlock failed ({reason}) ({_attempts}/{_config.MaxAttempts})");
        HiddenInput.Clear();
        _notifier.Notify(
            "TermLock",
            $"Failed attempt {_attempts}/{_config.MaxAttempts}",
            "warning");

        if (_attempts >= _config.MaxAttempts)
        {
            _attempts = 0;
            BeginFailAction();
        }
        else
        {
            UpdateStatus($"ACCESS DENIED · attempt {_attempts}/{_config.MaxAttempts}");
        }

        UpdateStats();
    }

    private void BeginFailAction()
    {
        _failAction = NormalizeFailAction(_config.FailAction);

        if (_failAction == "none")
        {
            _cooldownUntil = DateTime.UtcNow.AddSeconds(_config.CooldownSeconds);
            UpdateStatus($"ACCESS DENIED · too many attempts · cooldown {_config.CooldownSeconds}s");
            _notifier.Notify(
                "TermLock",
                "Too many attempts. Cooldown.",
                "warning",
                "high",
                force: true);
            return;
        }

        var seconds = Math.Clamp(_config.FailActionSeconds, 3, 300);
        _failAt = DateTime.UtcNow.AddSeconds(seconds);
        _failTimer.Start();

        UpdateStatus(
            $"FATAL · too many attempts · {ActionLabel(_failAction)} in {seconds}s · passphrase cancels");
        _notifier.Notify(
            "TermLock",
            $"Too many attempts. {ActionLabel(_failAction)} in {seconds}s.",
            "skull",
            "urgent",
            force: true);
        Log.Write($"fail action armed: {_failAction} in {seconds}s");
    }

    private void TickFailAction()
    {
        if (_failAt == DateTime.MinValue)
        {
            _failTimer.Stop();
            return;
        }

        var remaining = (_failAt - DateTime.UtcNow).TotalSeconds;
        if (remaining > 0)
        {
            UpdateStatus(
                $"FATAL · {ActionLabel(_failAction)} in {Math.Ceiling(remaining):0}s · passphrase cancels");
            return;
        }

        _failTimer.Stop();
        _failAt = DateTime.MinValue;
        ExecuteFailAction();
    }

    private void ExecuteFailAction()
    {
        var action = _failAction;
        Log.Write($"executing fail action: {action}");
        _notifier.Notify(
            "TermLock",
            $"Executing {ActionLabel(action)}.",
            "skull",
            "urgent",
            force: true);

        try
        {
            switch (action)
            {
                case "sleep":
                    Process.Start(new ProcessStartInfo(
                        "rundll32.exe",
                        "powrprof.dll,SetSuspendState 0,1,0")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    break;

                case "hibernate":
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/h")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    break;

                case "lock":
                    LockWorkStation();
                    break;

                case "shutdown":
                default:
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 0")
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Write("fail action failed: " + ex.Message);
        }
    }

    private static string NormalizeFailAction(string value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "sleep" => "sleep",
            "hibernate" => "hibernate",
            "lock" => "lock",
            "none" => "none",
            _ => "shutdown"
        };

    private static string ActionLabel(string action) => action.ToUpperInvariant();

    private void PollUsb()
    {
        if (!IsVisible || !_config.HasUsbKey)
        {
            return;
        }

        var present = UsbKey.IsPresent(_config);

        if (present && !_usbPresent)
        {
            _usbPresent = true;
            Unlock("usb");
            return;
        }

        if (present != _usbPresent)
        {
            _usbPresent = present;
            UpdateStats();
        }
    }

    [DllImport("user32.dll")]
    private static extern bool LockWorkStation();
}
