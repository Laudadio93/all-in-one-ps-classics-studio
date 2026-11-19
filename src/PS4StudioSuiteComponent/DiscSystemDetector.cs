using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace PS4Studio;
internal static class DiscSystemDetector
{
    public static string? ReadPs1Serial(string path)
    {
        try
        {
            string image = ResolveImage(path);
            using var file = File.OpenRead(image);
            int size = (int)Math.Min(file.Length, 16L * 1024 * 1024);
            byte[] data = new byte[size];
            file.ReadExactly(data);
            var match = Regex.Match(Encoding.ASCII.GetString(data), @"(?i)\bBOOT\s*=\s*cdrom0?:\\([A-Z]{4})[_-](\d{3})\.(\d{2})");
            return match.Success ? (match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value).ToUpperInvariant() : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }
    private static string ResolveImage(string path)
    {
        string image = Path.GetFullPath(path);
        if (!Path.GetExtension(image).Equals(".cue", StringComparison.OrdinalIgnoreCase)) return image;
        string cue = File.ReadAllText(image);
        var match = Regex.Match(cue, @"(?im)^\s*FILE\s+(?:""([^""\r\n]+)""|(\S+))\s+BINARY\s*$");
        if (!match.Success) throw new InvalidDataException("CUE file has no binary image.");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(image)!, match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value));
    }
    public static int? Detect(string path)
    {
        try {
            string image=Path.GetFullPath(path);
            if(Path.GetExtension(image).Equals(".cue",StringComparison.OrdinalIgnoreCase)){
                string cue=File.ReadAllText(image);
                var match=Regex.Match(cue,@"(?im)^\s*FILE\s+(?:""([^""\r\n]+)""|(\S+))\s+BINARY\s*$");
                if(!match.Success) return null;
                string refName=match.Groups[1].Success?match.Groups[1].Value:match.Groups[2].Value;
                image=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(image)!,refName));
            }
            if(!File.Exists(image)) return null;
            using var file=File.OpenRead(image); int size=(int)Math.Min(file.Length,16L*1024*1024);
            byte[] data=new byte[size];file.ReadExactly(data);
            string text=Encoding.ASCII.GetString(data);
            if(Regex.IsMatch(text,@"(?i)\bBOOT2\s*=\s*cdrom0?:\\[A-Z]{4}[_-]\d{3}\.\d{2}"))return 1;
            if(Regex.IsMatch(text,@"(?i)\bBOOT\s*=\s*cdrom0?:\\[A-Z]{4}[_-]\d{3}\.\d{2}"))return 0;
        } catch(Exception e) when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }
}
