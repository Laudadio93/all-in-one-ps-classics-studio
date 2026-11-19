using System.IO;
using System.Text;
using System.Windows;

namespace PS4Studio;

public partial class AboutDialog : Window
{
    public AboutDialog(Window owner)
    {
        InitializeComponent();
        Owner = owner;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Source_Click(object sender, RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.SaveFileDialog { Title="Save PS2 Classics tool source", FileName="ps2classic-source.7z", Filter="7-Zip archive (*.7z)|*.7z" };
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            using var source=typeof(AboutDialog).Assembly.GetManifestResourceStream("PS4Studio.Resources.ps2classic-source.7z") ?? throw new FileNotFoundException("The source archive is missing.");
            using var destination=new FileStream(dialog.FileName,FileMode.Create,FileAccess.Write,FileShare.None);source.CopyTo(destination);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){new ThemeDialog(this,"Could not save the source archive: "+ex.Message,"Save source").ShowDialog();}
    }

    private void Licenses_Click(object sender, RoutedEventArgs e)
    {
        var notices = new (string title, string resource)[]
        {
            ("PDFium distribution package notice", "LICENSE"),
            ("PS2 Classics tools — GNU GPL version 3", "PS2CLASSIC-GPL3.txt"),
            ("xdelta3 â€” Apache License 2.0", "XDELTA-LICENSE.txt"),
            ("7-Zip â€” distribution and component notices", "7ZIP-LICENSE.txt"),
            ("7-Zip â€” GNU Lesser General Public License 2.1", "7ZIP-LGPL.txt"),
            ("7-Zip â€” unRAR restriction", "7ZIP-UNRAR-LICENSE.txt")
        };
        var text = new StringBuilder("Third-party notices\n\nThe complete included notices are reproduced below. Scroll to read each section.\n");
        foreach (var (title, resource) in notices)
        {
            using var stream = typeof(AboutDialog).Assembly.GetManifestResourceStream("PS4Studio.Resources." + resource);
            if (stream is null) { text.Append("\n\n").Append(title).Append("\nNotice unavailable.\n"); continue; }
            using var reader = new StreamReader(stream);
            text.Append("\n\nâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€\n").Append(title).Append("\nâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€\n\n").Append(reader.ReadToEnd());
        }
        new ThemeDialog(this, text.ToString(), "Third-party licenses").ShowDialog();
    }
}
