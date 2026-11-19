using System.IO;
namespace PS4Studio;
internal static class Ps2ProfileIndex
{
    private static readonly Lazy<HashSet<string>> Ids=new(()=>{using var file=typeof(Ps2ProfileIndex).Assembly.GetManifestResourceStream("PS4Studio.Resources.ps2-profile-index.txt")??throw new InvalidDataException("The PS2 profile index is missing.");using var text=new StreamReader(file);var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);string? line;while((line=text.ReadLine()) is not null)if(line.Length>0)ids.Add(line);return ids;});
    public static bool Contains(string id)=>Ids.Value.Contains(id);
}
