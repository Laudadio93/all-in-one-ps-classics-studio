using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using ClassicsStudio.Packaging;

namespace PS2ClassicsPackageStudio;

// Homebrew PKGs produced by the supported pop-fe engines use the PSL1GHT debug
// file table. Inspect just the names on the usual path, even for multi-GiB games.
internal static class ManualFreePackage
{
    private sealed record Entry(string Name, long Offset, long Size, bool Directory);
    private sealed record Index(string ContentId, long DataOffset, byte[] Context, List<Entry> Entries);

    public static async Task EnsureAsync(string path, string job, Action<string> log, CancellationToken token)
    {
        ClassicsBackend.ValidatePackage(path);
        Index index;
        using (var input = File.OpenRead(path)) index = ReadIndex(input, token);
        if (!index.Entries.Any(e => IsManual(e.Name))) return;

        // Some external pop-fe2 versions add a catalog manual unconditionally.
        // Repack only those jobs, from a fresh directory, using the bundled writer.
        log("The external engine included manual files. Rebuilding the PKG without them.");
        // Keep the large staged content on the user's selected build volume.
        string content = Directory.CreateDirectory(Path.Combine(job, "manual-free-" + Guid.NewGuid().ToString("N"))).FullName;
        byte[] buffer = new byte[1024 * 1024];
        using (var input = File.OpenRead(path))
        {
            foreach (var entry in index.Entries.Where(e => !IsManual(e.Name)))
            {
                token.ThrowIfCancellationRequested();
                string dest = Path.Combine(content, entry.Name);
                if (entry.Directory) { Directory.CreateDirectory(dest); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                using var output = new FileStream(dest, FileMode.CreateNew, FileAccess.Write);
                for (long pos = 0; pos < entry.Size;)
                {
                    token.ThrowIfCancellationRequested();
                    int count = (int)Math.Min(buffer.Length, entry.Size - pos);
                    ReadPlain(input, index.DataOffset, index.Context, entry.Offset + pos, buffer.AsSpan(0, count));
                    output.Write(buffer, 0, count);
                    pos += count;
                }
            }
        }
        string writerFolder = PackageWriterRuntime.ExtractWriter(job, token);
        string clean = Path.Combine(job, Guid.NewGuid().ToString("N") + ".pkg");
        var writer = PackageTool.Command(Path.Combine(writerFolder, "pkg.exe"), job, ["-c", index.ContentId, content, clean]);
        writer.Environment["TMP"] = writer.Environment["TEMP"] = writer.Environment["TMPDIR"] = job;
        await PackageTool.RunAsync(writer, token);
        ClassicsBackend.ValidatePackage(clean, index.ContentId);
        using (var verify = File.OpenRead(clean))
        {
            var actual = ReadIndex(verify, token);
            var expected = index.Entries.Where(e => !e.Directory && !IsManual(e.Name))
                .Select(e => (e.Name, e.Size)).ToHashSet();
            if (actual.Entries.Any(e => IsManual(e.Name)) ||
                !expected.SetEquals(actual.Entries.Where(e => !e.Directory).Select(e => (e.Name, e.Size))))
                throw new InvalidDataException("The rebuilt package failed its file-list check.");
        }
        token.ThrowIfCancellationRequested();
        File.Move(clean, path, true);
    }

    private static bool IsManual(string name)
    {
        // PS1 DOCUMENT.DAT and PS2 manual.idx / numbered texture pages.
        const string prefix = "USRDIR/CONTENT/";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string file = name[prefix.Length..];
        if (file.Equals("DOCUMENT.DAT", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("manual.idx", StringComparison.OrdinalIgnoreCase)) return true;
        string number = Path.GetFileNameWithoutExtension(file);
        return !file.Contains('/') && number.Length >= 3 && number.All(char.IsAsciiDigit) &&
            Path.GetExtension(file).Equals(".dxt", StringComparison.OrdinalIgnoreCase);
    }

    private static Index ReadIndex(FileStream stream, CancellationToken token)
    {
        byte[] header = new byte[128];
        stream.ReadExactly(header);
        if (BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4)) != 1)
            throw new InvalidDataException("The engine returned a PKG format whose manual contents cannot be verified.");
        int count = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20));
        long data = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(32));
        long size = BinaryPrimitives.ReadInt64BigEndian(header.AsSpan(40));
        if (count is < 1 or > 65536 || count * 32L > size)
            throw new InvalidDataException("Invalid package directory size.");
        byte[] context = new byte[64];
        for (int i = 0; i < 4; i++) header.AsSpan(96 + 8 * (i / 2), 8).CopyTo(context.AsSpan(i * 8));
        byte[] table = new byte[count * 32];
        ReadPlain(stream, data, context, 0, table);
        var entries = new List<Entry>(count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; ++i)
        {
            token.ThrowIfCancellationRequested();
            var row = table.AsSpan(i * 32, 32);
            uint offset = BinaryPrimitives.ReadUInt32BigEndian(row);
            uint length = BinaryPrimitives.ReadUInt32BigEndian(row[4..]);
            long start = BinaryPrimitives.ReadInt64BigEndian(row[8..]);
            long bytes = BinaryPrimitives.ReadInt64BigEndian(row[16..]);
            if (length is 0 or > 4096 || offset < table.Length || offset > size - length ||
                start < 0 || bytes < 0 || start > size - bytes)
                throw new InvalidDataException("Invalid package file range.");
            byte[] raw = new byte[length];
            ReadPlain(stream, data, context, offset, raw);
            string name = new UTF8Encoding(false, true).GetString(raw).Replace('\\', '/');
            foreach (string part in name.Split('/'))
            {
                if (part.Length == 0 || part.EndsWith('.') || part.EndsWith(' ') ||
                    part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    System.Text.RegularExpressions.Regex.IsMatch(part, @"^(CON|AUX|PRN|NUL|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    throw new InvalidDataException("Invalid path in package directory.");
            }
            if (!names.Add(name)) throw new InvalidDataException("Duplicate package file path.");
            entries.Add(new(name, start, bytes, row[27] == 4));
        }
        return new(Encoding.ASCII.GetString(header, 48, 48).TrimEnd('\0'), data, context, entries);
    }

    private static void ReadPlain(FileStream stream, long dataOffset, byte[] context, long offset, Span<byte> bytes)
    {
        stream.Position = dataOffset + offset;
        stream.ReadExactly(bytes);
        Span<byte> digest = stackalloc byte[20];
        while (!bytes.IsEmpty)
        {
            BinaryPrimitives.WriteInt64BigEndian(context.AsSpan(56), offset / 16);
            SHA1.HashData(context, digest);
            int skip = (int)(offset % 16), take = Math.Min(16 - skip, bytes.Length);
            for (int i = 0; i < take; i++) bytes[i] ^= digest[skip + i];
            offset += take;
            bytes = bytes[take..];
        }
    }
}
