using System.Buffers.Binary;
using ClassicsStudio.Packaging;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PS2ClassicsPackageStudio;

internal sealed record BuildProgress(string Stage, double? Percent, string Detail)
{
    public static BuildProgress FromBytes(string stage, int startPercent, int endPercent, long completed, long total)
    {
        completed = Math.Clamp(completed, 0, total);
        double fraction = total > 0 ? (double)completed / total : 0;
        const long mib = 1024 * 1024;
        string detail = total >= mib
            ? $"{completed / (double)mib:N1} of {total / (double)mib:N1} MiB"
            : $"{completed:N0} of {total:N0} bytes";
        return new(stage, startPercent + fraction * (endPercent - startPercent), detail);
    }
}

internal static class ClassicsBackend
{
    public const string Bundled = "Built-in PS2 engine";
    private const string Placeholder = "2P0001-PS2U10000_00-0000111122223333";
    private static readonly string[] TemplateFiles = ["PARAM.SFO", "ICON0.PNG", "PIC0.PNG", "PIC1.PNG", "PS3LOGO.DAT", "USRDIR/SAVEDATA/SCEVMC0.VME", "USRDIR/SAVEDATA/SCEVMC1.VME"];
    private static readonly string[] ProfileTypes = ["CUSTOM", "NET", "GX", "SOFT"];
    private static string ProfileName(string serial) => serial[..4] + "_" + serial.Substring(4, 3) + "." + serial[7..] + ".CONFIG";
    private static Stream OpenToolkit() => typeof(ClassicsBackend).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.ps2-toolkit.zip")
        ?? throw new FileNotFoundException("Built-in PS2 engine is missing.");
    private static readonly Lazy<HashSet<string>> BundledProfiles = new(() =>
    {
        using var stream = OpenToolkit();
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("CONFIGS/", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    public static bool IsToolFolder(string path) => path == Bundled || Directory.Exists(path)
        && File.Exists(Path.Combine(path, "ps2classic.exe"))
        && File.Exists(Path.Combine(path, "scetool", "rap2rifkey.exe"))
        && File.Exists(Path.Combine(path, "exdata", Placeholder + ".rap"))
        && TemplateFiles.All(f => File.Exists(Path.Combine(path, "PS2U10000", f)));

    public static string[] FindProfiles(string engine, string serial)
    {
        string file = ProfileName(serial);
        if (engine == Bundled)
            return ProfileTypes.Where(t => BundledProfiles.Value.Contains($"CONFIGS/{t}/{file}")).ToArray();
        return ProfileTypes.Where(t => File.Exists(Path.Combine(engine, "CONFIGS", t, file))).ToArray();
    }

    public static async Task BuildAsync(string folder, string isoPath, string job, string target, string title, bool widescreen,
        Action<string> log, Action<BuildProgress> progress, CancellationToken token, string cover="", string backdrop="", bool autoArtwork=false, bool fitCover=false, string customConfig="", string profileChoice="Automatic")
    {
        token.ThrowIfCancellationRequested();
        progress(new("Reading disc info", 0, Path.GetFileName(isoPath)));
        if (!IsToolFolder(folder)) throw new DirectoryNotFoundException("Choose the extracted PS2 Classics GUI toolkit folder.");
        using var iso = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        string serial = DiscMetadata.ReadSerial(iso);
        string? profile = null;
        if (string.IsNullOrWhiteSpace(customConfig) && profileChoice != "No profile")
        {
            string[] matches = FindProfiles(folder, serial);
            profile = profileChoice == "Automatic" ? matches.FirstOrDefault()
                : matches.FirstOrDefault(t => t.Equals(profileChoice, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException($"No {profileChoice} profile is available for {serial}. Choose Automatic or another profile.");
        }
        if (folder == Bundled)
        {
            progress(new("Preparing built-in PS2 engine", 0, "Extracting required components"));
            using var resource = OpenToolkit();
            using var zip = new ZipArchive(resource, ZipArchiveMode.Read);
            folder = Path.Combine(job, "ps2-engine");
            string[] runtimeFiles = ["ps2classic.exe", "cygwin1.dll", "zlib1.dll", "ps2titleid.txt",
                "scetool/rap2rifkey.exe", "scetool/cygwin1.dll", "scetool/zlib1.dll", $"exdata/{Placeholder}.rap"];
            var entries = runtimeFiles.Concat(TemplateFiles.Select(f => "PS2U10000/" + f));
            if (profile is not null) entries = entries.Append($"CONFIGS/{profile}/{ProfileName(serial)}");
            foreach (string name in entries)
            {
                token.ThrowIfCancellationRequested();
                var entry = zip.GetEntry(name) ?? throw new InvalidDataException($"The built-in PS2 engine is missing {name}.");
                string path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var input = entry.Open();
                await using var destination = File.Create(path);
                await input.CopyToAsync(destination, token);
            }
            if (!IsToolFolder(folder)) throw new InvalidDataException("The built-in PS2 engine is incomplete.");
        }
        folder = Path.GetFullPath(folder);
        if (string.IsNullOrWhiteSpace(title))
        {
            string database = Path.Combine(folder, "ps2titleid.txt");
            string? row = File.Exists(database) ? File.ReadLines(database, Encoding.Latin1).FirstOrDefault(t => t.StartsWith(serial + " ", StringComparison.OrdinalIgnoreCase)) : null;
            title = row?[10..].Trim() ?? Path.GetFileNameWithoutExtension(isoPath);
        }
        log($"{title} · {serial}");
        var volume = new DriveInfo(Path.GetPathRoot(job)!);
        long required = checked(3 * (iso.Length + iso.Length / 512 + 20 * 1024 * 1024) + 20 * 1024 * 1024);
        if (volume.IsReady && volume.AvailableFreeSpace < required)
            throw new IOException($"This build needs approximately {required / 1073741824.0:0.0} GiB free on the output drive.");
        string package = Path.Combine(job, "package");
        string usrdir = Path.Combine(package, "USRDIR");
        foreach (string file in TemplateFiles)
        {
            string dest = Path.Combine(package, file);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(Path.Combine(folder, "PS2U10000", file), dest);
            File.SetAttributes(dest, FileAttributes.Normal);
        }
        if (!string.IsNullOrWhiteSpace(cover)) Artwork.Write(cover, Path.Combine(package, "ICON0.PNG"), 320, 176, fitCover);
        else if (autoArtwork)
        {
            progress(new("Finding PS2 cover artwork", 0, serial));
            string? downloaded = await Ps2CoverLookup.DownloadAsync(serial, job, log, token);
            if (downloaded is not null)
            {
                try { Artwork.Write(downloaded, Path.Combine(package, "ICON0.PNG"), 320, 176, fit: true); log("Added matching PS2 cover artwork."); }
                catch (Exception e) when (e is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FormatException) { log($"Could not use downloaded cover: {e.Message}. Using the default icon."); }
            }
        }
        if (!string.IsNullOrWhiteSpace(backdrop)) Artwork.Write(backdrop, Path.Combine(package, "PIC1.PNG"), 1920, 1080);
        DiscMetadata.CustomizeTemplate(Path.Combine(package, "PARAM.SFO"), title, serial);
        string localImage = Path.Combine(job, "disc.iso");
        log("Preparing a working copy of the DVD image.");
        void Copied(long bytes) => progress(BuildProgress.FromBytes("Copying disc image", 1, 16, bytes, iso.Length));
        Copied(0);
        long length = await DiscMetadata.PrepareAsync(iso, localImage, Copied, token);
        if (widescreen) { progress(new("Checking widescreen patch", 16, serial)); WidescreenPatcher.Apply(localImage, serial, log, token); }
        progress(new("Preparing game metadata", 16, serial));
        File.Copy(Path.Combine(folder, "exdata", Placeholder + ".rap"), Path.Combine(job, "template.rap"));
        await RunToolAsync(Path.Combine(folder, "scetool", "rap2rifkey.exe"), ["template.rap", "klic.bin"], job, token);
        if (new FileInfo(Path.Combine(job, "klic.bin")).Length != 16) throw new InvalidDataException("The toolkit could not read its template license.");
        string writer = PackageWriterRuntime.ExtractWriter(job, token);

        string discId = serial[..4] + "_" + serial[4..];
        File.WriteAllText(Path.Combine(job, "gameid.txt"), discId, Encoding.ASCII);
        await RunToolAsync(Path.Combine(writer, "make_npdata.exe"),
            ["-e", "gameid.txt", "ISO.BIN.EDAT", "1", "1", "3", "0", "16", "2", "01", Placeholder, "8", "template.rap"], job, token);
        var edat = Path.Combine(job, "ISO.BIN.EDAT");
        using (var file = File.OpenRead(edat))
        {
            byte[] header = new byte[144];
            file.ReadExactly(header);
            if (!header.AsSpan(0, 4).SequenceEqual("NPD\0"u8) || BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(136)) != discId.Length || file.Length != 320)
                throw new InvalidDataException("The toolkit could not encode the game's PS3 disc identifier.");
        }
        File.Move(edat, Path.Combine(usrdir, "ISO.BIN.EDAT"));
        log("Encrypting the disc for PS2 Classics.");
        var encoded = Path.Combine(job, "encoded.bin");
        long encodedLength = EncryptedLength(length);
        long encrypted = 0;
        progress(BuildProgress.FromBytes("Encrypting disc", 17, 60, 0, encodedLength));
        await Encrypt("disc.iso", "ISO.BIN.ENC", length, () =>
        {
            var current = WrittenLength(encoded);
            if (current <= encrypted) return;
            encrypted = current;
            progress(BuildProgress.FromBytes("Encrypting disc", 17, 60, current, encodedLength));
        });
        File.Delete(localImage);

        progress(new("Checking compatibility profile", 60, serial));
        if (!string.IsNullOrWhiteSpace(customConfig))
        {
            log("Adding the selected custom PS2 compatibility profile.");
            File.Copy(customConfig, Path.Combine(job, "config.bin"));
            await Encrypt("config.bin", "CONFIG", new FileInfo(customConfig).Length);
        }
        else if (profile is not null)
        {
            string config = Path.Combine(folder, "CONFIGS", profile, ProfileName(serial));
            log($"Adding the {profile} compatibility profile for {serial}.");
            File.Copy(config, Path.Combine(job, "config.bin"));
            await Encrypt("config.bin", "CONFIG", new FileInfo(config).Length);
        }
        token.ThrowIfCancellationRequested();
        log("Writing the PS3 package.");
        string contentId = $"UP0000-{serial}_00-PS2CLASSICS00000";
        // The bundled writer stages the package data in a TemporaryFile in this job,
        // then encrypts it into the .pkg. The two counters describe separate passes.
        long dataBytes = PackageDataLength(package);
        long packageBytes = dataBytes + 0x1A0; // PSL1GHT package header and trailer
        string? stagingFile = null;
        long staged = 0, written = 0;
        progress(BuildProgress.FromBytes("Assembling package", 61, 75, 0, dataBytes));
        await RunToolAsync(Path.Combine(writer, "pkg.exe"), ["-c", contentId, "package", Path.GetFileName(target)], job, token, () =>
        {
            long size = WrittenLength(target);
            if (size > written)
            {
                written = size;
                progress(BuildProgress.FromBytes("Writing package", 75, 98, size, packageBytes));
            }
            else if (written == 0)
            {
                stagingFile ??= Directory.EnumerateFiles(job, "tmp*").FirstOrDefault();
                size = stagingFile == null ? 0 : WrittenLength(stagingFile);
                if (size <= staged) return;
                staged = size;
                progress(BuildProgress.FromBytes("Assembling package", 61, 75, size, dataBytes));
            }
        });
        progress(new("Verifying package", 99, "Checking the PS3 header, disc ID and package length"));
        ValidatePackage(target, contentId);

        async Task Encrypt(string input, string name, long plainLength, Action? sample = null)
        {
            await RunToolAsync(Path.Combine(folder, "ps2classic.exe"), ["e", "cex", "klic.bin", input, "encoded.bin", name, Placeholder], job, token, sample);
            using (var result = File.OpenRead(encoded))
            {
                byte[] h = new byte[144];
                result.ReadExactly(h);
                if (!h.AsSpan(0, 4).SequenceEqual("PS2\0"u8) || BinaryPrimitives.ReadInt64BigEndian(h.AsSpan(136)) != plainLength ||
                    result.Length != EncryptedLength(plainLength))
                    throw new InvalidDataException($"The toolkit produced an incomplete {name}.");
            }
            File.Move(encoded, Path.Combine(usrdir, name));
        }
    }

    private static long EncryptedLength(long plainLength)
    {
        long chunks = (plainLength + 16383) / 16384;
        return (1 + chunks + (chunks + 511) / 512) * 16384;
    }

    private static long PackageDataLength(string directory)
    {
        long length = 0;
        var root = new DirectoryInfo(directory);
        foreach (var entry in root.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
        {
            int nameLength = Encoding.ASCII.GetByteCount(Path.GetRelativePath(directory, entry.FullName));
            length += 32 + ((nameLength + 15) & ~15);
            if (entry is FileInfo file) length += (file.Length + 15) & ~15L;
        }
        return length;
    }

    private static long WrittenLength(string path)
    {
        // Directory metadata on Windows can lag until a native writer closes its handle.
        // Share deletion too, since the packer uses delete-on-close for its staging file.
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return RandomAccess.GetLength(handle);
        }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }
    }

    public static void ValidatePackage(string path, string? expectedId = null)
    {
        using var pkg = File.OpenRead(path);
        byte[] h = new byte[128];
        pkg.ReadExactly(h);
        long dataOffset = BinaryPrimitives.ReadInt64BigEndian(h.AsSpan(32)), dataSize = BinaryPrimitives.ReadInt64BigEndian(h.AsSpan(40));
        if (!h.AsSpan(0, 4).SequenceEqual(new byte[] { 0x7f, 0x50, 0x4b, 0x47 }) ||
            BinaryPrimitives.ReadUInt16BigEndian(h.AsSpan(6)) != 1 ||
            BinaryPrimitives.ReadUInt32BigEndian(h.AsSpan(20)) == 0 ||
            BinaryPrimitives.ReadInt64BigEndian(h.AsSpan(24)) != pkg.Length ||
            dataOffset < 128 || dataSize <= 0 || dataSize > pkg.Length - dataOffset ||
            (expectedId != null && Encoding.ASCII.GetString(h, 48, 48).TrimEnd('\0') != expectedId))
            throw new InvalidDataException("The output failed PS3 package header and length verification.");
    }

    internal static Task RunProcessAsync(ProcessStartInfo info, Action<string>? log, CancellationToken token) =>
        PackageTool.RunAsync(info, token, stdout: log, stderr: log);

    private static Task RunToolAsync(string file, string[] args, string job, CancellationToken token, Action? sample = null)
    {
        var info = PackageTool.Command(file, job, args);
        info.Environment["TMPDIR"] = info.Environment["TEMP"] = info.Environment["TMP"] = job;
        return PackageTool.RunAsync(info, token, sample: sample);
    }
}
