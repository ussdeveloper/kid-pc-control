using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace KidPcControl.Setup;

public partial class App : Application
{
}

internal static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        void Go()
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            var dark = 1;
            DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            var caption = 0x0016120F;
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            var text = 0x00F7F2EE;
            DwmSetWindowAttribute(hwnd, DwmwaTextColor, ref text, sizeof(int));
        }

        if (window.IsLoaded) Go();
        else window.SourceInitialized += (_, _) => Go();
    }
}
