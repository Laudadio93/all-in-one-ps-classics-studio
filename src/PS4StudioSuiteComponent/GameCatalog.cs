using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace PS4Studio;
internal static class GameCatalog
{
    private static string NormalizeId(string value)
    {
        var match=Regex.Match(value,@"(?i)([A-Z]{4})[-_. ]?(\d{3})[. -]?(\d{2})");
        return match.Success?(match.Groups[1].Value+match.Groups[2].Value+match.Groups[3].Value).ToUpperInvariant():"";
    }
    private static string TitleKey(string value)
    {
        value=Regex.Replace(value,@"\s*[\(\[].*?[\)\]]","");
        return Regex.Replace(value.ToLowerInvariant(),@"[^a-z0-9]","");
    }
    public static string Identify(string path,bool ps1,string toolkit)
    {
        try
        {
            using var file=File.OpenRead(path);
            if(!ps1){try{return DiscMetadata.ReadSerial(file);}catch(InvalidDataException){}}
            else
            {
                byte[] data=new byte[Math.Min(8*1024*1024,(int)Math.Min(file.Length,int.MaxValue))];
                file.Position=0;file.ReadExactly(data);
                var directMatch=Regex.Match(Encoding.ASCII.GetString(data),@"(?i)\b[A-Z]{4}[_-]\d{3}\.\d{2}\b");
                string direct=directMatch.Success?NormalizeId(directMatch.Value):"";
                if(direct.Length>0)return direct;
            }
            string inName=NormalizeId(Path.GetFileNameWithoutExtension(path));
            if(inName.Length>0)return inName;
            toolkit=BuiltInKit.Resolve(toolkit);
            string database=Path.Combine(toolkit,"Tools",ps1?"ps1ids.txt":"ps2ids.txt");
            if(!File.Exists(database))return "";
            string key=TitleKey(Path.GetFileNameWithoutExtension(path));
            if(key.Length==0)return "";
            var matches=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string line in File.ReadLines(database))
            {
                int split=line.IndexOf(';');if(split<=0)continue;
                if(TitleKey(line[(split+1)..])!=key)continue;
                string id=NormalizeId(line[..split]);if(id.Length>0)matches.Add(id);
                if(matches.Count>1)return "";
            }
            return matches.Count==1?matches.First():"";
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException){return "";}
    }
}

