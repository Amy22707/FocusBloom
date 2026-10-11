using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public static class WindowAppearance
{
    public static void Attach(Window window) => window.SourceInitialized += (_, _) => Apply(window);

    public static bool Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || window.WindowStyle == WindowStyle.None) return false;
        var dark = Appearance.IsDark ? 1 : 0;
        var caption = ColorRef(((SolidColorBrush)Application.Current.Resources["CardBrush"]).Color);
        var text = ColorRef(((SolidColorBrush)Application.Current.Resources["TextBrush"]).Color);
        var modeResult = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var captionResult = DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        var textResult = DwmSetWindowAttribute(handle, 36, ref text, sizeof(int));
        return modeResult == 0 && captionResult == 0 && textResult == 0;
    }

    private static int ColorRef(Color color) => color.R | color.G << 8 | color.B << 16;
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
