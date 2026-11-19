using ClassicsStudio.Packaging;
using System.IO;
using System.Text.RegularExpressions;

namespace PS4Studio;

internal static class Widescreen
{
    public static bool Apply(string kit, string[] discs, string serial, string content, string job, Action<string> log, CancellationToken token)
    {
        if (discs.Length != 1) throw new InvalidDataException("Automatic widescreen patches currently support single-disc PS2 packages only.");
        string tools = Path.Combine(kit, "Tools");
        string seven = Path.Combine(tools, "7z.exe");
        string crcTool = Path.Combine(tools, "PS4", "crc.exe");
        string database = Path.Combine(tools, "PS4", "ps2-configs", "widescreen.dat");
        if (!File.Exists(seven) || !File.Exists(crcTool) || !File.Exists(database))
            throw new InvalidDataException("Widescreen matching requires 7z.exe, PS4/crc.exe, and PS4/ps2-configs/widescreen.dat in the selected toolkit.");
        string actual;
        using (var disc = File.OpenRead(discs[0])) actual = DiscMetadata.ReadSerial(disc);
        string expected = serial.Replace("-", "");
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"The entered game ID {serial} does not match the disc ID {actual}. Correct it before applying a widescreen patch.");
        string elfName = actual[..4] + "_" + actual[4..7] + "." + actual[7..];
        string temp = Path.Combine(job, "widescreen");
        Directory.CreateDirectory(temp);
        log("Checking this PS2 disc for a matching widescreen patch.");
        Run(seven, ["e", discs[0], elfName, "-o" + temp, "-y"], job, token, allowWarnings:true);
        string elf = Path.Combine(temp, elfName);
        if (!File.Exists(elf) || new FileInfo(elf).Length == 0) throw new InvalidDataException("Could not extract the disc's boot executable for widescreen matching.");
        string? crc = null;
        Run(crcTool, [elf], job, token, allowWarnings:false, stdout: line =>
        {
            Match match = Regex.Match(line, @"(?i)\bcrc:\s*([0-9a-f]{8})\b");
            if (match.Success) crc ??= match.Groups[1].Value.ToUpperInvariant();
        });
        if (crc is null) throw new InvalidDataException("The toolkit could not identify this game's executable for widescreen matching.");
        string patch = crc + ".LUA";
        Run(seven, ["e", database, patch, "-o" + temp, "-y"], job, token, allowWarnings:true);
        string source = Path.Combine(temp, patch);
        if (!File.Exists(source)) { log($"No widescreen patch found for this disc version (CRC {crc}); building without one."); return false; }
        if (new FileInfo(source).Length == 0 || new FileInfo(source).Length > 2 * 1024 * 1024) throw new InvalidDataException("The matching widescreen patch is empty or unexpectedly large.");
        string folder = Path.Combine(content, "patches");
        Directory.CreateDirectory(folder);
        File.Copy(source, Path.Combine(folder, serial + "_config.lua"), true);
        log($"Added widescreen patch for {serial} (CRC {crc}).");
        return true;
    }

    private static void Run(string exe, string[] args, string cwd, CancellationToken token, bool allowWarnings, Action<string>? stdout = null)
    {
        PackageTool.RunAsync(PackageTool.Command(exe, cwd, args), token,
            stdout: stdout, allowWarningExit: allowWarnings).GetAwaiter().GetResult();
    }
}
