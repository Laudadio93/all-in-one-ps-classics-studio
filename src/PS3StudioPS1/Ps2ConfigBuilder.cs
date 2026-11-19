using System.IO;

namespace PS2ClassicsPackageStudio;

internal static class Ps2ConfigBuilder
{
    public static byte[] Build(bool cache, bool performance, bool pressure, int multitap)
    {
        if (multitap is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(multitap));
        using var data = new MemoryStream();
        void Write(params byte[] value) => data.Write(value);
        Write(0x3d, 0, 0, 0, 0x57, 0x44, 0, 0);
        if (cache) Write(0x21, 0, 0, 0, 0, 0, 0, 0);
        if (performance) Write(0x46, 0, 0, 0);
        if (pressure) Write(0x50, 0, 0, 0);
        if (multitap != 0) { Write(0x1c, 0, 0, 0, (byte)multitap, 0, 0, 0); Write(0x1d, 0, 0, 0, (byte)multitap, 0, 0, 0); }
        return data.ToArray();
    }
}
