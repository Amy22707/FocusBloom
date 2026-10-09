using System;
using System.Windows;

namespace FocusBloom.Desktop.Windows;

public sealed record WindowLayout(double Left, double Top, double Width, double Height, bool Maximized, bool Topmost)
{
    public const string SettingKey = "window.layout";
    public const double MinimumWidth = 900;
    public const double MinimumHeight = 560;
    private const double EdgeMargin = 16;

    public static WindowLayout CreateDefault(Rect workArea)
    {
        var width = Math.Min(1120, workArea.Width * 0.86);
        var height = Math.Min(720, workArea.Height * 0.86);
        return new WindowLayout(workArea.Left + (workArea.Width - width) / 2,
            workArea.Top + (workArea.Height - height) / 2, width, height, false, false).FitTo(workArea);
    }

    public WindowLayout FitTo(Rect workArea)
    {
        var availableWidth = workArea.Width - EdgeMargin * 2;
        var availableHeight = workArea.Height - EdgeMargin * 2;
        var width = Math.Clamp(Width, Math.Min(MinimumWidth, availableWidth), availableWidth);
        var height = Math.Clamp(Height, Math.Min(MinimumHeight, availableHeight), availableHeight);
        return this with
        {
            Width = width,
            Height = height,
            Left = Math.Clamp(Left, workArea.Left + EdgeMargin, workArea.Right - EdgeMargin - width),
            Top = Math.Clamp(Top, workArea.Top + EdgeMargin, workArea.Bottom - EdgeMargin - height)
        };
    }
}
