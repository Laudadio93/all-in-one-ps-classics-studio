using System.Windows;
namespace PS4Studio;
public partial class ThemeDialog : Window
{
    public ThemeDialog(Window owner, string message, string title)
    {
        InitializeComponent();
        Owner = owner;
        Title = title;
        Heading.Text = title;
        Message.Text = message;
        Loaded += (_, _) => Confirm.Focus();
    }
    private void Confirm_Click(object sender, RoutedEventArgs e) => Close();
}
