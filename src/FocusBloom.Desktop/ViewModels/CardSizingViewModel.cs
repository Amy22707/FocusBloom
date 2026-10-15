using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class CardSizingViewModel : ObservableObject
{
    public IReadOnlyList<CardSizeOption> Options { get; }
    private CardSizeOption _previewOption = null!;
    public CardSizeOption PreviewOption { get => _previewOption; set { if (_previewOption == value) return; _previewOption = value; Notify(); } }
    public CardSizingViewModel(IFocusStore store, Action refreshCalendar)
    {
        Options = new[] {
            new CardSizeOption(store, "Project", "项目库", 3),
            new CardSizeOption(store, "Todo", "待办列表", 3),
            new CardSizeOption(store, "Quadrant", "四象限", 1),
            new CardSizeOption(store, "Calendar", "日历时间轴", 1, refreshCalendar),
            new CardSizeOption(store, "FocusCalendar", "专注记录", 1, refreshCalendar),
            new CardSizeOption(store, "Timetable", "课程表", 1, refreshCalendar),
            new CardSizeOption(store, "DateEvent", "日期事项", 2),
            new CardSizeOption(store, "Media", "背景、文字与声音", 3),
            new CardSizeOption(store, "Goals", "目标与习惯", 3)
        };
        PreviewOption = Options[0];
    }
}

public sealed class CardSizeOption : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly string _key;
    private readonly int _columns;
    private readonly Action? _refresh;
    private double _percentage;
    public string Name { get; }
    public string PercentageText => $"{Percentage:0}%";
    public ScaleTransform PreviewTransform => new(Percentage / 100, Percentage / 100);
    public double PreviewHeight => _key switch { "Quadrant" => 32, "DateEvent" => 150, "Media" => 180, "Calendar" => 80, "Timetable" => 86, _ => 120 };
    public double PreviewWidth => _key switch { "Quadrant" => 240, "DateEvent" => 240, "Media" => 180, "FocusCalendar" => 240, _ => 220 };
    public Thickness PreviewPadding => _key == "Quadrant" ? new(8, 0, 8, 0) : new(12);
    public string PreviewTitle => _key switch { "Project" => "示例项目", "Todo" or "Quadrant" => "完成课程实验", "DateEvent" => "期末考试", "Media" => "湖光与山影", "Timetable" => "示例课程", _ => "复习课程笔记" };
    public string PreviewDetail => _key switch { "Project" => "学习", "Todo" => "截止 10月22日 · 待办", "DateEvent" => "还有 30 天", "Media" => "默认 · 背景素材", "Timetable" => "第1—2节 · 教室101", _ => "14:00—15:00 · 1小时" };
    public bool ShowPreviewDetail => _key != "Quadrant";
    public string PreviewDescription => _columns == 1 || _key == "Media" ? "示例随滑块实时缩放" : $"每行 {ColumnsForPreview} 个 · 示例随滑块实时缩放";
    private int ColumnsForPreview => Percentage <= 80 ? _columns + 1 : Percentage >= 120 ? _columns - 1 : _columns;
    public RelayCommand ResetCommand { get; }
    public double Percentage
    {
        get => _percentage;
        set
        {
            value = Math.Clamp(Math.Round(value / 5) * 5, 70, 140);
            if (_percentage == value) return;
            _percentage = value;
            _store.SaveSetting("ui.cardScale." + _key, value.ToString(CultureInfo.InvariantCulture));
            Apply(); Notify(); Notify(nameof(PercentageText)); Notify(nameof(PreviewTransform)); Notify(nameof(PreviewDescription)); _refresh?.Invoke();
        }
    }
    public CardSizeOption(IFocusStore store, string key, string name, int columns, Action? refresh = null)
    {
        _store = store; _key = key; Name = name; _columns = columns; _refresh = refresh;
        _percentage = double.TryParse(store.GetSetting("ui.cardScale." + key), CultureInfo.InvariantCulture, out var saved) ? Math.Clamp(saved, 70, 140) : 100;
        ResetCommand = new RelayCommand(() => Percentage = 100);
        Apply();
    }
    private void Apply()
    {
        if (Application.Current is not { } app) return;
        var factor = Percentage / 100;
        var transform = new ScaleTransform(factor, factor); transform.Freeze();
        app.Resources[_key + "CardTransform"] = transform;
        app.Resources[_key + "CardScale"] = factor;
        app.Resources[_key + "CardHeight"] = 120 * factor;
        app.Resources[_key + "CardColumns"] = _columns == 1 ? 1 : ColumnsForPreview;
    }
}
