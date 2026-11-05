using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace FocusBloom.Desktop.Presentation;

public static class UiText
{
    public const string SettingKey = "appearance.language";
    private static readonly Lazy<Dictionary<string, string>> English = new(() =>
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream("FocusBloom.English.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    public static bool IsEnglish { get; private set; }
    public static CultureInfo DateCulture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "zh-CN");
    public static void Configure(string? language)
    {
        IsEnglish = language == "en";
        if (Application.Current is { } app)
        {
            app.Resources["InterfaceLanguage"] = XmlLanguage.GetLanguage(IsEnglish ? "en-US" : "zh-CN");
            app.Resources["NavigationColumnWidth"] = new GridLength(IsEnglish ? 200 : 160);
        }
    }
    public static string Get(string source) => IsEnglish && English.Value.TryGetValue(source, out var english) ? english : source;
    public static string Format(string source, params object[] arguments) => string.Format(Get(source), arguments);
}

public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension(string source) => Source = source;
    public string Source { get; }
    public override object ProvideValue(IServiceProvider serviceProvider) => UiText.Get(Source);
}

public sealed class UiTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string text ? UiText.Get(text) : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
