using System.IO;
using System.Text.Json;

namespace PS2ClassicsPackageStudio;
internal static class Ps1Protection
{
    private static readonly Lazy<HashSet<string>> Supported = new(() => {
        using var input = typeof(Ps1Protection).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.ps1-libcrypt.json") ?? throw new InvalidDataException("PS1 protection index missing.");
        return new HashSet<string>(JsonSerializer.Deserialize<string[]>(input) ?? [], StringComparer.OrdinalIgnoreCase);
    });
    public static (string? Serial, bool Recognized, bool SbiPresent) Check(string path)
    {
        string? serial = DiscSystemDetector.ReadPs1Serial(path);
        return (serial, serial is not null && Supported.Value.Contains(serial), File.Exists(Path.ChangeExtension(path, ".sbi")));
    }
}
