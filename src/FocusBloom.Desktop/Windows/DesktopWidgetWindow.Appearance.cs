using System;
using System.Threading.Tasks;
using System.Windows.Media;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private static readonly string[] WidgetBrushes = { "CardBrush", "TextBrush", "MutedBrush", "FieldBrush", "SubtleBrush", "HoverBrush", "SelectedBrush", "AccentSoftBrush", "BorderBrush", "AccentBrush", "AccentTextBrush", "PrimaryTextBrush" };
    private string? _imagePath;
    private ImageSource? _widgetImage;
    public void ApplyBackground()
    {
        foreach (var key in WidgetBrushes) Resources.Remove(key);
        WidgetBackground.Background = null;
        var mode = Option.BackgroundMode;
        if (mode == "跟随主题") return;
        var color = (Color)ColorConverter.ConvertFromString(Option.BackgroundColor);
        var image = mode == "图片";
        if (image)
        {
            color = Color.FromRgb(25, 33, 43);
            if (_imagePath != Option.BackgroundImage) { _imagePath = Option.BackgroundImage; _widgetImage = null; if (_imagePath is { Length: > 0 } path) _ = LoadWidgetBackground(path); }
            if (_widgetImage is not null) WidgetBackground.Background = new ImageBrush(_widgetImage) { Stretch = Stretch.UniformToFill };
        }
        var dark = .2126 * color.R + .7152 * color.G + .0722 * color.B < 150;
        void Brush(string key, Color value) => Resources[key] = new SolidColorBrush(value);
        Color Hex(string value) => (Color)ColorConverter.ConvertFromString(value);
        Color Overlay(byte alpha, bool white) => Color.FromArgb(alpha, white ? (byte)255 : (byte)0, white ? (byte)255 : (byte)0, white ? (byte)255 : (byte)0);
        Brush("CardBrush", image && _widgetImage is not null ? Color.FromArgb(175, color.R, color.G, color.B) : color);
        Brush("TextBrush", Hex(dark ? "#F3F5FA" : "#293440")); Brush("MutedBrush", Hex(dark ? "#C5CEDA" : "#596574"));
        Brush("FieldBrush", Overlay(image ? (byte)130 : (byte)35, !image && dark)); Brush("SubtleBrush", Overlay(28, dark)); Brush("HoverBrush", Overlay(65, dark));
        Brush("SelectedBrush", Overlay(85, dark)); Brush("AccentSoftBrush", Overlay(75, dark)); Brush("BorderBrush", Overlay(45, dark));
        Brush("AccentBrush", Hex(dark ? "#C7B4EA" : "#74618E")); Brush("AccentTextBrush", Hex(dark ? "#E0CFFA" : "#675181")); Brush("PrimaryTextBrush", Hex(dark ? "#242B36" : "#FFFFFF"));
    }
    private async Task LoadWidgetBackground(string path)
    {
        var source = await Task.Run(() => FocusMediaViewModel.LoadImage(path, 1600));
        if (_imagePath != path || !IsVisible) return;
        _widgetImage = source; ApplyBackground();
    }
}
