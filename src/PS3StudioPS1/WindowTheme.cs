using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace PS2ClassicsPackageStudio;

// Keep the standard Windows title bar while matching the current app theme.
internal static class WindowTheme
{
    private const int WmSettingChange = 0x001A;
    private const int WmSysColorChange = 0x0015;
    private const int WmThemeChanged = 0x031A;
    private const string Personalize = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Attach(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            if (source == null) return;
            source.AddHook(OnWindowMessage);
            Apply(source.Handle);
        };
    }

    private static IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmSettingChange or WmSysColorChange or WmThemeChanged) Apply(hwnd);
        return IntPtr.Zero;
    }

    private static void Apply(IntPtr hwnd)
    {
        try
        {
            object? setting = Registry.GetValue(Personalize, "AppsUseLightTheme", 1);
            int dark = !SystemParameters.HighContrast && setting is int light && light == 0 ? 1 : 0;
            // Windows 10 20H1 and later use 20; earlier supported builds use 19.
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Keep the native default title bar if the setting or DWM is unavailable.
        }
    }
}
