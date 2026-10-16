using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private StatisticsSnapshot? _focusSaved;
    private (int Version, string Period, Guid? Project, DateOnly Day) _focusKey;
    private TextBlock? _focusValue, _focusHint;
    private void DrawFocus()
    {
        var today = DateOnly.FromDateTime(DateTime.Today); var key = (_manager.Data.Version, Option.Period, Option.ProjectId, today);
        if (_focusSaved is null || key != _focusKey)
        {
            var from = Option.Period switch { "周" => CalendarRules.Monday(today), "月" => new DateOnly(today.Year, today.Month, 1), "年" => new DateOnly(today.Year, 1, 1), _ => today };
            var to = Option.Period switch { "周" => from.AddDays(6), "月" => new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)), "年" => new DateOnly(today.Year, 12, 31), _ => today };
            _focusSaved = FocusStatistics.Build(_manager.Data.Records, new(from, to, Option.ProjectId is { } id ? new System.Collections.Generic.HashSet<Guid> { id } : null)); _focusKey = key;
        }
        var period = new ComboBox { ItemsSource = new[] { "日", "周", "月", "年" }, SelectedItem = Option.Period, Width = 57 }; period.SelectionChanged += (_, _) => { if (period.SelectedItem is string value) Option.Period = value; }; WidgetToolbar.Children.Add(period);
        var scope = Option.ProjectId is { } project ? _store.GetProjects(true).FirstOrDefault(item => item.Id == project)?.Name ?? "历史项目" : "全部项目";
        WidgetBody.Children.Add(Text((Option.Period switch { "日" => "今日", "周" => "本周", "月" => "本月", _ => "今年" }) + " · " + scope, 11, true));
        _focusValue = Text("", Option.View == "数字" ? 40 : 28); _focusValue.SetResourceReference(TextBlock.FontFamilyProperty, "FocusTimerFont"); _focusValue.Margin = new Thickness(0, 8, 0, 5); WidgetBody.Children.Add(_focusValue);
        _focusHint = Text("", 10, true); WidgetBody.Children.Add(_focusHint); UpdateFocus();
        if (Option.View == "数字") return;
        var current = _main.WidgetCurrentRecord;
        var live = LiveFocusStatistics.Build(_focusSaved, current);
        var slices = current is null || _focusSaved.Sessions.Any(item => item.Record.Id == current.Id) ? _focusSaved.Slices : _focusSaved.Slices.Concat(FocusStatistics.Build(new[] { current }, _focusSaved.Filter).Slices).ToArray();
        var combined = _focusSaved with { Duration = live.Duration, Days = live.Days, Slices = slices };
        var buckets = FocusStatistics.Trend(combined, Option.Period == "日", Option.Period == "年");
        var max = Math.Max(1, buckets.Max(item => item.Duration.TotalMinutes));
        if (Option.View == "热力图")
        {
            var grid = new UniformGrid { Columns = Option.Period == "日" ? 6 : Option.Period == "年" ? 4 : 7, Margin = new Thickness(0, 10, 0, 0) };
            foreach (var bucket in buckets)
            {
                var label = Text(bucket.Label, 9); label.TextAlignment = TextAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center;
                var cell = new Border { Child = label, Height = 26, Margin = new Thickness(2), CornerRadius = new CornerRadius(4), ToolTip = bucket.Label + " · " + FormatDuration(bucket.Duration) };
                cell.SetResourceReference(Border.BackgroundProperty, bucket.Duration > TimeSpan.Zero ? "AccentSoftBrush" : "SubtleBrush"); cell.Opacity = bucket.Duration > TimeSpan.Zero ? .45 + .55 * bucket.Duration.TotalMinutes / max : 1;
                grid.Children.Add(cell);
            }
            WidgetBody.Children.Add(grid); return;
        }
        var width = Math.Max(180, WidgetViewport.ActualWidth - 10); var chart = new Canvas { Width = width, Height = 118, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left }; var column = width / buckets.Count;
        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index]; var height = Math.Max(2, bucket.Duration.TotalMinutes / max * 87);
            var bar = new Border { Width = Math.Max(2, column * .65), Height = height, CornerRadius = new CornerRadius(3), ToolTip = bucket.Label + " · " + FormatDuration(bucket.Duration) }; bar.SetResourceReference(Border.BackgroundProperty, bucket.Duration > TimeSpan.Zero ? "AccentBrush" : "SubtleBrush");
            Canvas.SetLeft(bar, index * column + column * .15); Canvas.SetTop(bar, 90 - height); chart.Children.Add(bar);
            if (index % Math.Max(1, buckets.Count / 5) == 0) { var label = Text(bucket.Label, 9, true); Canvas.SetLeft(label, index * column); Canvas.SetTop(label, 96); chart.Children.Add(label); }
        }
        WidgetBody.Children.Add(chart);
    }
    private void UpdateFocus()
    {
        if (_focusSaved is null || _focusValue is null || _focusHint is null) return;
        var current = LiveFocusStatistics.Build(_focusSaved, _main.WidgetCurrentRecord); _focusValue.Text = FormatDuration(current.Duration);
        _focusHint.Text = (current.IncludesCurrent ? "含本次尚未保存的计时 · " : "") + $"{_focusSaved.Count} 次已保存专注";
    }
    private static string FormatDuration(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
}
