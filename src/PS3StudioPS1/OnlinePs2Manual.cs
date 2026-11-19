using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PS2ClassicsPackageStudio;

internal static class OnlinePs2Manual
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(90) };
    private static readonly SemaphoreSlim DownloadGate = new(1, 1);
    private static readonly Lazy<Dictionary<string, string>> Index = new(() => {
        using var input = typeof(OnlinePs2Manual).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.ps2-manual-index.json") ?? throw new InvalidDataException("Online manual index is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(input) ?? new();
    });

    public static async Task<string?> FindAsync(string iso, bool allowDownload = true)
    {
        using var file = File.OpenRead(iso);
        string serial = DiscMetadata.ReadSerial(file);
        if (!Index.Value.TryGetValue(serial, out string? name)) return null;
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PS1 and PS2 Classics Package Studio", "Manual Cache");
        Directory.CreateDirectory(folder);
        string cached = Path.Combine(folder, serial + ".zip");
        await DownloadGate.WaitAsync();
        try
        {
            if (File.Exists(cached))
            {
                if (ManualValidation.Check(cached, false) is null) return cached;
                File.Delete(cached);
            }
            if (!allowDownload) return null;
            string tempPdf = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".pdf");
            string tempZip = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                string url = "https://archive.org/download/kirklands-manual-labor-sony-playstation-2-usa-4k-version/" + Uri.EscapeDataString(name);
                using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                const long max = 128L * 1024 * 1024;
                if (response.Content.Headers.ContentLength is long length && (length <= 0 || length > max)) throw new InvalidDataException("Online manual exceeds the download limit.");
                using (var input = await response.Content.ReadAsStreamAsync())
                using (var output = File.Create(tempPdf))
                {
                    byte[] buffer = new byte[65536]; long total = 0; int count;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
                    {
                        total += count;
                        if (total > max) throw new InvalidDataException("Online manual exceeds the download limit.");
                        await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                    }
                }
                PdfManual.ConvertToArchive(tempPdf, tempZip);
                if (ManualValidation.Check(tempZip, false) is { } problem) throw new InvalidDataException(problem);
                File.Move(tempZip, cached, true);
                return cached;
            }
            finally { if (File.Exists(tempPdf)) File.Delete(tempPdf); if (File.Exists(tempZip)) File.Delete(tempZip); }
        }
        finally { DownloadGate.Release(); }
    }
}
