using System.IO;
using System.Text.RegularExpressions;
namespace PS4Studio;
internal static class Ps1Protection
{
    public static string? Find(string kit,string id)
    {
        if(!Regex.IsMatch(id,@"^[A-Z]{4}\d{5}$"))return null;
        string path=Path.Combine(BuiltInKit.Resolve(kit),"Tools","libcrypt.txt"); if(!File.Exists(path))return null;
        string serial=id[..4]+"_"+id.Substring(4,3)+"."+id[7..];
        foreach(string line in File.ReadLines(path)) { var m=Regex.Match(line,@"^([A-Z]{4}_\d{3}\.\d{2})\s+([A-Fa-f0-9]{4})\s*$"); if(m.Success&&m.Groups[1].Value.Equals(serial,StringComparison.OrdinalIgnoreCase))return m.Groups[2].Value.ToUpperInvariant(); }
        return null;
    }
}
