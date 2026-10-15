using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using Microsoft.Win32;
using System.IO;

namespace FocusBloom.Desktop.Views;

public partial class CalendarView : UserControl
{
    private static readonly string[] Weekdays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
    private CalendarViewModel? _model;
    private Point _press;
    private bool _pressed;
    private bool _rendering;
    private string? _lastMode;
    private double _initialOffset;
    public event Action<CalendarEntry>? EntryRequested;
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public CalendarView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_model is not null) _model.Changed -= ModelChanged;
            _model = DataContext as CalendarViewModel;
            if (_model is not null) { _model.Changed += ModelChanged; JumpDate.SelectedDate = _model.Date.ToDateTime(TimeOnly.MinValue); }
            Render();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
    }
    private void ModelChanged(object? sender, EventArgs e) { Render(); if (_model is not null) JumpDate.SelectedDate = _model.Date.ToDateTime(TimeOnly.MinValue); }
    private void Previous_Click(object sender, RoutedEventArgs e) => _model?.Move(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => _model?.Move(1);
    private void Today_Click(object sender, RoutedEventArgs e) { if (_model is not null) _model.Date = DateOnly.FromDateTime(DateTime.Today); }
    private void JumpDate_Changed(object sender, SelectionChangedEventArgs e) { if (_model is not null && JumpDate.SelectedDate is { } date && DateOnly.FromDateTime(date) != _model.Date) _model.Date = DateOnly.FromDateTime(date); }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 1 || _model?.Mode == "月") Render(); }
    private void Viewport_MouseDown(object sender, MouseButtonEventArgs e) { _press = e.GetPosition(CalendarViewport); _pressed = true; }
    private void Viewport_MouseUp(object sender, MouseButtonEventArgs e)
    { if (!_pressed) return; _pressed = false; var delta = e.GetPosition(CalendarViewport) - _press; if (Math.Abs(delta.X) > 80 && Math.Abs(delta.X) > Math.Abs(delta.Y) * 1.5) { _model?.Move(delta.X < 0 ? 1 : -1); e.Handled = true; } }
    private void Viewport_MouseWheel(object sender, MouseWheelEventArgs e) { if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { _model?.Move(e.Delta < 0 ? 1 : -1); e.Handled = true; } }
    private void Viewport_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    { var delta = e.TotalManipulation.Translation; if (Math.Abs(delta.X) > 80 && Math.Abs(delta.X) > Math.Abs(delta.Y)) { _model?.Move(delta.X < 0 ? 1 : -1); e.Handled = true; } }
    private void NewPlan_Click(object sender, RoutedEventArgs e) { if (_model is not null) new CalendarPlanWindow(Main, _model.Date, focusOnly: _model.FocusOnly) { Owner = Window.GetWindow(this) }.ShowDialog(); }
    private void Courses_Click(object sender, RoutedEventArgs e) => new CourseManagerWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void Reminders_Click(object sender, RoutedEventArgs e) => new ReminderInboxWindow(Main, entry => EntryRequested?.Invoke(entry)) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_model is null) return;
        var (from, to) = Range(); var dialog = new SaveFileDialog { Filter = "iCalendar|*.ics", FileName = $"FocusBloom-{from:yyyyMMdd}.ics" };
        var dates = _model.Store.GetDateEvents().ToDictionary(item => item.Id); var plans = _model.Store.GetCalendarPlans().ToDictionary(item => item.Id); var courses = _model.Store.GetCourses().ToDictionary(item => item.Id);
        ReminderOptions? Options(CalendarEntry entry) => entry.Kind switch { "待办" => entry.Completed ? new(false) : _model.Store.GetReminderOptions("待办", entry.Id), "日期事项" => dates.GetValueOrDefault(entry.Id)?.Reminder ?? new(), "安排" => plans.GetValueOrDefault(entry.Id)?.Reminder ?? new(false), "课程" => courses.GetValueOrDefault(entry.Id)?.Reminder ?? new(true, 10), _ => null };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        { try { File.WriteAllText(dialog.FileName, IcsExporter.Entries(_model.Entries(from, to).Where(entry => entry.Kind != "专注"), Options), new System.Text.UTF8Encoding(false)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), "导出失败：" + ex.Message); } }
    }
    private (DateOnly From, DateOnly To) Range()
    {
        var date = _model!.Date;
        return _model.Mode switch { "日" or "时间轴" => (date, date), "月" => (new(date.Year, date.Month, 1), new(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month))), "年" => (new(date.Year, 1, 1), new(date.Year, 12, 31)), _ => (CalendarRules.Monday(date), CalendarRules.Monday(date).AddDays(6)) };
    }
    public void Render()
    {
        if (_rendering || _model is null || CalendarViewport.ActualWidth < 100) return;
        _rendering = true;
        try
        {
            var reset = _lastMode != _model.Mode; CalendarContent.Children.Clear(); _initialOffset = 0;
            if (_model.Mode == "时间轴") DrawRecordTimeline();
            else if (_model.Mode == "月") DrawMonth(_model.Date);
            else if (_model.Mode == "年") DrawYear();
            else if (_model.Mode == "日" && _model.DayLayout == "双列") DrawDoubleDay();
            else DrawTimeline(_model.Mode == "日" ? _model.Date : CalendarRules.Monday(_model.Date), _model.Mode == "日" ? 1 : 7, _model.Mode == "课程表");
            _lastMode = _model.Mode;
            if (reset) CalendarViewport.ScrollToVerticalOffset(_initialOffset);
        }
        finally { _rendering = false; }
    }
    private TextBlock Label(string text, double size = 11, bool muted = false)
    { var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return label; }
    private Border Cell(FrameworkElement content, Thickness? margin = null)
    { var border = new Border { Child = content, BorderThickness = new Thickness(0, 0, 1, 1), Padding = new Thickness(7), Margin = margin ?? new Thickness(0) }; border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return border; }
    private Button EntryButton(CalendarEntry entry, bool compact)
    {
        var factor = (double)(TryFindResource(_model?.Mode == "课程表" ? "TimetableCardScale" : _model?.FocusOnly == true ? "FocusCalendarCardScale" : "CalendarCardScale") ?? 1d);
        var panel = new StackPanel(); var heading = Label((compact ? "" : entry.Kind + " · ") + entry.Title, (compact ? 10 : 11) * factor); heading.FontWeight = FontWeights.SemiBold;
        heading.TextTrimming = TextTrimming.CharacterEllipsis;
        heading.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; heading.LineHeight = (compact ? 12 : 14) * factor;
        heading.MaxHeight = heading.LineHeight * 2; panel.Children.Add(heading);
        if (!compact)
        {
            var time = entry.Start is { } start ? $"{start:HH:mm}" + (entry.End is { } end ? "—" + (entry.EndMinutes >= 1440 ? "24:00" : $"{end:HH:mm}") : "") : "全天";
            var detail = Label(time + (entry.Detail.Length > 0 ? " · " + entry.Detail : ""), 10 * factor, true);
            detail.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; detail.LineHeight = 13 * factor;
            detail.MaxHeight = detail.LineHeight * (entry.Kind == "课程" ? 3 : 2);
            detail.TextTrimming = TextTrimming.CharacterEllipsis; panel.Children.Add(detail);
        }
        var button = new Button { Content = panel, Padding = new Thickness(7 * factor, 5 * factor, 7 * factor, 5 * factor), HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = $"{entry.Kind} · {entry.Title}\n{entry.Date:yyyy-MM-dd} {entry.Start?.ToString("HH:mm")}\n{entry.Detail}", Margin = new Thickness(2), Tag = entry };
        button.SetResourceReference(StyleProperty, "CalendarEntryButton");
        button.SetResourceReference(Control.BackgroundProperty, "FieldBrush");
        if (entry.Kind == "课程" && ColorConverter.ConvertFromString(entry.Color) is Color color) button.Background = new SolidColorBrush(Color.FromArgb(65, color.R, color.G, color.B));
        if (entry.Completed) button.Opacity = 0.55;
        button.Click += (_, _) => EntryRequested?.Invoke(entry); return button;
    }
    private void DrawMonth(DateOnly date)
    {
        var first = new DateOnly(date.Year, date.Month, 1); var from = CalendarRules.Monday(first);
        var entries = _model!.Entries(from, from.AddDays(41)).GroupBy(entry => entry.Date).ToDictionary(group => group.Key, group => group.ToArray());
        var weekdays = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 8) }; foreach (var name in Weekdays) weekdays.Children.Add(Label(name, 11, true)); CalendarContent.Children.Add(weekdays);
        var grid = new UniformGrid { Columns = 7, Rows = 6, Height = Math.Max(240, CalendarViewport.ActualHeight - 24) };
        for (var index = 0; index < 42; index++)
        {
            var day = from.AddDays(index); var panel = new StackPanel();
            var daily = entries.GetValueOrDefault(day) ?? Array.Empty<CalendarEntry>();
            var heading = new DockPanel();
            if (daily.Length > 1) { var counter = Label($"{daily.Length}项", 9, true); counter.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(counter, Dock.Right); heading.Children.Add(counter); }
            var number = new Button { Content = day.Day.ToString(), Padding = new Thickness(3, 0, 3, 0), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            if (day == DateOnly.FromDateTime(DateTime.Today)) number.SetResourceReference(Control.BackgroundProperty, "SelectedBrush");
            if (day.Month != date.Month) number.Opacity = 0.45;
            number.Click += (_, _) => { _model.Date = day; _model.Mode = _model.FocusOnly ? "时间轴" : "日"; }; heading.Children.Add(number); panel.Children.Add(heading);
            if (daily.Length > 0)
            {
                var item = EntryButton(daily[0], true); item.Padding = new Thickness(3, 0, 3, 0); item.Margin = new Thickness(0, 2, 0, 0);
                if (item.Content is StackPanel content && content.Children[0] is TextBlock text) text.TextWrapping = TextWrapping.NoWrap; panel.Children.Add(item);
            }
            var cell = Cell(panel); cell.Padding = new Thickness(3); cell.ClipToBounds = true; grid.Children.Add(cell);
        }
        CalendarContent.Children.Add(grid);
    }
    private void DrawYear()
    {
        var year = _model!.Date.Year; var entries = _model.Entries(new(year, 1, 1), new(year, 12, 31));
        var dates = entries.Select(entry => entry.Date).ToHashSet(); var grid = new UniformGrid { Columns = CalendarViewport.ActualWidth >= 760 ? 4 : 3 };
        for (var month = 1; month <= 12; month++)
        {
            var first = new DateOnly(year, month, 1); var panel = new StackPanel();
            var heading = new Button { Content = $"{month}月", Padding = new Thickness(6, 4, 6, 4), HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent };
            heading.Click += (_, _) => { _model.Date = first; _model.Mode = "月"; }; panel.Children.Add(heading);
            var days = new UniformGrid { Columns = 7, Rows = 7, Height = 126 }; foreach (var name in Weekdays) days.Children.Add(Label(name[^1..], 9, true));
            var begin = CalendarRules.Monday(first);
            for (var index = 0; index < 42; index++)
            {
                var day = begin.AddDays(index); var button = new Button { Content = day.Month == month ? day.Day.ToString() : "", FontSize = 10, Padding = new Thickness(0), Background = Brushes.Transparent, IsEnabled = day.Month == month };
                if (dates.Contains(day) && day.Month == month) button.SetResourceReference(Control.BackgroundProperty, "AccentSoftBrush");
                button.Click += (_, _) => { _model.Date = day; _model.Mode = _model.FocusOnly ? "时间轴" : "日"; }; days.Children.Add(button);
            }
            panel.Children.Add(days); grid.Children.Add(Cell(panel, new Thickness(4)));
        }
        CalendarContent.Children.Add(grid);
    }
    private void DrawRecordTimeline()
    {
        var entries = _model!.Entries(_model.Date, _model.Date);
        var records = entries.Where(entry => entry.Kind == "专注").GroupBy(entry => entry.Id).ToArray();
        var total = records.Sum(group => group.Sum(entry => (entry.EndMinutes ?? 0) - entry.Start!.Value.ToTimeSpan().TotalMinutes));
        CalendarContent.Children.Add(Label($"{records.Length} 次专注 · 累计 {Duration(total)}", 13, true));
        var factor = (double)(TryFindResource("FocusCalendarCardScale") ?? 1d);
        var rows = records.Select(group => group.OrderBy(entry => entry.Start).First() with {
            End = group.OrderBy(entry => entry.Start).Last().End,
            EndMinutes = group.OrderBy(entry => entry.Start).Last().EndMinutes
        }).Concat(entries.Where(entry => entry.Kind == "安排")).OrderByDescending(entry => entry.Start).ToArray();
        if (rows.Length == 0) { CalendarContent.Children.Add(new TextBlock { Text = "这一天还没有专注记录或项目安排。", Margin = new Thickness(0, 32, 0, 0), Foreground = (Brush)FindResource("MutedBrush") }); return; }
        var projectItems = _model.Store.GetProjects(true).ToDictionary(item => item.Id);
        var todoItems = _model.Store.GetTodos(true).ToDictionary(item => item.Id);
        var recordItems = _model.Store.GetRecords().ToDictionary(item => item.Id);
        var outcomes = _model.Store.GetOutcomes().ToDictionary(item => item.SessionId);
        foreach (var entry in rows)
        {
            var row = new Grid { Margin = new Thickness(0, 16, 0, 0) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(90 * factor) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(24) }); row.ColumnDefinitions.Add(new());
            var time = Label($"{entry.Start:HH:mm}\n{(entry.EndMinutes >= 1440 ? "24:00" : entry.End?.ToString("HH:mm") ?? "")}", 12 * factor, true); time.Margin = new Thickness(0, 14, 8, 0); row.Children.Add(time);
            var line = new Border { Width = 1, Margin = new Thickness(0, -16, 0, 0) }; line.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); Grid.SetColumn(line, 1); row.Children.Add(line);
            var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 18, 0, 0) }; dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "AccentBrush"); Grid.SetColumn(dot, 1); row.Children.Add(dot);
            var panel = new StackPanel(); var heading = new DockPanel();
            var minutes = entry.Kind == "专注" ? records.Single(group => group.Key == entry.Id).Sum(part => (part.EndMinutes ?? 0) - part.Start!.Value.ToTimeSpan().TotalMinutes) : (entry.EndMinutes ?? 0) - entry.Start!.Value.ToTimeSpan().TotalMinutes;
            var length = Label(Duration(minutes), 14 * factor); length.FontWeight = FontWeights.SemiBold; DockPanel.SetDock(length, Dock.Right); heading.Children.Add(length);
            var title = Label(entry.Title, 17 * factor); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0, 0, 14, 0); heading.Children.Add(title); panel.Children.Add(heading);
            if (entry.ProjectId is { } project && projectItems.GetValueOrDefault(project) is { } item) AddDetail(item.Category);
            if (entry.TodoId is { } todo && todoItems.GetValueOrDefault(todo) is { } linked) AddDetail("待办 · " + linked.Title);
            if (entry.Kind == "专注")
            {
                var record = recordItems[entry.Id];
                if (record.Strict) AddDetail("严格专注 · 拦截 " + (record.InterceptionCount?.ToString() ?? "未统计") + (record.InterceptionCount is not null ? " 次" : ""));
                var outcome = outcomes.GetValueOrDefault(entry.Id);
                AddDetail(outcome is { Summary.Length: > 0 } ? "成果 · " + outcome.Summary : outcome?.Quantity is { } quantity ? $"成果 · {quantity:0.##} {outcome.Unit}" : "点击补记成果");
                if (outcome is { NextStep.Length: > 0 }) AddDetail("下一步 · " + outcome.NextStep);
            }
            else AddDetail("项目安排" + (entry.Detail.Length > 0 ? " · " + entry.Detail : ""));
            var button = new Button { Content = panel, Padding = new Thickness(18 * factor), HorizontalContentAlignment = HorizontalAlignment.Stretch, Tag = entry, ToolTip = "查看详情" };
            button.SetResourceReference(StyleProperty, "CalendarEntryButton"); button.SetResourceReference(BackgroundProperty, "FieldBrush");
            button.Click += (_, _) => EntryRequested?.Invoke(entry); Grid.SetColumn(button, 2); row.Children.Add(button); CalendarContent.Children.Add(row);
            void AddDetail(string text) { var label = Label(text, 12 * factor, true); label.Margin = new Thickness(0, 7 * factor, 0, 0); panel.Children.Add(label); }
        }
    }
    private static string Duration(double minutes) => minutes >= 60 ? $"{(int)minutes / 60}小时{(int)minutes % 60}分钟" : minutes >= 1 ? $"{(int)minutes}分钟" : $"{Math.Round(minutes * 60)}秒";
    private void DrawDoubleDay()
    {
        var width = Math.Max(300, CalendarViewport.ActualWidth - 18);
        var allDay = _model!.Entries(_model.Date, _model.Date).Where(entry => entry.Start is null).ToArray();
        foreach (var entry in allDay) CalendarContent.Children.Add(EntryButton(entry, true));
        var grid = new Grid(); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); grid.ColumnDefinitions.Add(new());
        var left = new StackPanel(); var right = new StackPanel(); Grid.SetColumn(right, 2); grid.Children.Add(left); grid.Children.Add(right); CalendarContent.Children.Add(grid);
        DrawTimeline(_model.Date, 1, false, left, 0, 12, (width - 16) / 2);
        DrawTimeline(_model.Date, 1, false, right, 12, 12, (width - 16) / 2);
        _initialOffset = 0;
    }
    private void DrawTimeline(DateOnly from, int count, bool timetable, StackPanel? target = null, int firstHour = 0, int hours = 24, double? blockWidth = null)
    {
        target ??= CalendarContent;
        var entries = _model!.Entries(from, from.AddDays(count - 1)); var width = blockWidth ?? Math.Max(300, CalendarViewport.ActualWidth - 18);
        if (!timetable) _initialOffset = Math.Max(0, entries.Where(entry => entry.Start is not null).Select(entry => (double)entry.Start!.Value.Hour).DefaultIfEmpty(8).Min() - 1) * 48;
        var labelWidth = timetable ? 70 : 48; var dayWidth = (width - labelWidth) / count;
        var header = new Grid { Height = 32, Width = width, HorizontalAlignment = HorizontalAlignment.Left }; header.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth) });
        for (var day = 0; day < count; day++)
        {
            header.ColumnDefinitions.Add(new()); var label = Label(hours == 12 ? $"{firstHour:00}:00 — {firstHour + 12:00}:00" : $"{Weekdays[((int)from.AddDays(day).DayOfWeek + 6) % 7]} {from.AddDays(day):M/d}", 11);
            Grid.SetColumn(label, day + 1); header.Children.Add(label);
        }
        target.Children.Add(header);
        var allDay = new Grid { Width = width, HorizontalAlignment = HorizontalAlignment.Left }; allDay.ColumnDefinitions.Add(new() { Width = new GridLength(labelWidth) }); allDay.Children.Add(Label("全天", 10, true));
        for (var day = 0; day < count; day++)
        {
            allDay.ColumnDefinitions.Add(new()); var panel = new StackPanel(); foreach (var entry in entries.Where(entry => entry.Date == from.AddDays(day) && entry.Start is null)) panel.Children.Add(EntryButton(entry, true));
            Grid.SetColumn(panel, day + 1); allDay.Children.Add(panel);
        }
        if (hours == 24 && entries.Any(entry => entry.Start is null)) target.Children.Add(allDay);
        var periods = _model.Store.GetSchoolTerm().Periods.OrderBy(period => period.Number).ToArray();
        var factor = (double)(TryFindResource(timetable ? "TimetableCardScale" : _model!.FocusOnly ? "FocusCalendarCardScale" : "CalendarCardScale") ?? 1d);
        var rowHeight = (timetable ? 86d : hours == 12 ? Math.Max(20, (CalendarViewport.ActualHeight - 50) / 12) : 48d) * factor; var height = rowHeight * (timetable ? periods.Length : hours);
        var canvas = new Canvas { Width = width, Height = height + (timetable ? 0 : 32), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        for (var row = 0; row <= (timetable ? periods.Length : hours); row++)
        {
            var line = new Border { Width = width, Height = 1 }; line.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); Canvas.SetTop(line, row * rowHeight); canvas.Children.Add(line);
            if (row < (timetable ? periods.Length : hours))
            {
                var label = Label(timetable ? $"第{periods[row].Number}节\n{periods[row].Start:HH:mm}\n{periods[row].End:HH:mm}" : $"{row + firstHour:00}:00", 10, true); Canvas.SetTop(label, row * rowHeight + 4); canvas.Children.Add(label);
            }
        }
        for (var day = 0; day <= count; day++) { var line = new Border { Width = 1, Height = height }; line.SetResourceReference(Border.BackgroundProperty, "BorderBrush"); Canvas.SetLeft(line, labelWidth + day * dayWidth); canvas.Children.Add(line); }
        for (var day = 0; day < count; day++)
        {
            var date = from.AddDays(day);
            var daily = entries.Where(entry => entry.Date == date && entry.Start is not null);
            if (hours == 12) daily = daily.Where(entry => entry.Start!.Value.Hour < firstHour + hours && Math.Max(entry.Start.Value.ToTimeSpan().TotalMinutes, entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes ?? 0) >= firstHour * 60 && !(entry.Start.Value.Hour < firstHour && (entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes ?? 0) <= firstHour * 60))
                .Select(entry => entry with { Start = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Max(firstHour * 60, entry.Start!.Value.ToTimeSpan().TotalMinutes))), EndMinutes = Math.Min((firstHour + hours) * 60, entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes ?? entry.Start.Value.ToTimeSpan().TotalMinutes) });
            foreach (var placement in TimelineLayout.Place(daily, timetable ? 1 : 32d / rowHeight * 60))
            {
                var entry = placement.Entry; double top, bottom;
                if (timetable)
                {
                    var first = Array.FindIndex(periods, period => period.Start == entry.Start); var last = Array.FindIndex(periods, period => period.End == entry.End);
                    if (first < 0 || last < first) continue; top = first * rowHeight; bottom = (last + 1) * rowHeight;
                }
                else { top = (entry.Start!.Value.ToTimeSpan().TotalMinutes / 60 - firstHour) * rowHeight; bottom = ((entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes ?? entry.Start.Value.ToTimeSpan().TotalMinutes) / 60 - firstHour) * rowHeight; }
                var shortBlock = bottom - top < 54;
                var button = EntryButton(entry, shortBlock); if (shortBlock && button.Content is StackPanel panel && panel.Children[0] is TextBlock text) { text.Text = entry.Kind + " · " + entry.Title; text.TextWrapping = TextWrapping.NoWrap; }
                button.Width = Math.Max(20, dayWidth / placement.Columns - 5); button.Height = Math.Max(28, bottom - top - 3);
                Canvas.SetLeft(button, labelWidth + day * dayWidth + placement.Column * dayWidth / placement.Columns); Canvas.SetTop(button, top); canvas.Children.Add(button);
            }
        }
        target.Children.Add(canvas);
    }
}
