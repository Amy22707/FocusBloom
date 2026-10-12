using System;
using System.Windows;

namespace FocusBloom.Desktop.Windows;

public sealed record TimerFloatLayout(double Left, double Top, double Width, double Height,
    bool Topmost, string? Monitor = null)
{
    public const string SettingKey = "widget.timer.layout";
    public const double MinimumWidth = 240;
    public const double MinimumHeight = 180;

    public static TimerFloatLayout CreateDefault(Rect area) =>
        new TimerFloatLayout(area.Right - 356, area.Bottom - 260, 340, 244, true).FitTo(area);

    public TimerFloatLayout FitTo(Rect area)
    {
        var width = Math.Clamp(Width, Math.Min(MinimumWidth, area.Width - 16), Math.Min(620, area.Width - 16));
        var height = Math.Clamp(Height, Math.Min(MinimumHeight, area.Height - 16), Math.Min(420, area.Height - 16));
        return this with
        {
            Width = width, Height = height,
            Left = Math.Clamp(Left, area.Left + 8, area.Right - width - 8),
            Top = Math.Clamp(Top, area.Top + 8, area.Bottom - height - 8)
        };
    }
}
