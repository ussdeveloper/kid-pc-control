using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KidPcControl.Admin;

internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        void ApplyNow()
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            // COLORREF 0x00BBGGRR for #0F1216
            var caption = 0x0016120F;
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));

            var text = 0x00F7F2EE;
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        }

        if (window.IsLoaded)
            ApplyNow();
        else
            window.SourceInitialized += (_, _) => ApplyNow();
    }
}
