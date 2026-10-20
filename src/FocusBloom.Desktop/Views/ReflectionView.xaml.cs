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
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class ReflectionView : UserControl
{
    private ReflectionViewModel? _model;
    private bool _rendering;
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public ReflectionView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => { if (_model is not null) _model.RedrawRequested -= Changed; _model = DataContext as ReflectionViewModel; if (_model is not null) _model.RedrawRequested += Changed; Render(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); }; Loaded += (_, _) => { Appearance.Changed += Changed; Render(); }; Unloaded += (_, _) => Appearance.Changed -= Changed;
    }
    private void Changed(object? sender, EventArgs args) => Render();
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs args) { if (Math.Abs(args.PreviousSize.Width - args.NewSize.Width) > 1 || Math.Abs(args.PreviousSize.Height - args.NewSize.Height) > 1) Render(); }
    private void Edit_Click(object sender, RoutedEventArgs args) { if (_model!.CanEdit) new ReflectionEditorWindow(Main, _model.Day) { Owner = Window.GetWindow(this) }.ShowDialog(); }
    private void Reminder_Click(object sender, RoutedEventArgs args) => new ReflectionReminderWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void Render()
    {
        if (_rendering || _model is null || !IsVisible || ReflectionViewport.ActualWidth < 200) return;
        _rendering = true;
        try
        {
            var wide = ReflectionViewport.ActualWidth >= 780;
            var daily = _model.IsDailyView;
            MonthPanel.Visibility = daily ? Visibility.Collapsed : Visibility.Visible;
            ReflectionContent.ColumnDefinitions[0].Width = new GridLength(!daily && wide ? 1.25 : 1, GridUnitType.Star); ReflectionContent.ColumnDefinitions[1].Width = new GridLength(!daily && wide ? 14 : 0); ReflectionContent.ColumnDefinitions[2].Width = !daily && wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Grid.SetColumn(DayPanel, !daily && wide ? 2 : 0); Grid.SetRow(DayPanel, daily || wide ? 0 : 1); DayPanel.Margin = new Thickness(0, daily || wide ? 0 : 14, 0, 0);
            DayPanel.Width = daily ? Math.Min(760, ReflectionViewport.ActualWidth - 4) : double.NaN; DayPanel.HorizontalAlignment = daily ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
            var width = Math.Max(220, (wide ? (ReflectionViewport.ActualWidth - 14) * 1.25 / 2.25 : ReflectionViewport.ActualWidth) - 32);
            if (!daily) { RenderCalendar(); RenderTrend(width); }
        }
        finally { _rendering = false; }
    }
    private void RenderCalendar()
    {
        MoodCalendar.Children.Clear(); var first = _model!.MonthFirst; var start = CalendarRules.Monday(first); var rows = (first.DayNumber - start.DayNumber + DateTime.DaysInMonth(first.Year, first.Month) + 6) / 7;
        var weekdays = new UniformGrid { Columns = 7, Height = 24 };
        foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Label(name, 11, true); label.TextAlignment = TextAlignment.Center; weekdays.Children.Add(label); } MoodCalendar.Children.Add(weekdays);
        var grid = new UniformGrid { Columns = 7, Rows = rows }; var entries = _model.Entries.ToDictionary(item => item.Date); var scale = (double)Application.Current.FindResource("MoodCardScale");
        var cellHeight = Math.Clamp((ReflectionViewport.ActualHeight - 112) / rows, 56, 72);
        for (var index = 0; index < rows * 7; index++)
        {
            var day = start.AddDays(index); var entry = entries.GetValueOrDefault(day); var choice = entry?.Mood is { } value ? ReflectionRules.Moods[value - 1] : null;
            var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var header = new Grid { Height = 16 * scale }; var number = Label(day.Day.ToString(), 11 * scale); number.TextAlignment = TextAlignment.Center; header.Children.Add(number); content.Children.Add(header);
            if (entry?.Photos is { Length: > 0 } photos) { var label = Label("▧ " + photos.Length, 8 * scale, true); label.HorizontalAlignment = HorizontalAlignment.Right; label.Margin = new Thickness(0, 0, 2, 0); header.Children.Add(label); }
            if (choice is not null) content.Children.Add(new MoodIcon { Value = choice.Value, Width = Math.Min(34, cellHeight - 24) * scale, Height = Math.Min(34, cellHeight - 24) * scale, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2 * scale, 0, 0) });
            else { var icon = Label(entry is null ? "" : entry.Photos is { Length: > 0 } ? "图片" : "记", 11 * scale); icon.TextAlignment = TextAlignment.Center; icon.Margin = new Thickness(0, 3, 0, 0); content.Children.Add(icon); }
            var button = new Button { Content = content, Tag = day, Height = cellHeight * scale, Margin = new Thickness(2), IsEnabled = day <= _model.Today, Opacity = day.Month == first.Month ? 1 : .35, BorderThickness = new Thickness(day == _model.Day ? 2 : day == _model.Today ? 1 : 0), ToolTip = $"{day:yyyy-MM-dd}" + (choice is not null ? " · " + choice.Name : entry is not null ? " · 已写复盘" : day > _model.Today ? " · 未来日期" : " · 尚未记录") };
            button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.SetResourceReference(BorderBrushProperty, "AccentBrush");
            if (choice is not null) button.Background = MoodFill(choice.Value); else button.SetResourceReference(BackgroundProperty, "SubtleBrush");
            button.Click += (_, _) => _model.Date = day.ToDateTime(TimeOnly.MinValue); System.Windows.Automation.AutomationProperties.SetName(button, (string)button.ToolTip); grid.Children.Add(button);
        }
        MoodCalendar.Children.Add(grid);
    }
    private void Photo_Click(object sender, RoutedEventArgs args) => new PhotoViewerWindow(((ReflectionPhoto)((Button)sender).DataContext).Path) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void RenderTrend(double width)
    {
        const double left = 42, top = 12, bottom = 140; var canvas = new Canvas { Width = width, Height = 165, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var mood in ReflectionRules.Moods.Take(5))
        {
            var y = bottom - (mood.Value - 1) * (bottom - top) / 4; var line = new Line { X1 = left, X2 = width - 8, Y1 = y, Y2 = y }; line.SetResourceReference(Shape.StrokeProperty, "BorderBrush"); canvas.Children.Add(line); var label = Label(mood.Name, 9, true); label.Width = 38; Canvas.SetTop(label, y - 6); canvas.Children.Add(label);
        }
        var count = DateTime.DaysInMonth(_model!.Day.Year, _model.Day.Month); Point Position(DailyReflection entry) => new(left + (entry.Date.Day - 1) * (width - left - 10) / Math.Max(1, count - 1), bottom - (ReflectionRules.Mood(entry.Mood!.Value).Level - 1) * (bottom - top) / 4);
        DailyReflection? previous = null;
        foreach (var entry in _model.MonthEntries.Where(item => item.Mood is not null).OrderBy(item => item.Date))
        {
            var point = Position(entry);
            if (previous is not null && entry.Date.DayNumber == previous.Date.DayNumber + 1) { var from = Position(previous); var line = new Line { X1 = from.X, Y1 = from.Y, X2 = point.X, Y2 = point.Y, StrokeThickness = 2 }; line.SetResourceReference(Shape.StrokeProperty, "AccentBrush"); canvas.Children.Add(line); }
            var dot = new Ellipse { Width = 9, Height = 9, Fill = MoodIcon.BrushFor(entry.Mood!.Value) };
            var hit = new Button { Content = dot, Width = 18, Height = 18, Tag = entry.Date, ToolTip = $"{entry.Date:M月d日} · {ReflectionRules.Moods[entry.Mood!.Value - 1].Name}" }; hit.SetResourceReference(StyleProperty, "CompletionDateButton"); hit.HorizontalContentAlignment = HorizontalAlignment.Center; hit.VerticalContentAlignment = VerticalAlignment.Center;
            hit.Click += (_, _) => _model.Date = entry.Date.ToDateTime(TimeOnly.MinValue); Canvas.SetLeft(hit, point.X - 9); Canvas.SetTop(hit, point.Y - 9); canvas.Children.Add(hit); previous = entry;
        }
        foreach (var day in new[] { 1, 8, 15, 22, count }.Distinct()) { var label = Label(day.ToString(), 9, true); Canvas.SetLeft(label, left + (day - 1) * (width - left - 10) / Math.Max(1, count - 1) - 3); Canvas.SetTop(label, 148); canvas.Children.Add(label); }
        if (previous is null) { var empty = Label("本月还没有心情记录", 11, true); Canvas.SetLeft(empty, left + 12); Canvas.SetTop(empty, 65); canvas.Children.Add(empty); }
        MoodTrend.Content = canvas;
    }
    internal static SolidColorBrush MoodFill(int value)
    {
        var color = MoodIcon.ColorFor(value); var background = ((SolidColorBrush)Application.Current.FindResource("CardBrush")).Color; var amount = Appearance.IsDark ? .18 : .22;
        return new(Color.FromRgb((byte)(background.R + (color.R - background.R) * amount), (byte)(background.G + (color.G - background.G) * amount), (byte)(background.B + (color.B - background.B) * amount)));
    }
    private static TextBlock Label(string text, double size, bool muted = false) { var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return label; }
}
