using System.IO;
using System.Text.RegularExpressions;

namespace PS4Studio;

internal static class Ps2ManualLibrary
{
    public static string? Find(string isoPath, string folder)
    {
        string baseName = Path.Combine(Path.GetDirectoryName(isoPath)!, Path.GetFileNameWithoutExtension(isoPath));
        var nearby = new[] { baseName + ".manual.zip", baseName + ".manual.cbz" }.Where(File.Exists).ToArray();
        if (nearby.Length == 1) return nearby[0];
        if (nearby.Length > 1) throw new InvalidDataException("More than one nearby manual matches this game. Choose the manual explicitly.");
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
        using var iso = File.OpenRead(isoPath);
        string serial = DiscMetadata.ReadSerial(iso);
        var candidates = Directory.EnumerateFiles(folder).Where(path =>
            new[] { ".zip", ".cbz" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
            Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9]", "").Equals(serial, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (candidates.Length > 1) throw new InvalidDataException($"More than one manual matches {serial}. Keep one match or choose a manual explicitly.");
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
