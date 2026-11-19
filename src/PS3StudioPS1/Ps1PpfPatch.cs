using System.Buffers.Binary;
using System.IO;

namespace PS2ClassicsPackageStudio;

internal static class Ps1PpfPatch
{
    public static void Apply(string image, string patch, CancellationToken token)
    {
        var info = new FileInfo(patch);
        if (!info.Exists || info.Length < 1084 || info.Length > 16 * 1024 * 1024) throw new InvalidDataException("Choose a PPF2 or PPF3 patch with a validation block (maximum 16 MiB).");
        byte[] data = File.ReadAllBytes(patch);
        bool ppf2 = data.AsSpan(0, 4).SequenceEqual("PPF2"u8), ppf3 = data.AsSpan(0, 4).SequenceEqual("PPF3"u8);
        if (!ppf2 && !ppf3) throw new InvalidDataException("Only PPF2 and PPF3 patches are supported.");
        if (ppf3 && (data[5] != 2 || data[56] != 0 || data[57] != 1)) throw new InvalidDataException("This PPF3 variant is not supported: method 2, CD image and a validation block are required.");
        if (data.AsSpan(Math.Max(0, data.Length - 128)).IndexOf(".DIZ"u8) >= 0) throw new InvalidDataException("PPF patches with DIZ metadata are not supported.");
        using var file = new FileStream(image, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (file.Length < 0x9320 + 1024) throw new InvalidDataException("Disc image is too small for the patch validation block.");
        if (ppf2 && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(56, 4)) != file.Length) throw new InvalidDataException("Patch image size does not match this disc.");
        byte[] block = new byte[1024]; file.Position = 0x9320; file.ReadExactly(block);
        if (!data.AsSpan(60, 1024).SequenceEqual(block)) throw new InvalidDataException("The patch validation block does not match this disc; choose the correct game version.");
        var records = new List<(long Position, int Offset, int Length)>(); int at = 1084; bool undo = ppf3 && data[58] != 0;
        while (at < data.Length)
        {
            token.ThrowIfCancellationRequested();
            int prefix = ppf2 ? 5 : 9;
            if (data.Length - at < prefix) throw new InvalidDataException("The PPF patch has a truncated record.");
            ulong position = ppf2 ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at, 4)) : BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(at, 8));
            int length = data[at + prefix - 1]; at += prefix;
            if (length == 0 || position > (ulong)file.Length || (ulong)length > (ulong)file.Length - position || data.Length - at < length * (undo ? 2 : 1)) throw new InvalidDataException("The PPF patch contains an invalid or out-of-range record.");
            records.Add(((long)position, at, length)); at += length * (undo ? 2 : 1);
        }
        if (records.Count == 0) throw new InvalidDataException("The PPF patch has no patch records.");
        foreach (var record in records) { token.ThrowIfCancellationRequested(); file.Position = record.Position; file.Write(data, record.Offset, record.Length); }
        file.Flush(true);
    }
}
