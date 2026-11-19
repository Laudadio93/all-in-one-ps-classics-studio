using ClassicsStudio.Packaging;
using System.IO;

namespace PS4Studio;
internal static class Ps1XdeltaPatch
{
    public static void Apply(string image, string patch, string job, CancellationToken token)
    {
        var file = new FileInfo(patch);
        if (!file.Exists || file.Length < 4 || file.Length > 1024L * 1024 * 1024) throw new InvalidDataException("The xdelta patch is missing or exceeds 1 GiB.");
        string tools = Path.Combine(job, "patch-tool"); Directory.CreateDirectory(tools);
        string tool = Path.Combine(tools, "xdelta3.exe"), output = image + ".patched";
        using (var input = typeof(Ps1XdeltaPatch).Assembly.GetManifestResourceStream("PS4Studio.Resources.xdelta3.exe") ?? throw new FileNotFoundException("Built-in xdelta decoder is missing."))
        using (var destination = File.Create(tool)) input.CopyTo(destination);
        try
        {
            var command = PackageTool.Command(Path.GetFullPath(tool), job,
                ["-d", "-s", Path.GetFullPath(image), Path.GetFullPath(patch), Path.GetFullPath(output)]);
            PackageTool.RunAsync(command, token).GetAwaiter().GetResult();
            if (!File.Exists(output)) throw new InvalidDataException("xdelta did not produce a patched image.");
            if (new FileInfo(output).Length != new FileInfo(image).Length) throw new InvalidDataException("The patched image size changed; this disc layout is not supported.");
            File.Move(output, image, true);
        }
        finally { if (File.Exists(output)) File.Delete(output); }
    }
}
