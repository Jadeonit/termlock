using System.Drawing;
using System.Windows.Forms;
using TermLock.Core;

namespace TermLock.Platform;

/// <summary>
/// A tray icon is the escape hatch: if the hotkey is taken by another app,
/// the user can still lock the screen, open the config folder, or quit.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action lockAction, Action exitAction, Action openConfig)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Lock now", null, (_, _) => lockAction());
        menu.Items.Add("Open config folder", null, (_, _) => openConfig());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exitAction());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "TermLock - privacy curtain",
            ContextMenuStrip = menu
        };

        _icon.DoubleClick += (_, _) => lockAction();
        Log.Write("tray icon ready");
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.ShowBalloonTip(3000, title, text, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
