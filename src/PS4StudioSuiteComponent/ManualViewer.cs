using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PS4Studio;
internal static class ManualViewer
{
    public static void Show(Window owner,string path)
    {
        using var archive=ZipFile.OpenRead(path);
        var pages=archive.Entries.Where(e=>!string.IsNullOrEmpty(e.Name)&&new[]{".png",".jpg",".jpeg",".bmp"}.Contains(Path.GetExtension(e.Name),StringComparer.OrdinalIgnoreCase)).OrderBy(e=>System.Text.RegularExpressions.Regex.Replace(e.FullName,@"\d+",m=>m.Value.PadLeft(12,'0')),StringComparer.OrdinalIgnoreCase).ToArray();
        if(pages.Length==0)throw new InvalidDataException("Manual has no image pages.");
        var window=new Window{Title="Manual preview · PC only",Owner=owner,Width=800,Height=720,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=new SolidColorBrush(Color.FromRgb(20,29,43)),Foreground=Brushes.White};
        var root=new DockPanel{Margin=new Thickness(12)};window.Content=root;var bar=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center};DockPanel.SetDock(bar,Dock.Bottom);root.Children.Add(bar);
        var image=new Image{Stretch=Stretch.Uniform};root.Children.Add(new ScrollViewer{Content=image,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var previous=new Button{Content="Previous",Margin=new Thickness(4),Padding=new Thickness(12,6,12,6)};var label=new TextBlock{Margin=new Thickness(12),VerticalAlignment=VerticalAlignment.Center};var next=new Button{Content="Next",Margin=new Thickness(4),Padding=new Thickness(12,6,12,6)};bar.Children.Add(previous);bar.Children.Add(label);bar.Children.Add(next);int current=0;
        void Render(){using var stream=pages[current].Open();using var buffer=new MemoryStream();stream.CopyTo(buffer);buffer.Position=0;var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.StreamSource=buffer;bitmap.EndInit();bitmap.Freeze();image.Source=bitmap;label.Text=$"Page {current+1} of {pages.Length}";previous.IsEnabled=current>0;next.IsEnabled=current<pages.Length-1;}
        previous.Click+=(_,_)=>{current--;Render();};next.Click+=(_,_)=>{current++;Render();};Render();window.ShowDialog();
    }
}
