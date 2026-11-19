using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PS2ClassicsPackageStudio;

// PDFium is bundled in the executable; no external PDF application is needed.
internal static class PdfManual
{
    private static readonly object Gate = new();
    private static IntPtr _library;
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_InitLibrary();
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_DestroyLibrary();
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size, IntPtr password);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDF_LoadPage(IntPtr document, int page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageWidth(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern double FPDF_GetPageHeight(IntPtr page);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport("pdfium", CallingConvention = CallingConvention.Cdecl)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int x, int y, int width, int height, int rotate, int flags);

    static PdfManual()
    {
        NativeLibrary.SetDllImportResolver(typeof(PdfManual).Assembly, (name, assembly, paths) => {
            if (name != "pdfium") return IntPtr.Zero;
            if (_library != IntPtr.Zero) return _library;
            string dir = Path.Combine(Path.GetTempPath(), "PS3StudioPdfium-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "pdfium.dll");
            using (var input = assembly.GetManifestResourceStream("PS2ClassicsPackageStudio.Resources.bin.pdfium.dll") ?? throw new FileNotFoundException("Built-in PDF renderer is missing."))
            using (var output = File.Create(file)) input.CopyTo(output);
            _library = NativeLibrary.Load(file);
            return _library;
        });
    }

    public static void ConvertToArchive(string pdf, string output)
    {
        lock (Gate)
        {
            byte[] bytes = File.ReadAllBytes(pdf);
            if (bytes.Length < 8 || bytes.Length > 128 * 1024 * 1024) throw new InvalidDataException("PDF manual is empty or exceeds 128 MiB.");
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr document = IntPtr.Zero;
            FPDF_InitLibrary();
            try
            {
                document = FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)bytes.Length, IntPtr.Zero);
                if (document == IntPtr.Zero) throw new InvalidDataException("PDF manual could not be opened (it may be damaged or password protected).");
                int count = FPDF_GetPageCount(document);
                if (count is < 1 or > 250) throw new InvalidDataException("PDF manual must contain 1 to 250 pages.");
                using var zip = ZipFile.Open(output, ZipArchiveMode.Create);
                for (int i = 0; i < count; i++)
                {
                    IntPtr page = FPDF_LoadPage(document, i);
                    if (page == IntPtr.Zero) throw new InvalidDataException($"PDF page {i + 1} could not be read.");
                    IntPtr bitmap = IntPtr.Zero;
                    try
                    {
                        double w = FPDF_GetPageWidth(page), h = FPDF_GetPageHeight(page);
                        if (w <= 0 || h <= 0 || !double.IsFinite(w) || !double.IsFinite(h)) throw new InvalidDataException($"PDF page {i + 1} has invalid dimensions.");
                        bitmap = FPDFBitmap_Create(960, 768, 1);
                        if (bitmap == IntPtr.Zero) throw new InvalidDataException("Could not allocate the manual page image.");
                        FPDFBitmap_FillRect(bitmap, 0, 0, 960, 768, 0xffffffff);
                        double scale = Math.Min(960 / w, 768 / h);
                        int width = Math.Max(1, (int)(w * scale)), height = Math.Max(1, (int)(h * scale));
                        FPDF_RenderPageBitmap(bitmap, page, (960 - width) / 2, (768 - height) / 2, width, height, 0, 0);
                        int stride = FPDFBitmap_GetStride(bitmap);
                        if (stride < 960 * 4 || stride > 960 * 8) throw new InvalidDataException("Unexpected PDF page image format.");
                        byte[] pixels = new byte[stride * 768]; Marshal.Copy(FPDFBitmap_GetBuffer(bitmap), pixels, 0, pixels.Length);
                        var image = BitmapSource.Create(960, 768, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                        using var encoded = new MemoryStream(); encoder.Save(encoded); encoded.Position = 0;
                        using var stream = zip.CreateEntry($"{i + 1:000}.png", CompressionLevel.Fastest).Open(); encoded.CopyTo(stream);
                    }
                    finally { if (bitmap != IntPtr.Zero) FPDFBitmap_Destroy(bitmap); FPDF_ClosePage(page); }
                }
            }
            finally { if (document != IntPtr.Zero) FPDF_CloseDocument(document); FPDF_DestroyLibrary(); pinned.Free(); }
        }
    }
}
