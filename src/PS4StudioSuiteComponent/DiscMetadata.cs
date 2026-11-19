using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PS4Studio;

internal static class DiscMetadata
{
    private const int Sector = 2048;
    private const int Segment = 16384;

    public static string ReadSerial(FileStream iso)
    {
        if (iso.Length < 18 * Sector || iso.Length % Sector != 0)
            throw new InvalidDataException("Choose a PS2 DVD ISO with 2048-byte sectors.");
        byte[] sector = new byte[Sector];
        for (int i = 16; i < 80 && (long)(i + 1) * Sector <= iso.Length; i++)
        {
            iso.Position = (long)i * Sector;
            iso.ReadExactly(sector);
            if (!sector.AsSpan(1, 5).SequenceEqual("CD001"u8) || sector[0] == 255) break;
            if (sector[0] != 1) continue;
            if (BinaryPrimitives.ReadUInt16LittleEndian(sector.AsSpan(128)) != Sector) break;
            byte[] root = ReadExtent(iso, sector.AsSpan(156), 16 * 1024 * 1024);
            for (int offset = 0; offset < root.Length;)
            {
                int len = root[offset];
                if (len == 0) { offset += Sector - offset % Sector; continue; }
                if (len < 34 || len > root.Length - offset) throw new InvalidDataException("Damaged ISO directory.");
                var record = root.AsSpan(offset, len);
                offset += len;
                if (record[32] > len - 33) throw new InvalidDataException("Damaged ISO filename.");
                string name = Encoding.ASCII.GetString(record.Slice(33, record[32])).Split(';')[0];
                if (!name.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase)) continue;
                var cnf = Encoding.ASCII.GetString(ReadExtent(iso, record, 1024 * 1024));
                var boot = Regex.Match(cnf, @"(?im)^\s*BOOT2\s*=\s*[^\r\n]*?([A-Z]{4})[_-](\d{3})\.?(\d{2})(?:;\d+)?\s*$");
                if (boot.Success)
                    return (boot.Groups[1].Value + boot.Groups[2].Value + boot.Groups[3].Value).ToUpperInvariant();
                throw new InvalidDataException("SYSTEM.CNF has no PS2 BOOT2 entry with a recognizable disc serial.");
            }
            break;
        }
        throw new InvalidDataException("A PS2 DVD volume with SYSTEM.CNF could not be found in this image.");
    }

    private static byte[] ReadExtent(FileStream iso, ReadOnlySpan<byte> record, int limit)
    {
        long start = ((long)BinaryPrimitives.ReadUInt32LittleEndian(record[2..]) + record[1]) * Sector;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(record[10..]);
        if (length > limit || start + length > iso.Length) throw new InvalidDataException("Disc directory points outside the ISO or exceeds the supported size.");
        byte[] data = new byte[length];
        iso.Position = start;
        iso.ReadExactly(data);
        return data;
    }

    public static async Task<long> PrepareAsync(FileStream source, string path, Action<long> copied, CancellationToken token)
    {
        source.Position = 0;
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, true);
        byte[] buffer = new byte[1024 * 1024];
        long nextUpdate = 0;
        var clock = Stopwatch.StartNew();
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, count), token);
            if (clock.ElapsedMilliseconds >= nextUpdate || source.Position == source.Length)
            {
                copied(source.Position);
                nextUpdate = clock.ElapsedMilliseconds + 200;
            }
        }
        long aligned = checked((output.Length + Segment - 1) / Segment * Segment);
        if (aligned == output.Length)
        {
            byte[] existing = new byte[16];
            output.Position = aligned - Segment;
            await output.ReadExactlyAsync(existing, token);
            if (existing.AsSpan(0, 4).SequenceEqual("LIMG"u8))
            {
                if (BinaryPrimitives.ReadUInt32BigEndian(existing.AsSpan(4)) != 1 ||
                    BinaryPrimitives.ReadUInt32BigEndian(existing.AsSpan(8)) != (aligned - Segment) / Sector ||
                    BinaryPrimitives.ReadUInt32BigEndian(existing.AsSpan(12)) != Sector)
                    throw new InvalidDataException("The ISO already contains an inconsistent DVD LIMG footer.");
                return aligned;
            }
        }
        byte[] footer = new byte[Segment];
        "LIMG"u8.CopyTo(footer);
        BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(8), checked((uint)(aligned / Sector)));
        BinaryPrimitives.WriteUInt32BigEndian(footer.AsSpan(12), Sector);
        output.Position = aligned;
        await output.WriteAsync(footer, token);
        return output.Length;
    }

    public static void CustomizeTemplate(string path, string title, string serial)
    {
        var sfo = File.ReadAllBytes(path);
        if (!sfo.AsSpan(0, 4).SequenceEqual("\0PSF"u8)) throw new InvalidDataException("Invalid template PARAM.SFO.");
        int keys = I32(8), values = I32(12), count = I32(16);
        bool setTitle = false, setId = false;
        for (int i = 0; i < count; i++)
        {
            int entry = checked(20 + 16 * i);
            int key = checked(keys + BinaryPrimitives.ReadUInt16LittleEndian(sfo.AsSpan(entry)));
            string name = Encoding.ASCII.GetString(sfo, key, Array.IndexOf(sfo, (byte)0, key) - key);
            if (name != "TITLE" && name != "TITLE_ID") continue;
            var value = Encoding.UTF8.GetBytes(name == "TITLE" ? title : serial);
            int capacity = I32(entry + 8);
            if (value.Length >= capacity) throw new InvalidDataException($"The {name} field exceeds the template limit of {capacity - 1} UTF-8 bytes.");
            var field = sfo.AsSpan(checked(values + I32(entry + 12)), capacity);
            field.Clear();
            value.CopyTo(field);
            BinaryPrimitives.WriteInt32LittleEndian(sfo.AsSpan(entry + 4), value.Length + 1);
            setTitle |= name == "TITLE";
            setId |= name == "TITLE_ID";
        }
        if (!setTitle || !setId) throw new InvalidDataException("The template is missing its title or title ID.");
        File.WriteAllBytes(path, sfo);
        int I32(int offset) => BinaryPrimitives.ReadInt32LittleEndian(sfo.AsSpan(offset));
    }
}

