using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TermLock.Core;
using TermLock.Platform;
using TermLock.UI;

namespace TermLock;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;

    public App()
    {
        // A curtain that dies silently is worse than a curtain that logs why.
        DispatcherUnhandledException += (_, args) =>
            Log.Write("dispatcher exception: " + args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Write("unhandled exception: " + args.ExceptionObject);
    }
    private TrayIcon? _tray;
    private HotkeyHost? _hotkeys;
    private MainWindow? _lockWindow;
    private AppConfig _config = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        AppPaths.Ensure();

        var args = e.Args
            .Select(a => a.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // --- little CLI surface -------------------------------------------------
        if (args.Contains("--install"))
        {
            Autostart.Enable();
            Shutdown();
            return;
        }

        if (args.Contains("--uninstall"))
        {
            Autostart.Disable();
            Shutdown();
            return;
        }

        if (args.Contains("--disable"))
        {
            Log.Write("disabled by --disable");
            Shutdown();
            return;
        }

        _config = AppConfig.Load();

        // Dev/CI: render the profile dialog to a PNG without showing it on the
        // interactive desktop. Used for README screenshots.
        if (args.Contains("--setup-shot"))
        {
            var setup = new SetupWindow(_config);
            setup.Show();

            var path = Path.Combine(AppPaths.Root, "setup-shot.png");
            var shotTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700)
            };

            shotTimer.Tick += (_, _) =>
            {
                shotTimer.Stop();
                try
                {
                    RenderWindowToFile(setup, path);
                    Log.Write("setup shot written: " + path);
                }
                catch (Exception ex)
                {
                    Log.Write("setup shot failed: " + ex);
                }
                finally
                {
                    setup.Close();
                    Shutdown();
                }
            };

            shotTimer.Start();
            return;
        }

        // Dev/CI: render the alerts dialog.
        if (args.Contains("--notifications-shot"))
        {
            var alerts = new NotificationsWindow(_config);
            alerts.Show();

            var path = Path.Combine(AppPaths.Root, "notifications-shot.png");
            var shotTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };

            shotTimer.Tick += (_, _) =>
            {
                shotTimer.Stop();
                try
                {
                    RenderWindowToFile(alerts, path);
                    Log.Write("notifications shot written: " + path);
                }
                catch (Exception ex)
                {
                    Log.Write("notifications shot failed: " + ex);
                }
                finally
                {
                    alerts.Close();
                    Shutdown();
                }
            };

            shotTimer.Start();
            return;
        }

        // Dev/CI: render the curtain and exit. This path runs before the
        // single-instance mutex, the tray icon and the hotkey, so screenshot
        // runs never block a live TermLock and never leave ghost tray icons.
        if (args.Contains("--shot"))
        {
            var window = new MainWindow(_config);

            try
            {
                window.ShowLock();
            }
            catch (Exception ex)
            {
                Log.Write("show lock failed: " + ex);
            }

            var path = Path.Combine(AppPaths.Root, "shot.png");
            var shotTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(900)
            };

            shotTimer.Tick += (_, _) =>
            {
                shotTimer.Stop();
                try
                {
                    window.RenderToFile(path);
                    Log.Write("shot written: " + path);
                }
                catch (Exception ex)
                {
                    Log.Write("shot failed: " + ex);
                }
                finally
                {
                    Shutdown();
                }
            };

            shotTimer.Start();
            return;
        }

        // Change the passphrase without starting the tray app. If TermLock is
        // already running, the tray menu is the right place: it updates the
        // in-memory config too.
        if (args.Contains("--setup"))
        {
            using var probe = new Mutex(true, @"Local\TermLock.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show(
                    "TermLock is already running.\n\nUse the tray icon: right-click -> Set profile...",
                    "TermLock",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                Shutdown();
                return;
            }

            ShowSetup();
            probe.ReleaseMutex();
            Shutdown();
            return;
        }

        // --- one TermLock per user session --------------------------------------
        _mutex = new Mutex(true, @"Local\TermLock.SingleInstance", out var isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        Log.Write($"start v0.1 user={Environment.UserName}");

        // The lock window is created once and reused. It stays hidden until the
        // hotkey (or --lock) asks for it, so startup does not flash a black screen.
        _lockWindow = new MainWindow(_config);
        _lockWindow.Unlocked += (_, _) => Log.Write("unlocked");

        _tray = new TrayIcon(
            lockAction: () => _lockWindow.ShowLock(),
            exitAction: Shutdown,
            openConfig: () => Process.Start("explorer.exe", AppPaths.Root),
            setPassphrase: () => ShowSetup(),
            createUsbKey: ShowUsbKey,
            notifications: ShowNotifications);

        // Global hotkey: Ctrl+Alt+Shift+L
        _hotkeys = new HotkeyHost();
        _hotkeys.Pressed += (_, _) => _lockWindow.ShowLock();
        if (!_hotkeys.IsRegistered)
        {
            Log.Write("hotkey registration failed (already in use?)");
        }

        // First run: ask for a profile and passphrase before anything else.
        if (!_config.HasPassphrase)
        {
            if (!ShowSetup())
            {
                Shutdown();
                return;
            }
        }

        if (args.Contains("--lock"))
        {
            try
            {
                _lockWindow.ShowLock();
            }
            catch (Exception ex)
            {
                Log.Write("show lock failed: " + ex);
            }
        }

        _tray.ShowBalloon("TermLock", "Running. Ctrl+Alt+Shift+L locks the screen.");
    }

    private bool ShowSetup()
    {
        var setup = new SetupWindow(_config);
        if (setup.ShowDialog() != true)
        {
            return false;
        }

        _config.Save();
        Log.Write("passphrase saved");

        if (!_config.HasRecovery)
        {
            var code = RecoveryCode.Generate();
            var recovery = new RecoveryWindow(code);

            if (recovery.ShowDialog() == true)
            {
                _config.RecoveryHash = RecoveryCode.Hash(code);
                _config.Save();
                Log.Write("recovery code created");
            }
            else
            {
                Log.Write("recovery code skipped");
            }
        }

        return true;
    }

    private void ShowUsbKey()
    {
        var window = new UsbKeyWindow(_config);
        window.ShowDialog();
    }

    private void ShowNotifications()
    {
        var window = new NotificationsWindow(_config);
        window.ShowDialog();
    }

    private static void RenderWindowToFile(Window window, string path)
    {
        window.UpdateLayout();

        var width = (int)Math.Max(1, window.ActualWidth);
        var height = (int)Math.Max(1, window.ActualHeight);

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();

        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
            // not the owner - nothing to release
        }

        _mutex?.Dispose();
        base.OnExit(e);
    }
}
