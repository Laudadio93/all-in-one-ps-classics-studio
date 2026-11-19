using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PS2ClassicsPackageStudio;
internal static class Ps1DiscVerifier
{
    private sealed record Entry(string Name, string[] Hashes);
    private static readonly Lazy<Dictionary<string, List<Entry>>> Database = new(() => {
        using var source = typeof(Ps1DiscVerifier).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.ps1-redump-multitrack.json.gz") ?? throw new InvalidDataException("The PS1 verification database is missing.");
        using var gzip = new GZipStream(source, CompressionMode.Decompress); using var json = JsonDocument.Parse(gzip);
        var index = new Dictionary<string, List<Entry>>();
        foreach (var row in json.RootElement.GetProperty("records").EnumerateArray())
        {
            var tracks = row[1].EnumerateArray().ToArray(); string key = string.Join(",", tracks.Select(t => t[0].GetInt64()));
            if (!index.TryGetValue(key, out var entries)) index[key] = entries = [];
            entries.Add(new Entry(row[0].GetString()!, tracks.Select(t => t[1].GetString()!).ToArray()));
        }
        return index;
    });
    public static async Task<DiscVerificationResult> VerifyAsync(string cue, IProgress<int>? progress, CancellationToken token)
    {
        if (!Path.GetExtension(cue).Equals(".cue", StringComparison.OrdinalIgnoreCase)) return new("unsupported", "PS1 verification requires a CUE file and its BIN tracks.");
        if (CueValidation.Check(cue) is { } error) return new("unsupported", error);
        var cueInfo = new FileInfo(cue); var cueTime = cueInfo.LastWriteTimeUtc; long cueSize = cueInfo.Length;
        var files = new List<string>(); bool track = false, index = false;
        foreach (string line in File.ReadLines(cue))
        {
            var file = Regex.Match(line, "^[ \\t]*FILE[ \\t]+(?:\"([^\"\\r\\n]+)\"|(\\S+))[ \\t]+BINARY[ \\t]*$", RegexOptions.IgnoreCase);
            if (file.Success)
            {
                if (files.Count > 0 && (!track || !index)) return new("unsupported", "Each PS1 BIN must contain exactly one track starting at 00:00:00.");
                string name = file.Groups[1].Success ? file.Groups[1].Value : file.Groups[2].Value;
                string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(cue))!, name));
                if (!Path.GetExtension(path).Equals(".bin", StringComparison.OrdinalIgnoreCase) || files.Contains(path, StringComparer.OrdinalIgnoreCase)) return new("unsupported", "Each track must use a separate BIN file.");
                files.Add(path); track = false; index = false; continue;
            }
            if (Regex.IsMatch(line, @"^[ \t]*TRACK[ \t]+", RegexOptions.IgnoreCase)) { if (files.Count == 0 || track) return new("unsupported", "Each PS1 BIN must contain exactly one track."); track = true; continue; }
            if (Regex.IsMatch(line, @"^[ \t]*(PREGAP|POSTGAP)[ \t]+", RegexOptions.IgnoreCase)) return new("unsupported", "CUE gap directives are not yet supported by verification.");
            if (Regex.IsMatch(line, @"^[ \t]*INDEX[ \t]+", RegexOptions.IgnoreCase)) { if (!track || index || !Regex.IsMatch(line, @"^[ \t]*INDEX[ \t]+01[ \t]+00:00:00[ \t]*$", RegexOptions.IgnoreCase)) return new("unsupported", "Each track must have exactly one INDEX 01 at 00:00:00."); index = true; }
        }
        if (files.Count == 0 || !track || !index) return new("unsupported", "The CUE track layout is not supported.");
        var before = files.Select(p => new FileInfo(p)).ToArray();
        long[] sizes = before.Select(x => x.Length).ToArray(); DateTime[] times = before.Select(x => x.LastWriteTimeUtc).ToArray();
        string key = string.Join(",", sizes);
        if (!Database.Value.TryGetValue(key, out var candidates)) return new("not in database", "No supported Redump disc has this track count and set of sizes. This is inconclusive.");
        long total = sizes.Sum(), completed = 0; int last = -1; var hashes = new List<string>();
        foreach (string path in files)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[1024 * 1024]; int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0) { hash.AppendData(buffer, 0, count); completed += count; int percent = (int)(completed * 100 / total); if (percent != last) { progress?.Report(percent); last = percent; } }
            hashes.Add(Convert.ToHexString(hash.GetHashAndReset()));
        }
        var cueAfter = new FileInfo(cue);
        if (!cueAfter.Exists || cueAfter.Length != cueSize || cueAfter.LastWriteTimeUtc != cueTime) return new("changed", "The CUE changed during verification; run the check again.");
        for (int i = 0; i < files.Count; i++) { var after = new FileInfo(files[i]); if (!after.Exists || after.Length != sizes[i] || after.LastWriteTimeUtc != times[i]) return new("changed", "A track changed during verification; run the check again."); }
        var matches = candidates.Where(e => e.Hashes.SequenceEqual(hashes, StringComparer.OrdinalIgnoreCase)).Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return matches.Length > 0 ? new("verified", string.Join("; ", matches) + $" — all {files.Count} track SHA-1 hashes match the same Redump disc.") : new("no match", "The track hashes do not match the same cataloged disc. The dump may be modified, a different revision, absent from the snapshot, or damaged.");
    }
}
