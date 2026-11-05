using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Presentation;

public enum AppearanceMode { System, Light, Dark }
public sealed record AppearanceChoice(AppearanceMode Mode, string Name);
public sealed record ThemePalette(string Id, string Name, string Swatch, string LightAccent, string DarkAccent);

public static class Appearance
{
    public static event EventHandler? Changed;
    public static bool IsDark { get; private set; }
    public static IReadOnlyList<ThemePalette> Palettes { get; } = new[]
    {
        new ThemePalette("fog-blue", "水色", "#B7D8E1", "#487A8B", "#9ECBD7"),
        new ThemePalette("ink-blue", "墨蓝", "#A6B9D3", "#4C668D", "#ADC5EB"),
        new ThemePalette("sage", "鼠尾草绿", "#ABB8A6", "#718578", "#A2B69E"),
        new ThemePalette("mint", "薄荷绿", "#C5DDD4", "#668B7E", "#9AC5B3"),
        new ThemePalette("pine", "松针绿", "#A3C8BB", "#467467", "#9CCFBA"),
        new ThemePalette("cream", "奶油杏", "#F1DDC8", "#AD8566", "#D7B599"),
        new ThemePalette("ochre", "琥珀金", "#E8C98C", "#896329", "#E4C17C"),
        new ThemePalette("tea", "茶棕", "#CDB2A1", "#8C604E", "#D9AE99"),
        new ThemePalette("pink", "樱花粉", "#EBC4D3", "#AE748D", "#D3A7BB"),
        new ThemePalette("rosewood", "玫瑰木", "#DDB4B5", "#925A60", "#E2AAB0"),
        new ThemePalette("lilac", "淡紫", "#D5C9E5", "#927DAF", "#B7A4D0"),
        new ThemePalette("plum", "梅子紫", "#C7B4D1", "#775883", "#D0B0DD")
    };

    public static IReadOnlyList<AppearanceChoice> Modes { get; } = new[]
    {
        new AppearanceChoice(AppearanceMode.System, "跟随系统"),
        new AppearanceChoice(AppearanceMode.Light, "浅色"),
        new AppearanceChoice(AppearanceMode.Dark, "深色")
    };

    public static ThemePalette? CustomPalette(string? color)
    {
        if (color is not { Length: 7 } || color[0] != '#' ||
            !uint.TryParse(color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _)) return null;
        var accent = Parse(color);
        return new("custom", "自定义配色", color,
            Hex(Mix(accent, Colors.Black, 0.24)), Hex(Mix(accent, Colors.White, 0.38)));
    }

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    public static void Apply(ThemePalette palette, AppearanceMode mode)
    {
        if (Application.Current is null) return;
        var dark = mode == AppearanceMode.Dark || (mode == AppearanceMode.System && IsSystemDark());
        IsDark = dark;
        var accent = Parse(dark ? palette.DarkAccent : palette.LightAccent);
        var card = Parse(dark ? "#242832" : "#FFFFFF");
        var background = dark ? Parse("#191D25") : Mix(card, accent, 0.035);
        var resources = Application.Current.Resources;
        var colors = new Dictionary<string, Color>
        {
            ["BackgroundBrush"] = background,
            ["CardBrush"] = card,
            ["TextBrush"] = Parse(dark ? "#E5E9F0" : "#303944"),
            ["MutedBrush"] = Parse(dark ? "#A6AFBE" : "#76808D"),
            ["AccentBrush"] = accent,
            ["AccentTextBrush"] = dark ? accent : Mix(accent, Parse("#303944"), 0.12),
            ["PrimaryTextBrush"] = Parse(dark ? "#1B2430" : "#FFFFFF"),
            ["BorderBrush"] = Mix(card, accent, dark ? 0.23 : 0.13),
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
        foreach (Window window in Application.Current.Windows) WindowAppearance.Apply(window);
        Changed?.Invoke(null, EventArgs.Empty);
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
