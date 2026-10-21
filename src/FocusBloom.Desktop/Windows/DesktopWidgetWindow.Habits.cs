using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private void DrawHabits()
    {
        Tool("‹", () => MoveHabitPeriod(-1)); Tool("今天", () => { _anchor = _main.Goals.Today; Render(); }); Tool("›", () => MoveHabitPeriod(1));
        Tool("＋", () => new HabitEditorWindow(_main) { Owner = this }.ShowDialog()); MonthPicker();
        var habits = _manager.Data.Habits.Where(row => Option.Includes(row.Id)).Select(row => row.Habit).ToArray();
        if (habits.Length == 0) { WidgetBody.Children.Add(Text("没有选中的习惯，可在设置中选择或点击＋创建。", 12, true)); return; }
        if (Option.View is "周" or "月" or "年") { DrawHabitCompletion(habits); return; }
        var rows = habits.Select(habit => HabitDay(habit, _anchor)).ToArray(); var planned = rows.Where(row => row.Planned || row.Checked).ToArray(); var done = planned.Count(row => row.Checked);
        var caption = _anchor == _main.Goals.Today ? "今天" : _anchor == _main.Goals.Today.AddDays(-1) ? "昨天" : _anchor.ToString("M月d日");
        WidgetBody.Children.Add(Text(_anchor > _main.Goals.Today ? $"{caption} · 计划 {planned.Length} 项习惯" : $"{caption} · 已完成 {done} / {planned.Length}", 12));
        if (planned.Length > 0 && _anchor <= _main.Goals.Today)
        {
            var progress = new ProgressBar { Maximum = planned.Length, Value = done, Height = 4, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 6) }; progress.SetResourceReference(ForegroundProperty, "AccentBrush"); progress.SetResourceReference(BackgroundProperty, "SubtleBrush"); WidgetBody.Children.Add(progress);
            WidgetBody.Children.Add(Text(done == planned.Length ? "这一天的小事，都认真完成了。" : done == 0 ? "从最容易的一件开始。" : "按自己的节奏继续。", 10, true));
        }
        Panel list = Option.View == "平铺" ? new UniformGrid { Columns = WidgetViewport.ActualWidth >= 400 ? 2 : 1 } : new StackPanel(); list.Margin = new Thickness(0, 10, 0, 0);
        foreach (var row in rows) list.Children.Add(HabitDayCard(row, Option.View == "日"));
        WidgetBody.Children.Add(list);
    }
    private void MoveHabitPeriod(int direction)
    {
        _anchor = Option.View switch { "周" => _anchor.AddDays(direction * 7), "月" => _anchor.AddMonths(direction), "年" => _anchor.AddYears(direction), _ => _anchor.AddDays(direction) }; Render();
    }
    private HabitDayRow HabitDay(Habit habit, DateOnly day)
    {
        var checks = _manager.Data.CheckIns;
        return new(habit, day, checks.Any(check => check.HabitId == habit.Id && check.Date == day), GoalRules.Due(habit, day), day > _main.Goals.Today,
            checks.Count(check => check.HabitId == habit.Id && check.Date <= day && check.Date <= _main.Goals.Today), GoalRules.HabitStatus(habit, checks, day > _main.Goals.Today ? _main.Goals.Today : day).Streak);
    }
    private void CheckWidgetHabit(HabitDayRow row)
    {
        if (!row.CanCheck || row.Checked && !ConfirmDeletionWindow.Ask(this, $"撤销 {row.Date:M月d日}‘{row.Name}’的打卡？")) return;
        _main.Goals.CheckIn(row.Id, row.Date);
    }
    private Border HabitDayCard(HabitDayRow row, bool daily)
    {
        var root = new DockPanel(); var history = new Button { Content = "···", Padding = new Thickness(4, 0, 4, 0), ToolTip = "查看历史或补打卡", VerticalAlignment = VerticalAlignment.Top }; history.SetResourceReference(StyleProperty, "WidgetTool"); history.Click += (_, _) => new HabitHistoryWindow(_main, row.Habit, _anchor) { Owner = this }.ShowDialog(); DockPanel.SetDock(history, Dock.Right); root.Children.Add(history);
        if (daily)
        {
            var content = new Grid(); content.ColumnDefinitions.Add(new() { Width = new GridLength(46) }); content.ColumnDefinitions.Add(new()); content.ColumnDefinitions.Add(new() { Width = new GridLength(26) });
            var count = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Text(row.TotalDays.ToString(), 23); number.FontWeight = FontWeights.SemiBold; count.Children.Add(number); count.Children.Add(Text("累计天", 9, true)); content.Children.Add(count);
            var detail = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) }; var title = Text(row.Name, 13); title.FontWeight = FontWeights.SemiBold; title.TextWrapping = TextWrapping.NoWrap; title.TextTrimming = TextTrimming.CharacterEllipsis; detail.Children.Add(title); detail.Children.Add(Text(row.Schedule, 9, true)); var status = Text(row.Detail, 10); status.Margin = new Thickness(0, 4, 0, 0); detail.Children.Add(status); Grid.SetColumn(detail, 1); content.Children.Add(detail);
            var badge = new Border { Width = 25, Height = 25, CornerRadius = new CornerRadius(13), VerticalAlignment = VerticalAlignment.Center, Background = row.Checked ? new SolidColorBrush((Color)ColorConverter.ConvertFromString(row.Color)) : (Brush)FindResource("SubtleBrush") };
            var mark = Text(row.Checked ? "✓" : "＋", 16); mark.TextAlignment = TextAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center; if (row.Checked) mark.Foreground = new SolidColorBrush(Color.FromRgb(54, 69, 75)); badge.Child = mark; Grid.SetColumn(badge, 2); content.Children.Add(badge);
            var check = new Button { Name = "WidgetHabitCheckButton", Content = content, Tag = row.Id, DataContext = row, IsEnabled = row.CanCheck, ToolTip = row.Name + " · " + row.Date.ToString("yyyy-MM-dd") + "\n" + row.Detail }; check.SetResourceReference(StyleProperty, "CompletionDateButton"); check.Click += (_, _) => CheckWidgetHabit(row); root.Children.Add(check);
        }
        else
        {
            var content = new StackPanel(); var title = Text(row.Name, 13); title.FontWeight = FontWeights.SemiBold; content.Children.Add(title); content.Children.Add(Text($"累计 {row.TotalDays} 天 · 连续 {row.Streak} 个计划日", 10, true));
            var check = new Button { Name = "WidgetHabitCheckButton", Content = row.Checked ? "已打卡 ✓" : row.Date == _main.Goals.Today && row.CanCheck ? "今天打卡" : row.Detail, Tag = row.Id, DataContext = row, IsEnabled = row.CanCheck, Padding = new Thickness(8, 5, 8, 5), FontSize = 11, Margin = new Thickness(0, 8, 0, 0) }; check.Click += (_, _) => CheckWidgetHabit(row); content.Children.Add(check); root.Children.Add(content);
        }
        var card = Card(root, 14); card.CornerRadius = new CornerRadius(24); card.DataContext = row; card.MinHeight = daily ? 90 : 0; card.Margin = new Thickness(0, 0, Option.View == "平铺" ? 7 : 0, 8);
        card.Background = HabitFill(row.Checked ? 1 : 0, row.Color); return card;
    }
    private SolidColorBrush HabitFill(double ratio, string? hex = null)
    {
        var low = ((SolidColorBrush)FindResource("FieldBrush")).Color; var backdrop = ((SolidColorBrush)FindResource("CardBrush")).Color;
        var alpha = low.A / 255d; low = Color.FromRgb((byte)(low.R * alpha + backdrop.R * (1 - alpha)), (byte)(low.G * alpha + backdrop.G * (1 - alpha)), (byte)(low.B * alpha + backdrop.B * (1 - alpha)));
        var high = hex is null ? ((SolidColorBrush)FindResource("AccentBrush")).Color : (Color)ColorConverter.ConvertFromString(hex); var amount = ratio <= 0 ? 0 : .15 + .3 * ratio;
        return new(Color.FromRgb((byte)(low.R + (high.R - low.R) * amount), (byte)(low.G + (high.G - low.G) * amount), (byte)(low.B + (high.B - low.B) * amount)));
    }
    private void DrawHabitCompletion(Habit[] habits)
    {
        var today = _main.Goals.Today;
        var from = Option.View == "年" ? new DateOnly(_anchor.Year, 1, 1) : Option.View == "月" ? new DateOnly(_anchor.Year, _anchor.Month, 1) : CalendarRules.Monday(_anchor);
        var to = Option.View == "年" ? new DateOnly(_anchor.Year, 12, 31) : Option.View == "月" ? from.AddMonths(1).AddDays(-1) : from.AddDays(6);
        var days = GoalCompletion.Build(Array.Empty<FocusGoal>(), habits, Array.Empty<FocusRecord>(), _manager.Data.CheckIns, from, to, today);
        WidgetBody.Children.Add(Text(Option.View == "年" ? $"{from:yyyy年}" : Option.View == "月" ? $"{from:yyyy年M月}" : $"{from:M月d日} — {to:M月d日}", 12));
        var reached = days.Where(day => !day.Future && day.Entries.Count > 0).ToArray(); WidgetBody.Children.Add(Text(reached.Length == 0 ? "所选范围暂无已到期的计划" : $"平均完成度 {reached.Average(day => day.Ratio):P0} · 全部完成 {reached.Count(day => day.Ratio >= 1)} / {reached.Length} 天", 10, true));
        if (Option.View == "周")
        {
            var grid = new Grid { MinWidth = 420, Margin = new Thickness(0, 12, 0, 0) }; grid.ColumnDefinitions.Add(new() { Width = new GridLength(90) }); for (var index = 0; index < 7; index++) grid.ColumnDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
            for (var index = 0; index < 7; index++) { var label = Text("一二三四五六日"[index] + "\n" + from.AddDays(index).ToString("M/d"), 10, true); label.TextAlignment = TextAlignment.Center; Grid.SetColumn(label, index + 1); grid.Children.Add(label); }
            for (var index = 0; index < habits.Length; index++)
            {
                var habit = habits[index]; grid.RowDefinitions.Add(new() { Height = new GridLength(44) }); var label = Text(habit.Name, 12); label.VerticalAlignment = VerticalAlignment.Center; Grid.SetRow(label, index + 1); grid.Children.Add(label);
                for (var dayIndex = 0; dayIndex < 7; dayIndex++)
                {
                    var row = HabitDay(habit, from.AddDays(dayIndex)); var mark = Text(row.Checked ? "✓" : row.Planned ? "○" : "—", 14); mark.TextAlignment = TextAlignment.Center; mark.VerticalAlignment = VerticalAlignment.Center;
                    var button = new Button { Content = mark, Tag = (habit.Id, row.Date), DataContext = row, IsEnabled = row.CanCheck, Background = HabitFill(row.Checked ? 1 : 0, row.Color), Margin = new Thickness(2), ToolTip = habit.Name + " · " + row.Date.ToString("M/d") + "\n" + row.Detail }; button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.Click += (_, _) => CheckWidgetHabit(row); Grid.SetColumn(button, dayIndex + 1); Grid.SetRow(button, index + 1); grid.Children.Add(button);
                }
            }
            WidgetBody.Children.Add(new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }); return;
        }
        if (Option.View == "月")
        {
            var weekdays = new UniformGrid { Columns = 7, Height = 20, Margin = new Thickness(0, 10, 0, 0) }; foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Text(name, 10, true); label.TextAlignment = TextAlignment.Center; weekdays.Children.Add(label); } WidgetBody.Children.Add(weekdays);
            var grid = new UniformGrid { Columns = 7 };
            for (var index = 0; index < from.DayNumber - CalendarRules.Monday(from).DayNumber; index++) grid.Children.Add(new Border());
            foreach (var day in days) grid.Children.Add(HabitCompletionDay(day, habits, 40)); WidgetBody.Children.Add(grid); return;
        }
        var start = CalendarRules.Monday(from); var columns = (to.DayNumber - start.DayNumber) / 7 + 1; var cell = Math.Clamp((WidgetViewport.ActualWidth - 24) / columns, 5, 14); var canvas = new Canvas { Width = columns * cell + 20, Height = 24 + cell * 7, Margin = new Thickness(0, 14, 0, 6) };
        foreach (var day in days)
        {
            var column = (day.Date.DayNumber - start.DayNumber) / 7; var row = ((int)day.Date.DayOfWeek + 6) % 7; var button = HabitCompletionDay(day, habits, cell - 1); button.Content = null; button.Width = cell - 1; button.Margin = new Thickness(0); Canvas.SetLeft(button, 20 + column * cell); Canvas.SetTop(button, 20 + row * cell); canvas.Children.Add(button);
            if (day.Date.Day == 1) { var label = Text(day.Date.Month + "月", 9, true); Canvas.SetLeft(label, 20 + column * cell); canvas.Children.Add(label); }
        }
        WidgetBody.Children.Add(new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });
    }
    private Button HabitCompletionDay(CompletionDay day, Habit[] habits, double height)
    {
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Text(day.Date.Day.ToString(), 11); number.TextAlignment = TextAlignment.Center; body.Children.Add(number); var ratio = Text(day.Future ? "待执行" : day.Entries.Count == 0 ? "—" : day.Ratio.ToString("P0"), 9, true); ratio.TextAlignment = TextAlignment.Center; body.Children.Add(ratio);
        var button = new Button { Content = body, Tag = day, Height = height, Margin = new Thickness(2), Background = HabitFill(day.Future ? 0 : day.Ratio), BorderThickness = new Thickness(day.Date == _main.Goals.Today ? 1 : 0), ToolTip = day.Date.ToString("yyyy-MM-dd") + (day.Future ? " · 未来计划" : $" · 已完成 {day.Completed}/{day.Entries.Count}") }; button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.SetResourceReference(BorderBrushProperty, "AccentBrush");
        button.Click += (_, _) => { if (habits.Length == 1) CheckWidgetHabit(HabitDay(habits[0], day.Date)); else { _anchor = day.Date; Option.View = "日"; } }; return button;
    }
}
