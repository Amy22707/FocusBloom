using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private CalendarViewModel? _widgetCalendar;
    private (int Version, string View, DateOnly From, DateOnly To)? _calendarKey;
    private IReadOnlyList<CalendarEntry> _calendarEntries = Array.Empty<CalendarEntry>();
    private IReadOnlyList<CalendarEntry> WidgetEntries(DateOnly from, DateOnly to)
    {
        _widgetCalendar ??= new(_store, settingsPrefix: "widgets.calendar");
        if (_widgetCalendar.Mode != Option.View) _widgetCalendar.Mode = Option.View;
        var key = (_manager.Data.Version, Option.View, from, to);
        if (_calendarKey != key) { _calendarEntries = _widgetCalendar.Entries(from, to); _calendarKey = key; }
        return _calendarEntries;
    }
    private void MoveCalendar(int direction)
    {
        _anchor = Option.View switch { "日" => _anchor.AddDays(direction), "月" => _anchor.AddMonths(direction), "年" => _anchor.AddYears(direction), _ => _anchor.AddDays(direction * 7) }; Render();
    }
    private void DrawCalendar()
    {
        Tool("‹", () => MoveCalendar(-1)); Tool("今天", () => { _anchor = DateOnly.FromDateTime(DateTime.Today); Render(); }); Tool("›", () => MoveCalendar(1));
        MonthPicker();
        if (Option.View == "月")
        {
            var first = new DateOnly(_anchor.Year, _anchor.Month, 1); var start = CalendarRules.Monday(first); var entries = WidgetEntries(start, start.AddDays(41));
            WidgetBody.Children.Add(CalendarMonth(first, entries, false));
            var heading = Text($"{_anchor:M月d日} · {entries.Count(item => item.Date == _anchor)} 项", 12, true); heading.Margin = new Thickness(0, 10, 0, 7); WidgetBody.Children.Add(heading);
            CalendarAgenda(WidgetBody, entries.Where(item => item.Date == _anchor)); return;
        }
        if (Option.View == "年")
        {
            var entries = WidgetEntries(new(_anchor.Year, 1, 1), new(_anchor.Year, 12, 31)); var grid = new UniformGrid { Columns = WidgetViewport.ActualWidth >= 550 ? 3 : 2 };
            for (var month = 1; month <= 12; month++)
            {
                var first = new DateOnly(_anchor.Year, month, 1); var body = new StackPanel();
                var heading = new Button { Content = $"{month}月", Style = (Style)FindResource("WidgetTool"), HorizontalAlignment = HorizontalAlignment.Left }; heading.Click += (_, _) => { _anchor = first; Option.View = "月"; }; body.Children.Add(heading);
                body.Children.Add(CalendarMonth(first, entries, true)); var card = Card(body, 6); card.Margin = new Thickness(0, 0, 6, 6); grid.Children.Add(card);
            }
            WidgetBody.Children.Add(grid); return;
        }
        if (Option.View == "日")
        {
            var entries = WidgetEntries(_anchor, _anchor); var heading = Text($"{_anchor:yyyy年M月d日} · {entries.Count} 项", 13); heading.Margin = new Thickness(0, 0, 0, 8); WidgetBody.Children.Add(heading);
            CalendarAgenda(WidgetBody, entries); return;
        }
        var monday = CalendarRules.Monday(_anchor); var weekEntries = WidgetEntries(monday, monday.AddDays(6));
        var caption = $"{monday:M月d日} — {monday.AddDays(6):M月d日}";
        if (Option.View == "课程表")
        {
            var term = _store.GetSchoolTerm(); var week = CalendarRules.TeachingWeek(term, _anchor); caption += week >= 1 && week <= term.Weeks ? $" · 第{week}教学周" : " · 非教学周";
        }
        var label = Text(caption, 12, true); label.Margin = new Thickness(0, 0, 0, 8); WidgetBody.Children.Add(label);
        var days = new UniformGrid { Columns = 7, MinWidth = 630 };
        for (var index = 0; index < 7; index++)
        {
            var day = monday.AddDays(index); var body = new StackPanel { Margin = new Thickness(3) };
            var heading = Text($"{new[] { "一", "二", "三", "四", "五", "六", "日" }[index]}\n{day:M/d}", 11); heading.TextAlignment = TextAlignment.Center; heading.Margin = new Thickness(0, 0, 0, 8); body.Children.Add(heading);
            CalendarAgenda(body, weekEntries.Where(item => item.Date == day)); days.Children.Add(body);
        }
        WidgetBody.Children.Add(new ScrollViewer { Content = days, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }
    private UniformGrid CalendarMonth(DateOnly first, IReadOnlyList<CalendarEntry> entries, bool compact)
    {
        var start = CalendarRules.Monday(first); var daysWithEntries = entries.Select(item => item.Date).ToHashSet(); var grid = new UniformGrid { Columns = 7 };
        foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Text(name, 9, true); label.TextAlignment = TextAlignment.Center; grid.Children.Add(label); }
        for (var index = 0; index < 42; index++)
        {
            var day = start.AddDays(index); var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Text(day.Day.ToString(), compact ? 9 : 11); number.TextAlignment = TextAlignment.Center; body.Children.Add(number);
            if (!compact) { var marker = Text(daysWithEntries.Contains(day) ? "●" : "", 8, true); marker.TextAlignment = TextAlignment.Center; marker.Height = 11; body.Children.Add(marker); }
            var button = new Button { Content = body, Padding = new Thickness(0), MinHeight = compact ? 20 : 34, Margin = new Thickness(1), Tag = day, ToolTip = day.ToString("yyyy-MM-dd") + string.Concat(entries.Where(item => item.Date == day).Select(item => "\n" + item.Title)), Opacity = day.Month == first.Month ? 1 : .25 };
            button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.SetResourceReference(BackgroundProperty, day == _anchor && !compact ? "SelectedBrush" : daysWithEntries.Contains(day) ? "AccentSoftBrush" : "SubtleBrush");
            button.Click += (_, _) => { _anchor = day; if (compact) Option.View = "日"; else Render(); }; grid.Children.Add(button);
        }
        return grid;
    }
    private void CalendarAgenda(Panel panel, IEnumerable<CalendarEntry> source)
    {
        var entries = source.ToArray();
        if (entries.Length == 0) { var empty = Text("暂无事项", 10, true); empty.Margin = new Thickness(2, 7, 0, 7); panel.Children.Add(empty); return; }
        foreach (var entry in entries)
        {
            var body = new StackPanel(); var time = entry.Start is { } at ? at.ToString("HH:mm") + (entry.End is { } until ? "—" + until.ToString("HH:mm") : "") : "全天"; body.Children.Add(Text(time, 10, true));
            var title = Text(entry.Title, 12); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 4, 0, 4); body.Children.Add(title);
            if (entry.Detail.Length > 0) body.Children.Add(Text(entry.Detail, 10, true));
            var open = new Button { Content = body, Tag = entry, ToolTip = entry.Title + "\n" + time + "\n" + entry.Detail }; open.SetResourceReference(StyleProperty, "CompletionDateButton"); open.Click += (_, _) => _manager.OpenCalendarEntry(entry);
            var card = Card(open, 7); if (entry.Kind == "课程" && ColorConverter.ConvertFromString(entry.Color) is Color color) card.BorderBrush = new SolidColorBrush(color);
            if (entry.Completed) card.Opacity = .55; panel.Children.Add(card);
        }
    }
}
