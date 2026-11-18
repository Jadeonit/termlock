using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using TermLock.Core;
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
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _cursorBlink;
    private readonly DispatcherTimer _keepOnTop;

    private int _attempts;
    private DateTime _cooldownUntil = DateTime.MinValue;
    private bool _cursorVisible = true;
    private bool _allowClose;
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

        _clock = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => UpdateHeader(),
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

        HiddenInput.PasswordChanged += (_, _) => UpdatePrompt();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        InputLanguageManager.Current.InputLanguageChanged += OnInputLanguageChanged;
        Activated += (_, _) => UpdateLayoutIndicator();

        BuildBanner();
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
        UpdateStatus("awaiting passphrase");
        UpdateLayoutIndicator();

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

        StatsText.Text = $"""
SYSTEM
  user       {_config.UserName}
  os         {SystemInfo.WindowsProduct()}
  uptime     {uptime.Days}d {uptime.Hours:00}h {uptime.Minutes:00}m
  cpu        [ -- ]  day 6
  ram        [ -- ]  day 6
  disk       [ -- ]  day 6

SECURITY
  defender   ....... day 6
  firewall   ....... day 6
  secureboot ....... day 6
  curtain    ....... ARMED

AUTH
  method     passphrase / PBKDF2-SHA256
  layout     {_layoutCode}
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

        if (Passphrase.Verify(
                phrase,
                _config.PassphraseHash,
                _config.PassphraseSalt,
                _config.PassphraseIterations))
        {
            _attempts = 0;
            HiddenInput.Clear();
            Log.Write("unlock ok");
            UpdateStatus("access granted");

            _clock.Stop();
            _cursorBlink.Stop();
            _keepOnTop.Stop();
            Hide();

            Unlocked?.Invoke(this, EventArgs.Empty);
            return;
        }

        _attempts++;
        Log.Write($"unlock failed ({_attempts}/{_config.MaxAttempts})");
        HiddenInput.Clear();

        if (_attempts >= _config.MaxAttempts)
        {
            _attempts = 0;
            _cooldownUntil = DateTime.UtcNow.AddSeconds(_config.CooldownSeconds);
            UpdateStatus(
                $"ACCESS DENIED · too many attempts · cooldown {_config.CooldownSeconds}s");
        }
        else
        {
            UpdateStatus($"ACCESS DENIED · attempt {_attempts}/{_config.MaxAttempts}");
        }

        UpdateStats();
    }
}
