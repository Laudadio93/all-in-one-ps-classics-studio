using System.IO;
using System.IO.Compression;
using System.Windows.Media.Imaging;

namespace PS4Studio;

internal static class ManualValidation
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];
    private static readonly string[] Ps1Suffixes = [".manual.zip", ".manual.cbz", ".manual.cbr", ".manual.rar", ".manual.pdf", ".manual.dat", ".manual"];
    public static string? FindPs1(string disc)
    {
        string baseName = Path.Combine(Path.GetDirectoryName(disc)!, Path.GetFileNameWithoutExtension(disc));
        return Ps1Suffixes.Select(s => baseName + s).FirstOrDefault(p => File.Exists(p) || Directory.Exists(p));
    }
    public static string? Check(string path, bool ps1)
    {
        try
        {
            if (Directory.Exists(path))
            {
                if (!ps1) return "PS2 manuals must be ZIP or CBZ archives.";
                var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(p => ImageExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)).Take(251).ToArray();
                if (files.Length is 0 or > 250) return "The manual must contain 1 to 250 image pages.";
                foreach (string pagePath in files) { using var image = File.OpenRead(pagePath); string? error = CheckImage(image, image.Length); if (error is not null) return error; }
                return null;
            }
            if (!File.Exists(path)) return "Manual file was not found.";
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".zip" or ".cbz")
            {
                using var zip = ZipFile.OpenRead(path);
                var pages = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name) && ImageExtensions.Contains(Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase)).ToArray();
                if (pages.Length is 0 or > 250) return "The manual archive must contain 1 to 250 image pages.";
                foreach (var page in pages) { using var image = page.Open(); string? error = CheckImage(image, page.Length); if (error is not null) return $"{page.Name}: {error}"; }
                return null;
            }
            if (!ps1) return "PS2 manuals must be ZIP or CBZ archives.";
            using var file = File.OpenRead(path);
            if (file.Length < 8) return "The manual file is too short.";
            byte[] header = new byte[8]; file.ReadExactly(header);
            if (ext == ".pdf")
            {
                if (!header.AsSpan(0, 5).SequenceEqual("%PDF-"u8)) return "The selected file is not a PDF.";
                int tailLength = (int)Math.Min(file.Length, 2048);
                byte[] tail = new byte[tailLength]; file.Position = file.Length - tailLength; file.ReadExactly(tail);
                return System.Text.Encoding.Latin1.GetString(tail).Contains("%%EOF", StringComparison.Ordinal) ? null : "The PDF appears incomplete (end marker missing).";
            }
            if (header.AsSpan(0, 4).SequenceEqual("DOC "u8) || header.AsSpan(0, 4).SequenceEqual(new byte[] {0, 80, 71, 68})) return null;
            return "Choose a PDF, ZIP, CBZ, or valid DOCUMENT.DAT manual.";
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or OverflowException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return $"Manual could not be read: {e.Message}"; }
    }
    private static string? CheckImage(Stream input, long length)
    {
        if (length <= 0 || length > 32L * 1024 * 1024) return "Image page is empty or exceeds 32 MiB.";
        byte[] bytes = new byte[checked((int)length)]; input.ReadExactly(bytes);
        using var memory = new MemoryStream(bytes, writable: false);
        var decoder = BitmapDecoder.Create(memory, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0 || (long)decoder.Frames[0].PixelWidth * decoder.Frames[0].PixelHeight > 40000000) return "Image page is unreadable or too large.";
        var frame = decoder.Frames[0];
        int stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
        if (stride <= 0) return "Image page has an unsupported pixel format.";
        byte[] pixels = new byte[checked(stride * frame.PixelHeight)];
        frame.CopyPixels(pixels, stride, 0);
        return null;
    }
}
