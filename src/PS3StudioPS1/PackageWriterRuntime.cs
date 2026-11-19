using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace PS2ClassicsPackageStudio;

internal static class PackageWriterRuntime
{
    public static string ExtractWriter(string job, CancellationToken token)
    {
        string writer = Path.Combine(job, "writer");
        using (var resource = typeof(PackageWriterRuntime).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.pkg-writer.zip")
                              ?? throw new FileNotFoundException("The embedded PS3 package writer is missing."))
            Extract(resource, writer, token);
        return writer;
    }

    public static void Extract(Stream archive, string destination, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ZipFile.ExtractToDirectory(archive, destination);
        token.ThrowIfCancellationRequested();

        // Only the two bundled CPython 3.10 writers use this tested interface.
        // A future engine upgrade keeps its own module until it is qualified.
        string path = Path.Combine(destination, "_internal", "pkgcrypt.cp310-win_amd64.pyd");
        if (!File.Exists(path)) return;
        string original;
        using (var file = File.OpenRead(path)) original = Convert.ToHexString(SHA256.HashData(file));
        if (original is not ("C7AE9B26A484482AAA48EBC27373F453566A5BB30A3F10C83D0BAEA04EFEA74D"
                          or "1E03BB83789D43182D245087D9CDC1DD75680CCB57D0791FA44F0E85C8D24BF5")) return;

        using var accelerator = typeof(PackageWriterRuntime).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.ps3-pkgcrypt.pyd")
            ?? throw new FileNotFoundException("The built-in PS3 package writer is incomplete.");
        using var output = File.Create(path);
        accelerator.CopyTo(output);
    }
}
