using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record FontChoice(string Family, string Name, string Preview)
{
    public FontFamily Typeface { get; } = new(Family);
}

public sealed class FocusTypographyViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly Action<string> _report;
    private FontChoice _interfaceFont;
    private FontChoice _chinese;
    private FontChoice _english;
    private FontChoice _timer;
    private double _interfaceSizePercent;
    private double _focusSizePercent;
    private sealed record FontPreferences(string Chinese, string English, string Timer);

    public FocusTypographyViewModel(IFocusStore store, Action<string> report)
    {
        _store = store; _report = report;
        var installed = Fonts.SystemFontFamilies.SelectMany(font => font.FamilyNames.Values.Append(font.Source)).ToArray();
        FontChoice[] Available(FontChoice[] choices) => choices.Where(choice => installed.Contains(choice.Family, StringComparer.OrdinalIgnoreCase)).ToArray();
        ChineseFonts = Available([
            new("KaiTi", "楷体", "执着于理想，纯粹于当下。"),
            new("Microsoft YaHei", "微软雅黑", "执着于理想，纯粹于当下。"),
            new("DengXian", "等线", "执着于理想，纯粹于当下。"),
            new("SimSun", "宋体", "执着于理想，纯粹于当下。"),
            new("FangSong", "仿宋", "执着于理想，纯粹于当下。")
        ]);
        if (ChineseFonts.Count == 0) ChineseFonts = [new("Global User Interface", "系统中文字体", "执着于理想，纯粹于当下。")];
        InterfaceFonts = [new("Microsoft YaHei UI", "系统默认", ""), .. ChineseFonts];
        var interfaceFont = store.GetSetting("appearance.interfaceFont");
        _interfaceFont = InterfaceFonts.FirstOrDefault(font => font.Family == interfaceFont) ?? InterfaceFonts[0];
        _interfaceSizePercent = ReadSize("appearance.interfaceSize");
        _focusSizePercent = ReadSize("appearance.focusSize");
        ApplyInterfaceFont();
        EnglishFonts = Available([
            new("Segoe UI", "系统默认", "Make time for what matters."),
            new("Calibri", "Calibri", "Make time for what matters."),
            new("Arial", "Arial", "Make time for what matters."),
            new("Georgia", "Georgia", "Make time for what matters."),
            new("Cambria", "Cambria", "Make time for what matters."),
            new("Verdana", "Verdana", "Make time for what matters.")
        ]);
        TimerFonts = Available([
            new("Segoe UI", "简洁 · Segoe UI", "00:25:00"),
            new("Bahnschrift", "工业机械 · Bahnschrift", "00:25:00"),
            new("OCR A Extended", "机械刻字 · OCR A", "00:25:00"),
            new("Agency FB", "窄体仪表 · Agency FB", "00:25:00"),
            new("Consolas", "终端等宽 · Consolas", "00:25:00"),
            new("Lucida Console", "控制台 · Lucida Console", "00:25:00"),
            new("Courier New", "打字机 · Courier New", "00:25:00"),
            new("Arial", "清晰 · Arial", "00:25:00")
        ]);
        var saved = store.GetSetting("appearance.fonts");
        var preferences = saved is null ? null : JsonSerializer.Deserialize<FontPreferences>(saved);
        _chinese = ChineseFonts.FirstOrDefault(font => font.Family == preferences?.Chinese) ?? ChineseFonts[0];
        _english = EnglishFonts.FirstOrDefault(font => font.Family == preferences?.English) ?? EnglishFonts[0];
        _timer = TimerFonts.FirstOrDefault(font => font.Family == preferences?.Timer) ?? TimerFonts[0];
        ResetCommand = new RelayCommand(Reset);
        Apply();
        ApplySizes();
    }

    public IReadOnlyList<FontChoice> ChineseFonts { get; }
    public IReadOnlyList<FontChoice> EnglishFonts { get; }
    public IReadOnlyList<FontChoice> TimerFonts { get; }
    public IReadOnlyList<FontChoice> InterfaceFonts { get; }
    public RelayCommand ResetCommand { get; }
    public FontChoice InterfaceFont
    {
        get => _interfaceFont;
        set
        {
            if (value is null || value == _interfaceFont) return;
            _interfaceFont = value;
            ApplyInterfaceFont();
            Notify();
            try { _store.SaveSetting("appearance.interfaceFont", value.Family); }
            catch (Exception error) { _report($"软件字体保存失败：{error.Message}"); }
        }
    }
    public FontChoice Chinese { get => _chinese; set { if (value is null || value == _chinese) return; _chinese = value; Notify(); Save(); } }
    public FontChoice English { get => _english; set { if (value is null || value == _english) return; _english = value; Notify(); Save(); } }
    public FontChoice Timer { get => _timer; set { if (value is null || value == _timer) return; _timer = value; Notify(); Save(); } }
    public FontFamily TextTypeface { get; private set; } = new("Segoe UI, Microsoft YaHei");
    public double InterfaceSizePercent
    {
        get => _interfaceSizePercent;
        set => SetSize(ref _interfaceSizePercent, value, "appearance.interfaceSize", nameof(InterfaceSizePercent));
    }
    public double FocusSizePercent
    {
        get => _focusSizePercent;
        set => SetSize(ref _focusSizePercent, value, "appearance.focusSize", nameof(FocusSizePercent));
    }

    private double ReadSize(string key) =>
        double.TryParse(_store.GetSetting(key), CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 80, 120) : 100;

    private void SetSize(ref double field, double value, string key, string property)
    {
        value = Math.Clamp(value, 80, 120);
        if (field == value) return;
        field = value;
        ApplySizes();
        Notify(property);
        try { _store.SaveSetting(key, value.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception error) { _report($"字号保存失败：{error.Message}"); }
    }

    private void ApplySizes()
    {
        if (Application.Current is not { } app) return;
        var ui = InterfaceSizePercent / 100;
        var focus = FocusSizePercent / 100;
        app.Resources["InterfaceTextSize"] = 14 * ui;
        app.Resources["InterfaceCaptionSize"] = 12 * ui;
        app.Resources["InterfaceNavigationSize"] = 13 * ui;
        app.Resources["InterfaceTitleSize"] = 22 * ui;
        app.Resources["InterfaceSectionSize"] = 15 * ui;
        app.Resources["FocusTimerSize"] = 72 * focus;
        app.Resources["FullscreenTimerSize"] = 138 * focus;
        app.Resources["FocusHeadingSize"] = 21 * focus;
        app.Resources["FocusQuoteSize"] = 18 * focus;
        app.Resources["FullscreenFocusTextSize"] = 26 * focus;
    }

    private void ApplyInterfaceFont()
    {
        if (Application.Current is { } app) app.Resources["InterfaceFont"] = InterfaceFont.Typeface;
    }

    private void Apply()
    {
        TextTypeface = new FontFamily($"{English.Family}, {Chinese.Family}");
        Notify(nameof(TextTypeface));
        if (Application.Current is null) return;
        Application.Current.Resources["FocusTextFont"] = TextTypeface;
        Application.Current.Resources["FocusQuoteFont"] = TextTypeface;
        Application.Current.Resources["FocusTimerFont"] = Timer.Typeface;
    }
    private void Save()
    {
        Apply();
        try { _store.SaveSetting("appearance.fonts", JsonSerializer.Serialize(new FontPreferences(Chinese.Family, English.Family, Timer.Family))); }
        catch (Exception error) { _report($"字体偏好保存失败：{error.Message}"); }
    }
    private void Reset()
    {
        _chinese = ChineseFonts[0]; _english = EnglishFonts[0]; _timer = TimerFonts[0];
        Notify(nameof(Chinese)); Notify(nameof(English)); Notify(nameof(Timer)); Save();
    }
}
