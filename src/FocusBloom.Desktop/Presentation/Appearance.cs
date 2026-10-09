using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Presentation;

public enum AppearanceMode { System, Light, Dark }
public sealed record AppearanceChoice(AppearanceMode Mode, string Name);
public sealed record ThemePalette(string Id, string Name, string Swatch, string LightAccent, string DarkAccent);

public static class Appearance
{
    public static IReadOnlyList<ThemePalette> Palettes { get; } = new[]
    {
        new ThemePalette("fog-blue", "莫兰迪 · 雾蓝", "#9DAFBB", "#627F90", "#9CAFBF"),
        new ThemePalette("sage", "莫兰迪 · 鼠尾草绿", "#ABB8A6", "#718578", "#A2B69E"),
        new ThemePalette("cream", "马卡龙 · 奶油杏", "#F1DDC8", "#AD8566", "#D7B599"),
        new ThemePalette("pink", "马卡龙 · 樱花粉", "#EBC4D3", "#AE748D", "#D3A7BB"),
        new ThemePalette("mint", "马卡龙 · 薄荷绿", "#C5DDD4", "#668B7E", "#9AC5B3"),
        new ThemePalette("lilac", "马卡龙 · 淡紫", "#D5C9E5", "#927DAF", "#B7A4D0")
    };

    public static IReadOnlyList<AppearanceChoice> Modes { get; } = new[]
    {
        new AppearanceChoice(AppearanceMode.System, "跟随系统"),
        new AppearanceChoice(AppearanceMode.Light, "浅色"),
        new AppearanceChoice(AppearanceMode.Dark, "深色")
    };

    public static void Apply(ThemePalette palette, AppearanceMode mode)
    {
        if (Application.Current is null) return;
        var dark = mode == AppearanceMode.Dark || (mode == AppearanceMode.System && IsSystemDark());
        var accent = Parse(dark ? palette.DarkAccent : palette.LightAccent);
        var card = Parse(dark ? "#242832" : "#FFFFFF");
        var background = dark ? Parse("#191D25") : Mix(card, accent, 0.06);
        var resources = Application.Current.Resources;
        var colors = new Dictionary<string, Color>
        {
            ["BackgroundBrush"] = background,
            ["CardBrush"] = card,
            ["TextBrush"] = Parse(dark ? "#E5E9F0" : "#303944"),
            ["MutedBrush"] = Parse(dark ? "#A0A7B5" : "#838A95"),
            ["AccentBrush"] = accent,
            ["AccentTextBrush"] = accent,
            ["PrimaryTextBrush"] = Parse(dark ? "#1B2430" : "#FFFFFF"),
            ["BorderBrush"] = Mix(card, accent, dark ? 0.23 : 0.16),
            ["FieldBrush"] = Mix(card, accent, dark ? 0.06 : 0.035),
            ["SubtleBrush"] = Mix(card, accent, 0.08),
            ["HoverBrush"] = Mix(card, accent, 0.12),
            ["SelectedBrush"] = Mix(card, accent, dark ? 0.20 : 0.17),
            ["AccentSoftBrush"] = Mix(card, accent, 0.24)
        };
        foreach (var (key, color) in colors) resources[key] = new SolidColorBrush(color);
        resources["HeroStartColor"] = Mix(card, accent, dark ? 0.16 : 0.18);
        resources["HeroMiddleColor"] = Mix(card, accent, dark ? 0.08 : 0.11);
        resources["HeroEndColor"] = Mix(card, accent, dark ? 0.04 : 0.05);
    }

    private static bool IsSystemDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static Color Mix(Color baseColor, Color tint, double amount) => Color.FromRgb(
        (byte)Math.Round(baseColor.R + (tint.R - baseColor.R) * amount),
        (byte)Math.Round(baseColor.G + (tint.G - baseColor.G) * amount),
        (byte)Math.Round(baseColor.B + (tint.B - baseColor.B) * amount));
}
