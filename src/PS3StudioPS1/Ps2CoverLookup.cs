using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace PS2ClassicsPackageStudio;

internal static class Ps2CoverLookup
{
    private const int MaxBytes = 8 * 1024 * 1024;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<string?> DownloadAsync(string serial, string job, Action<string> log, CancellationToken token, bool ps1 = false, bool threeDimensional = false)
    {
        if (!Regex.IsMatch(serial, @"^[A-Z]{4}\d{5}$", RegexOptions.CultureInvariant))
        {
            log("No recognizable disc ID for cover lookup; using the default icon.");
            return null;
        }
        string id = serial[..4] + "-" + serial[4..];
        string collection = ps1 ? "psx-covers" : "ps2-covers";
        string style = threeDimensional ? "3d" : "default", ext = threeDimensional ? ".png" : ".jpg";
        var url = $"https://raw.githubusercontent.com/xlenore/{collection}/main/covers/{style}/{id}{ext}";
        try
        {
            string path = Path.Combine(job, (ps1 ? "ps1-cover" : "ps2-cover") + ext);
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS2ClassicsPackageStudio", "cover-cache", collection, style);
            string cached = Path.Combine(cacheDir, id + ext);
            if (!threeDimensional && !File.Exists(cached)) { string legacy = Path.Combine(Directory.GetParent(cacheDir)!.FullName, id + ext); if (File.Exists(legacy)) cached = legacy; }
            if (File.Exists(cached) && new FileInfo(cached).Length is > 7 and <= MaxBytes && IsImage(cached, threeDimensional))
            {
                File.Copy(cached, path, true);
                log($"Using cached cover for {id} (offline).");
                return path;
            }
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                log($"No cover found for {id}; using the default icon.");
                return null;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxBytes) throw new IOException("Cover image is too large.");
            await using var input = await response.Content.ReadAsStreamAsync(token);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[32768];
            int count;
            while ((count = await input.ReadAsync(chunk, token)) > 0)
            {
                if (buffer.Length + count > MaxBytes) throw new IOException("Cover image is too large.");
                buffer.Write(chunk, 0, count);
            }
            if (buffer.Length < 8 || !(threeDimensional ? buffer.GetBuffer().AsSpan(0, 8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) : buffer.GetBuffer()[0] == 0xff && buffer.GetBuffer()[1] == 0xd8 && buffer.GetBuffer()[2] == 0xff)) throw new IOException("The downloaded cover has an invalid image format.");
            await File.WriteAllBytesAsync(path, buffer.ToArray(), token);
            try { Directory.CreateDirectory(cacheDir); File.Copy(path, cached, true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { log("Cover could not be saved for offline use."); }
            return path;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { log("Cover lookup timed out; using the default artwork."); return null; }
        catch (Exception e) when (e is HttpRequestException or IOException or UnauthorizedAccessException) { log($"Cover lookup unavailable: {e.Message}. Using the default artwork."); return null; }
    }
    private static bool IsImage(string path, bool png)
    {
        using var file = File.OpenRead(path);
        byte[] bytes = new byte[8];
        return file.Read(bytes) == 8 && (png ? bytes.AsSpan().SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}) : bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff);
    }
}
