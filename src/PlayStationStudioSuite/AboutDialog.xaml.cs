using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace ClassicsSuite;
public partial class AboutDialog : Window
{
    public AboutDialog(Window owner){InitializeComponent();Owner=owner;}
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);int enabled=1;
        DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref enabled,sizeof(int));
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    void CloseDialog(object s,RoutedEventArgs e)=>Close();
    static Stream Resource(string name)=>typeof(AboutDialog).Assembly.GetManifestResourceStream("ClassicsSuite.Resources."+name)??throw new IOException("The included notices are missing.");
    void ViewNotices(object s,RoutedEventArgs e)
    {
        try
        {
            var text=new StringBuilder("PS3 package studio and PS2 Memory Card Studio — included third-party notices\n\n");
            using(var stream=Resource("ThirdParty.zip"))using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))
                foreach(var entry in zip.Entries.Where(x=>x.FullName.EndsWith(".txt",StringComparison.OrdinalIgnoreCase)||x.FullName.EndsWith("/LICENSE",StringComparison.OrdinalIgnoreCase)))
                {using var reader=new StreamReader(entry.Open());text.Append("\n\n=== ").Append(entry.FullName).Append(" ===\n\n").Append(reader.ReadToEnd());}
            foreach(string name in new[]{"Memory-NOTICES.txt","Memory-LICENSE.txt"}){using var stream=Resource(name);using var reader=new StreamReader(stream);text.Append("\n\n=== ").Append(name).Append(" ===\n\n").Append(reader.ReadToEnd());}
            var viewer=new Window{Title="Third-party notices",Owner=this,Width=700,Height=650,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#141F30"))};
            viewer.SourceInitialized+=(_,_)=>{int enabled=1;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(viewer).Handle,20,ref enabled,sizeof(int));};
            var dock=new DockPanel{Margin=new Thickness(20)};var close=new System.Windows.Controls.Button{Content="Close",HorizontalAlignment=System.Windows.HorizontalAlignment.Right,Margin=new Thickness(0,12,0,0),MinWidth=100};close.Click+=(_,_)=>viewer.Close();DockPanel.SetDock(close,Dock.Bottom);dock.Children.Add(close);
            dock.Children.Add(new System.Windows.Controls.TextBox{Text=text.ToString(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#0C1725")),Foreground=System.Windows.Media.Brushes.White,BorderThickness=new Thickness(0),Padding=new Thickness(12)});viewer.Content=dock;viewer.ShowDialog();
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException){System.Windows.MessageBox.Show(this,ex.Message,"Notices unavailable");}
    }
    void SaveNotices(object s,RoutedEventArgs e)
    {
        var d=new Microsoft.Win32.SaveFileDialog{Title="Save PS3 studio source and notices",FileName="PS3-Studio-ThirdParty.zip",Filter="ZIP archive (*.zip)|*.zip"};
        if(d.ShowDialog(this)!=true)return;
        try{using var input=Resource("ThirdParty.zip");using var output=new FileStream(d.FileName,FileMode.Create,FileAccess.Write,FileShare.None);input.CopyTo(output);}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException){System.Windows.MessageBox.Show(this,ex.Message,"Could not save archive");}
    }
}
