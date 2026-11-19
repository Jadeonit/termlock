using System.Windows;
using System.Windows.Input;

namespace TermLock.UI;

public partial class RecoveryWindow : Window
{
    public RecoveryWindow(string code)
    {
        InitializeComponent();
        CodeBox.Text = code;
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(CodeBox.Text);
            StatusText.Text = "copied to clipboard";
        }
        catch (Exception ex)
        {
            StatusText.Text = "copy failed: " + ex.Message;
        }
    }

    private void OnSaved(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
