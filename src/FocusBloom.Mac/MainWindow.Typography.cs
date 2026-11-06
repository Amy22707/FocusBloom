using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private double InterfaceScale => double.TryParse(_store.GetSetting("mac.interfaceScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? Math.Clamp(size, 0.8, 1.3) : 1;
    private double TimerScale => double.TryParse(_store.GetSetting("mac.timerScale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ? Math.Clamp(size, 0.6, 1.6) : 1;
    private void ApplyFont()
    {
        FontFamily = _store.GetSetting("mac.interfaceFont") is { Length: > 0 } font ? new FontFamily(font) : FontFamily.Default;
    }
    private Control TypographySettings()
    {
        var names = FontManager.Current.SystemFonts.Select(font => font.Name).Distinct().Order().ToArray();
        var fonts = new[] { T("系统默认", "System default") }.Concat(names).ToArray();
        var main = Choice(fonts, Math.Max(0, Array.IndexOf(names, _store.GetSetting("mac.interfaceFont")) + 1));
        var focus = Choice(fonts, Math.Max(0, Array.IndexOf(names, _store.GetSetting("mac.focusFont")) + 1));
        var timer = Choice(fonts, Math.Max(0, Array.IndexOf(names, _store.GetSetting("mac.timerFont")) + 1));
        var uiSize = new Slider { Minimum = 80, Maximum = 130, Value = InterfaceScale * 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 230 };
        var timerSize = new Slider { Minimum = 60, Maximum = 160, Value = TimerScale * 100, TickFrequency = 10, IsSnapToTickEnabled = true, Width = 230 };
        return Card(Label(T("字体与字号", "Fonts & Sizes"), 17, true),
            Field(T("软件字体", "Interface font"), main), Field(T("软件字号", "Interface size"), uiSize),
            Field(T("专注文字字体", "Focus text font"), focus), Field(T("计时数字字体", "Timer font"), timer), Field(T("计时字号", "Timer size"), timerSize),
            Button(T("应用", "Apply"), () =>
            {
                _store.SaveSetting("mac.interfaceFont", main.SelectedIndex > 0 ? names[main.SelectedIndex - 1] : "");
                _store.SaveSetting("mac.focusFont", focus.SelectedIndex > 0 ? names[focus.SelectedIndex - 1] : "");
                _store.SaveSetting("mac.timerFont", timer.SelectedIndex > 0 ? names[timer.SelectedIndex - 1] : "");
                _store.SaveSetting("mac.interfaceScale", (uiSize.Value / 100).ToString(CultureInfo.InvariantCulture));
                _store.SaveSetting("mac.timerScale", (timerSize.Value / 100).ToString(CultureInfo.InvariantCulture));
                ApplyFont(); Refresh();
            }));
    }
}
