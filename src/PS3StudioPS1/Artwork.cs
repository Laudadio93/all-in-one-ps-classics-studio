using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace PS2ClassicsPackageStudio;
internal static class Artwork
{
    public static void Write(string input,string output,int width,int height,bool fit=false)
    {
        using var stream=File.OpenRead(input);
        var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);
        var source=decoder.Frames[0];
        var visual=new DrawingVisual();
        using(var dc=visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, width, height));
            double scale = Math.Min((double)width / source.PixelWidth, (double)height / source.PixelHeight);
            double w = fit ? source.PixelWidth * scale : width, h = fit ? source.PixelHeight * scale : height;
            dc.DrawImage(source,new Rect((width-w)/2,(height-h)/2,w,h));
        }
        var render=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        render.Render(visual);
        var rgb=new FormatConvertedBitmap(render,PixelFormats.Bgr24,null,0);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(rgb));
        using var file=new FileStream(output,FileMode.Create,FileAccess.Write,FileShare.None);
        encoder.Save(file);
    }
}
