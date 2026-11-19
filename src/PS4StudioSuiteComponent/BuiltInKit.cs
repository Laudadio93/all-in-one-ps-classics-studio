using System.IO;
using System.IO.Compression;

namespace PS4Studio;
internal static class BuiltInKit
{
    public const string Name = "Built-in PS4 engine";
    private static readonly Lazy<string> Folder = new(() => {
        string dir = Path.Combine(Path.GetTempPath(), "PS4ClassicsStudio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using var archive = typeof(BuiltInKit).Assembly.GetManifestResourceStream("PS4Studio.Resources.ps4-engine.zip") ?? throw new FileNotFoundException("Built-in PS4 engine is missing.");
            ZipFile.ExtractToDirectory(archive, dir);
            return dir;
        }
        catch { try { Directory.Delete(dir, true); } catch (IOException) { } throw; }
    });
    public static string Resolve(string value) => value == Name ? Folder.Value : value;
    public static void Cleanup() { if (!Folder.IsValueCreated) return; try { Directory.Delete(Folder.Value, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
}
