using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace PS2ClassicsPackageStudio;

// PCSX2 patches describe emulator memory writes. Only direct writes to the
// file-backed part of the boot ELF can be represented as changes to a disc.
internal static class WidescreenPatcher
{
    private const int Sector = 2048;
    private sealed record Write(long Offset, byte[] Bytes, byte[]? Expected);

    public static void Apply(string isoPath, string serial, Action<string> log, CancellationToken token)
    {
        try
        {
            using var image = new FileStream(isoPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            var (elfStart, elfLength) = FindBootElf(image, serial);
            if (elfLength < 52 || elfLength > 128 * 1024 * 1024) throw new InvalidDataException("The boot executable has an unsupported size.");
            byte[] elf = new byte[elfLength];
            image.Position = elfStart;
            image.ReadExactly(elf);
            token.ThrowIfCancellationRequested();
            if (!elf.AsSpan(0, 7).SequenceEqual(new byte[] { 0x7f, 0x45, 0x4c, 0x46, 1, 1, 1 }))
                throw new InvalidDataException("The boot executable is not a supported little-endian 32-bit ELF.");
            uint checksum = 0;
            for (int i = 0; i + 4 <= elf.Length; i += 4) checksum ^= BinaryPrimitives.ReadUInt32LittleEndian(elf.AsSpan(i));
            string id = serial[..4] + "-" + serial[4..] + "_" + checksum.ToString("X8", CultureInfo.InvariantCulture);
            using var resource = typeof(WidescreenPatcher).Assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.widescreen-patches.zip")
                ?? throw new FileNotFoundException("The bundled widescreen database is missing.");
            using var archive = new ZipArchive(resource, ZipArchiveMode.Read);
            var entry = archive.Entries.SingleOrDefault(e => e.FullName.EndsWith("/patches/" + id + ".pnach", StringComparison.OrdinalIgnoreCase));
            if (entry == null) { log($"Widescreen skipped · No exact patch for {id}."); return; }
            using var reader = new StreamReader(entry.Open());
            var writes = Parse(reader.ReadToEnd(), elf);
            if (writes.Count == 0) { log($"Widescreen skipped · No supported 16:9 writes for {id}."); return; }
            token.ThrowIfCancellationRequested();
            foreach (var write in writes)
            {
                image.Position = elfStart + write.Offset;
                image.Write(write.Bytes);
            }
            image.Flush();
            log($"Widescreen applied · {id} · {writes.Count} static ELF writes. Confirm the result on your PS3.");
        }
        catch (InvalidDataException ex) { log($"Widescreen skipped · {ex.Message}"); }
    }

    private static (long Start, int Length) FindBootElf(FileStream image, string serial)
    {
        byte[] sector = new byte[Sector];
        image.Position = 16L * Sector;
        image.ReadExactly(sector);
        if (sector[0] != 1 || !sector.AsSpan(1, 5).SequenceEqual("CD001"u8)) throw new InvalidDataException("Unsupported ISO directory.");
        long rootStart = (long)BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(158)) * Sector;
        uint rootLength = BinaryPrimitives.ReadUInt32LittleEndian(sector.AsSpan(166));
        if (rootLength > 16 * 1024 * 1024 || rootStart + rootLength > image.Length) throw new InvalidDataException("Invalid ISO root directory.");
        byte[] root = new byte[rootLength];
        image.Position = rootStart;
        image.ReadExactly(root);
        string expected = serial[..4] + "_" + serial.Substring(4, 3) + "." + serial[7..];
        (long Start, int Length)? found = null;
        for (int pos = 0; pos < root.Length;)
        {
            int len = root[pos];
            if (len == 0) { pos += Sector - pos % Sector; continue; }
            if (len < 34 || pos + len > root.Length || root[pos + 32] > len - 33) throw new InvalidDataException("Invalid ISO directory entry.");
            var rec = root.AsSpan(pos, len);
            string name = Encoding.ASCII.GetString(rec.Slice(33, rec[32])).Split(';')[0];
            if (name.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                if ((rec[25] & 0x82) != 0 || found != null) throw new InvalidDataException("The boot executable has an unsupported ISO extent.");
                long start = ((long)BinaryPrimitives.ReadUInt32LittleEndian(rec[2..]) + rec[1]) * Sector;
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(rec[10..]);
                if (start + length > image.Length || length > int.MaxValue) throw new InvalidDataException("The boot executable lies outside the image.");
                found = (start, (int)length);
            }
            pos += len;
        }
        return found ?? throw new InvalidDataException("The matching boot executable was not found in the ISO root.");
    }

