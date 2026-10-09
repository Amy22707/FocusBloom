using System;
using System.Collections.Generic;
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
    private FontChoice _chinese;
    private FontChoice _english;
    private FontChoice _timer;
    private sealed record FontPreferences(string Chinese, string English, string Timer);

    public FocusTypographyViewModel(IFocusStore store, Action<string> report)
    {
        _store = store; _report = report;
        var installed = Fonts.SystemFontFamilies.SelectMany(font => font.FamilyNames.Values.Append(font.Source)).ToArray();
        FontChoice[] Available(FontChoice[] choices) => choices.Where(choice => installed.Contains(choice.Family, StringComparer.OrdinalIgnoreCase)).ToArray();
        ChineseFonts = Available([
            new("Microsoft YaHei", "微软雅黑", "执着于理想，纯粹于当下。"),
            new("DengXian", "等线", "执着于理想，纯粹于当下。"),
            new("SimSun", "宋体", "执着于理想，纯粹于当下。"),
            new("KaiTi", "楷体", "执着于理想，纯粹于当下。"),
            new("FangSong", "仿宋", "执着于理想，纯粹于当下。")
        ]);
        EnglishFonts = Available([
            new("Segoe UI", "Segoe UI", "Make time for what matters."),
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
    }

    public IReadOnlyList<FontChoice> ChineseFonts { get; }
    public IReadOnlyList<FontChoice> EnglishFonts { get; }
    public IReadOnlyList<FontChoice> TimerFonts { get; }
    public RelayCommand ResetCommand { get; }
    public FontChoice Chinese { get => _chinese; set { if (value is null || value == _chinese) return; _chinese = value; Notify(); Save(); } }
    public FontChoice English { get => _english; set { if (value is null || value == _english) return; _english = value; Notify(); Save(); } }
    public FontChoice Timer { get => _timer; set { if (value is null || value == _timer) return; _timer = value; Notify(); Save(); } }
    public FontFamily TextTypeface { get; private set; } = new("Segoe UI, Microsoft YaHei");

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
