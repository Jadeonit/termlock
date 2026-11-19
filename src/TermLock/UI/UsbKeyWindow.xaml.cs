using System.IO;
using System.Windows;
using System.Windows.Input;
using TermLock.Core;

namespace TermLock.UI;

public partial class UsbKeyWindow : Window
{
    private readonly AppConfig _config;

    public UsbKeyWindow(AppConfig config)
    {
        InitializeComponent();
        _config = config;
        RefreshDrives();
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void RefreshDrives()
    {
        DrivesList.Items.Clear();

        var drives = UsbKey.RemovableDrives();
        foreach (var drive in drives)
        {
            DrivesList.Items.Add(new DriveItem(
                drive,
                $"{drive.Name}  {drive.DriveFormat}  {FormatGb(drive.TotalSize)}"));
        }

        if (drives.Count == 0)
        {
            StatusText.Text = "no removable drives found — insert a USB stick and press refresh";
        }
        else
        {
            DrivesList.SelectedIndex = 0;
            StatusText.Text = "select a drive, then create the key";
        }
    }

    private void OnRefresh(object sender, RoutedEventArgs e)
    {
        RefreshDrives();
    }

    private void OnCreate(object sender, RoutedEventArgs e)
    {
        if (DrivesList.SelectedItem is not DriveItem item)
        {
            StatusText.Text = "select a removable drive first";
            return;
        }

        try
        {
            var (hash, serial) = UsbKey.Create(item.Drive.RootDirectory.FullName);
            _config.UsbTokenHash = hash;
            _config.UsbVolumeSerial = serial;
            _config.Save();
            StatusText.Text = $"key written to {item.Drive.Name}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "failed: " + ex.Message;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string FormatGb(long bytes) => $"{bytes / 1073741824.0:0.0} GB";

    public sealed record DriveItem(DriveInfo Drive, string Display);
}
