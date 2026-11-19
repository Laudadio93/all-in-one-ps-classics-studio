using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PS4Studio;

internal sealed record DiscVerificationResult(string Status, string Detail);
internal static class Ps2DiscVerifier
{
    private static readonly Lazy<Dictionary<long, Dictionary<string, List<string>>>> Database = new(() => LoadDatabase("ps2-redump.json.gz"));

    private static Dictionary<long, Dictionary<string, List<string>>> LoadDatabase(string name)
    {
        using var resource = typeof(Ps2DiscVerifier).Assembly.GetManifestResourceStream("PS4Studio.Resources." + name) ?? throw new InvalidDataException("The verification database is missing.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var json = JsonDocument.Parse(gzip);
        var sizes = new Dictionary<long, Dictionary<string, List<string>>>();
        foreach (var item in json.RootElement.GetProperty("records").EnumerateArray())
        {
            long size = item[0].GetInt64(); string hash = item[1].GetString()!; string title = item[2].GetString()!;
            if (!sizes.TryGetValue(size, out var hashes)) sizes[size] = hashes = new(StringComparer.OrdinalIgnoreCase);
            if (!hashes.TryGetValue(hash, out var titles)) hashes[hash] = titles = [];
            titles.Add(title);
        }
        return sizes;
    }

    public static async Task<DiscVerificationResult> VerifyAsync(string path, IProgress<int>? progress, CancellationToken token)
    {
        if (!Path.GetExtension(path).Equals(".iso", StringComparison.OrdinalIgnoreCase))
            return new("unsupported", "Only single-file PS2 DVD ISO images can currently be verified.");
        return await VerifyFileAsync(path, Database.Value, progress, token);
    }
    private static async Task<DiscVerificationResult> VerifyFileAsync(string path, Dictionary<long, Dictionary<string, List<string>>> database, IProgress<int>? progress, CancellationToken token)
    {
        var before = new FileInfo(path); long size = before.Length; DateTime modified = before.LastWriteTimeUtc;
        if (!database.TryGetValue(size, out var candidates))
            return new("not in database", "No supported single-file image in the bundled catalog has this size. This does not establish whether the image is good or bad.");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[1024 * 1024]; long total = 0; int lastPercent = -1;
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            hash.AppendData(buffer, 0, count); total += count;
            int percent = (int)(total * 100 / size);
            if (percent != lastPercent) { progress?.Report(percent); lastPercent = percent; }
        }
        var after = new FileInfo(path);
        if (total != size || !after.Exists || after.Length != size || after.LastWriteTimeUtc != modified)
            return new("changed", "The image changed during verification; run the check again.");
        string digest = Convert.ToHexString(hash.GetHashAndReset());
        if (candidates.TryGetValue(digest, out var titles))
            return new("verified", string.Join("; ", titles.Distinct(StringComparer.OrdinalIgnoreCase)) + " — SHA-1 matches the Redump catalog.");
        return new("no match", "The image's SHA-1 does not match a same-size entry in the catalog. It may be a different revision, modified, or damaged.");
    }
}