    private static List<Write> Parse(string text, byte[] elf)
    {
        uint programOffset = U32(elf, 28);
        int entrySize = U16(elf, 42), count = U16(elf, 44);
        if (entrySize < 32 || count == 0 || (long)programOffset + (long)entrySize * count > elf.Length) throw new InvalidDataException("Invalid ELF program headers.");
        var segments = new List<(uint Address, uint Offset, uint Size)>();
        for (int i = 0; i < count; i++)
        {
            int p = checked((int)programOffset + i * entrySize);
            if (U32(elf, p) != 1) continue;
            uint offset = U32(elf, p + 4), addr = U32(elf, p + 8), size = U32(elf, p + 16);
            if ((ulong)offset + size > (ulong)elf.Length) throw new InvalidDataException("Invalid ELF load segment.");
            segments.Add((addr, offset, size));
        }
        var writes = new List<Write>();
        bool inSection = false, foundSection = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                if (inSection) break;
                inSection = line.Equals("[Widescreen 16:9]", StringComparison.OrdinalIgnoreCase);
                foundSection |= inSection;
                continue;
            }
            if (!inSection || line.Length == 0 || line.StartsWith("//")) continue;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            string annotation = comment < 0 ? "" : line[(comment + 2)..].Trim();
            string code = (comment < 0 ? line : line[..comment]).Trim();
            if (code.StartsWith("author=", StringComparison.OrdinalIgnoreCase) || code.StartsWith("description=", StringComparison.OrdinalIgnoreCase) || code.StartsWith("gsaspectratio=", StringComparison.OrdinalIgnoreCase)) continue;
            if (!code.StartsWith("patch=", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The matching patch contains unsupported instructions.");
            string[] parts = code[6..].Split(',').Select(s => s.Trim()).ToArray();
            if (parts.Length != 5 || (parts[0] != "0" && parts[0] != "1") || !parts[1].Equals("EE", StringComparison.OrdinalIgnoreCase) || !parts[3].Equals("word", StringComparison.OrdinalIgnoreCase) || !uint.TryParse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint address) || !uint.TryParse(parts[4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value) || (address & 3) != 0)
                throw new InvalidDataException("The matching patch requires unsupported runtime codes.");
            var matches = segments.Where(s => address >= s.Address && (ulong)address + 4 <= (ulong)s.Address + s.Size).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("The matching patch writes outside the boot executable.");
            int offset = checked((int)((ulong)matches[0].Offset + address - matches[0].Address));
            if (writes.Any(w => w.Offset == offset)) throw new InvalidDataException("The matching patch has overlapping writes.");
            byte[] data = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            byte[]? expected = null;
            var match = Regex.Match(annotation, @"^([0-9a-fA-F]{8})(?:\s|$)");
            if (match.Success)
            {
                expected = new byte[4];
                BinaryPrimitives.WriteUInt32LittleEndian(expected, uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                if (!elf.AsSpan(offset, 4).SequenceEqual(expected)) throw new InvalidDataException("The patch's expected original data does not match this disc.");
            }
            writes.Add(new(offset, data, expected));
        }
        if (!foundSection) return new();
        return writes;
    }
    private static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    private static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
}
