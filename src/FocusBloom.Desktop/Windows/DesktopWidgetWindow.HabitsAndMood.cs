using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Reflection;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private void DrawHabits()
    {
        Tool("＋", () => new HabitEditorWindow(_main) { Owner = this }.ShowDialog());
        var rows = _manager.Data.Habits.Where(item => Option.Includes(item.Id)).ToArray(); var planned = rows.Where(item => item.CanCheck).ToArray();
        WidgetBody.Children.Add(Text($"今日已完成 {planned.Count(item => item.CheckedToday)} / {planned.Length}", 11, true));
        if (rows.Length == 0) { WidgetBody.Children.Add(Text("没有选中的习惯，可在设置中选择或点击＋创建。", 12, true)); return; }
        var list = Option.View == "平铺" ? (Panel)new UniformGrid { Columns = WidgetViewport.ActualWidth >= 360 ? 2 : 1, Margin = new Thickness(0, 8, 0, 0) } : new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var row in rows)
        {
            var body = new StackPanel(); var name = Text(row.Name, 13); name.FontWeight = FontWeights.SemiBold;
            var history = new Button { Content = name, Tag = row.Id, ToolTip = "查看历史或补打卡" }; history.SetResourceReference(StyleProperty, "CompletionDateButton"); history.Click += (_, _) => new HabitHistoryWindow(_main, row.Habit) { Owner = this }.ShowDialog(); body.Children.Add(history);
            body.Children.Add(Text(row.Streak, 10, true));
            var check = new Button { Content = row.CheckText, Tag = row.Id, IsEnabled = row.CanCheck, Padding = new Thickness(8, 5, 8, 5), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
            if (row.CheckedToday) check.SetResourceReference(StyleProperty, "PrimaryButton");
            check.Click += (_, _) => { if (row.CheckedToday && !ConfirmDeletionWindow.Ask(this, "撤销今天的打卡？")) return; _main.Goals.CheckIn(row.Id, _main.Goals.Today); }; body.Children.Add(check);
            var card = Card(body); card.Margin = new Thickness(0, 0, Option.View == "平铺" ? 7 : 0, 8); list.Children.Add(card);
        }
        WidgetBody.Children.Add(list);
    }
    private void DrawMood()
    {
        MonthTools(); MonthPicker(); var first = new DateOnly(_anchor.Year, _anchor.Month, 1); var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)); var today = DateOnly.FromDateTime(DateTime.Today);
        var entries = _manager.Data.Moods.Where(item => item.Date >= first && item.Date <= last).ToDictionary(item => item.Date);
        if (Option.View == "趋势")
        {
            var width = Math.Max(230, WidgetViewport.ActualWidth - 8); var chart = new Canvas { Width = width, Height = 154, HorizontalAlignment = HorizontalAlignment.Left };
            foreach (var mood in ReflectionRules.Moods) { var label = Text(mood.Name, 9, true); Canvas.SetTop(label, 118 - (mood.Value - 1) * 26); chart.Children.Add(label); }
            DailyReflection? previous = null;
            Point PointAt(DailyReflection item) => new(45 + (item.Date.Day - 1) * (width - 55) / Math.Max(1, last.Day - 1), 124 - (item.Mood!.Value - 1) * 26);
            foreach (var entry in entries.Values.Where(item => item.Mood is not null).OrderBy(item => item.Date))
            {
                var point = PointAt(entry); if (previous is not null && previous.Date.DayNumber + 1 == entry.Date.DayNumber) { var before = PointAt(previous); var line = new Line { X1 = before.X, Y1 = before.Y, X2 = point.X, Y2 = point.Y, StrokeThickness = 2 }; line.SetResourceReference(Shape.StrokeProperty, "AccentBrush"); chart.Children.Add(line); }
                var dot = new Ellipse { Width = 8, Height = 8 }; dot.SetResourceReference(Shape.FillProperty, "AccentBrush"); var button = new Button { Content = dot, Width = 16, Height = 16, Tag = entry.Date, ToolTip = entry.Date.ToString("M月d日") + " · " + ReflectionRules.Moods[entry.Mood!.Value - 1].Name }; button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.Click += (_, _) => new ReflectionEditorWindow(_main, entry.Date) { Owner = this }.ShowDialog(); Canvas.SetLeft(button, point.X - 8); Canvas.SetTop(button, point.Y - 8); chart.Children.Add(button); previous = entry;
            }
            if (previous is null) { var empty = Text("本月尚无心情记录", 11, true); Canvas.SetLeft(empty, 50); Canvas.SetTop(empty, 65); chart.Children.Add(empty); }
            var dateLabel = Text($"{first:M/d} — {last:M/d}", 10, true); Canvas.SetTop(dateLabel, 141); Canvas.SetLeft(dateLabel, 45); chart.Children.Add(dateLabel); WidgetBody.Children.Add(chart);
        }
        else
        {
            var start = CalendarRules.Monday(first); var count = (last.DayNumber - start.DayNumber + 7) / 7 * 7; var rows = count / 7; var grid = new UniformGrid { Columns = 7, Rows = rows }; var height = Math.Clamp((WidgetViewport.ActualHeight - 80) / rows - 4, 34, 44);
            var weekdays = new UniformGrid { Columns = 7, Height = 18 }; foreach (var day in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Text(day, 10, true); label.TextAlignment = TextAlignment.Center; weekdays.Children.Add(label); } WidgetBody.Children.Add(weekdays);
            for (var index = 0; index < count; index++)
            {
                var day = start.AddDays(index); var entry = entries.GetValueOrDefault(day); var choice = entry?.Mood is { } mood ? ReflectionRules.Moods[mood - 1] : null;
                var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var label = Text(day.Day.ToString(), 10); label.TextAlignment = TextAlignment.Center; body.Children.Add(label); var icon = Text(choice?.Emoji ?? (entry is null ? "" : "记"), choice is null ? 10 : 16); icon.FontFamily = new("Segoe UI Emoji, Microsoft YaHei UI"); icon.TextAlignment = TextAlignment.Center; body.Children.Add(icon);
                var button = new Button { Content = body, Tag = day, Height = height, Margin = new Thickness(2), IsEnabled = day <= today, Opacity = day.Month == first.Month ? 1 : .3, BorderThickness = new Thickness(day == today ? 1 : 0), ToolTip = $"{day:yyyy-MM-dd}" + (day > today ? " · 未来日期" : choice is null ? entry is null ? " · 点击记录" : " · 已写复盘" : " · " + choice.Name) }; button.SetResourceReference(StyleProperty, "CompletionDateButton"); if (choice is not null) button.Background = FocusBloom.Desktop.Views.ReflectionView.MoodFill(choice.Value); else button.SetResourceReference(BackgroundProperty, entry is null ? "SubtleBrush" : "AccentSoftBrush"); button.SetResourceReference(BorderBrushProperty, "AccentBrush");
                button.Click += (_, _) => new ReflectionEditorWindow(_main, day) { Owner = this }.ShowDialog(); grid.Children.Add(button);
            }
            WidgetBody.Children.Add(grid);
        }
        WidgetBody.Children.Add(Text($"本月记录 {entries.Count} 天 · 点击日期可记录或补记", 10, true));
    }
}
