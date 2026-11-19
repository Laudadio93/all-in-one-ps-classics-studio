using System.Windows;
using System.Windows.Input;

namespace PS2ClassicsPackageStudio;

public partial class StudioDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;
    private readonly MessageBoxButton _buttons;

    private StudioDialog(Window owner, string message, string title, MessageBoxButton buttons)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        _buttons = buttons;
        if (buttons == MessageBoxButton.YesNo)
        {
            OkButton.Visibility = Visibility.Collapsed;
            NoButton.Visibility = YesButton.Visibility = Visibility.Visible;
            NoButton.IsDefault = true;
        }
        else
        {
            OkButton.IsDefault = true;
        }
    }

    public static MessageBoxResult Show(Window owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new StudioDialog(owner, message, title, buttons);
        dialog.ShowDialog();
        return dialog._result;
    }

    private void No_Click(object sender, RoutedEventArgs e) { _result = MessageBoxResult.No; Close(); }
    private void Yes_Click(object sender, RoutedEventArgs e) { _result = MessageBoxResult.Yes; Close(); }
    private void Ok_Click(object sender, RoutedEventArgs e) { _result = MessageBoxResult.OK; Close(); }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Dismiss();
    }

    private void Dismiss()
    {
        _result = _buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        Close();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
