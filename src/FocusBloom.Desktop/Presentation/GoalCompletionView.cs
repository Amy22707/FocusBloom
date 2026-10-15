using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Presentation;

public sealed class GoalCompletionView : UserControl
{
    private GoalsViewModel? _model;
    private readonly StackPanel _calendar = new();
    private readonly ScrollViewer _viewport;
    private bool _rendering;
    public GoalCompletionView()
    {
        NameScope.SetNameScope(this, new NameScope()); RegisterName("CompletionCalendar", _calendar);
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new());
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) }; root.Children.Add(header);
        var tools = new WrapPanel(); header.Children.Add(tools);
        Button Tool(string text, string command)
        {
            var button = new Button { Content = text, Padding = new Thickness(9, 5, 9, 5), FontSize = 12, Margin = new Thickness(0, 0, 5, 0) };
            button.SetBinding(Button.CommandProperty, new Binding(command)); tools.Children.Add(button); return button;
        }
        Tool("‹", "PreviousCommand"); var period = Text("", 13); period.VerticalAlignment = VerticalAlignment.Center; period.Margin = new Thickness(6, 0, 10, 0); period.SetBinding(TextBlock.TextProperty, "CompletionLabel"); tools.Children.Add(period);
        Tool("›", "NextCommand"); Tool("今天", "TodayCommand");
        var date = new DatePicker { Width = 160, Height = 34, MinHeight = 34, FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
        var dateTextStyle = new Style(typeof(DatePickerTextBox), (Style)FindResource(typeof(DatePickerTextBox))); dateTextStyle.Setters.Add(new Setter(MinHeightProperty, 0d)); dateTextStyle.Setters.Add(new Setter(HeightProperty, 30d)); dateTextStyle.Setters.Add(new Setter(PaddingProperty, new Thickness(4, 2, 4, 2))); dateTextStyle.Setters.Add(new Setter(FontSizeProperty, 12d)); date.Resources[typeof(DatePickerTextBox)] = dateTextStyle;
        date.SetBinding(DatePicker.SelectedDateProperty, new Binding("Anchor") { Mode = BindingMode.TwoWay }); tools.Children.Add(date); RegisterName("CompletionAnchorDate", date);
        var summary = Text("", 12); summary.Margin = new Thickness(0, 10, 0, 0); summary.SetBinding(TextBlock.TextProperty, "CompletionSummary"); header.Children.Add(summary);
        _viewport = new ScrollViewer { Content = _calendar, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(_viewport, 1); root.Children.Add(_viewport); Content = root;
        DataContextChanged += (_, _) => { if (_model is not null) _model.CompletionChanged -= ModelChanged; _model = DataContext as GoalsViewModel; if (_model is not null) _model.CompletionChanged += ModelChanged; Render(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
        _viewport.SizeChanged += (_, args) => { if (Math.Abs(args.PreviousSize.Width - args.NewSize.Width) > 1 || _model?.View == "月" && Math.Abs(args.PreviousSize.Height - args.NewSize.Height) > 1) Render(); };
        Loaded += (_, _) => { Appearance.Changed += ModelChanged; Render(); }; Unloaded += (_, _) => Appearance.Changed -= ModelChanged;
    }
    private void ModelChanged(object? sender, EventArgs args) => Render();
    private void Render()
    {
        if (_rendering || _model is null || !IsVisible) return;
        _rendering = true;
        try
        {
            _calendar.Children.Clear();
            if (_model.View == "年")
            {
                _calendar.Children.Add(Year());
            }
            else if (_model.View == "月") _calendar.Children.Add(Month(_model.CompletionFrom));
            else
            {
                var week = new UniformGrid { Columns = 7, Margin = new Thickness(0, 4, 0, 10) };
                foreach (var day in _model.CompletionDays) { var body = new StackPanel(); body.Children.Add(Text("周" + "一二三四五六日"[((int)day.Date.DayOfWeek + 6) % 7], 11, true)); body.Children.Add(Day(day.Date, false, true)); week.Children.Add(body); }
                _calendar.Children.Add(week);
            }
            var legend = new WrapPanel { Margin = new Thickness(2, 10, 0, 8) }; legend.Children.Add(Text("完成度  ", 11, true));
            foreach (var ratio in new[] { 0d, .25, .5, .75, 1d })
            {
                legend.Children.Add(new Border { Width = 20, Height = 14, Background = Fill(ratio), CornerRadius = new(3), Margin = new(0, 0, 4, 0), ToolTip = ratio.ToString("P0") });
            }
            legend.Children.Add(Text("  0% → 100%", 11, true)); _calendar.Children.Add(legend);
            _calendar.Children.Add(Text("整体完成度取当天各目标与计划内习惯进度的平均值。周、月、累计目标按截至当天的周期进度显示；未来日期不计入平均值。点击日期查看明细。", 11, true));
        }
        finally { _rendering = false; }
    }
    private FrameworkElement Year()
    {
        var first = _model!.CompletionFrom; var start = CalendarRules.Monday(first); var columns = (_model.CompletionTo.DayNumber - start.DayNumber) / 7 + 1;
        var cell = Math.Min(19, Math.Max(6, (_viewport.ActualWidth - 36) / columns)); var canvas = new Canvas { Width = 30 + columns * cell, Height = 34 + 7 * cell, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 6) };
        for (var weekday = 0; weekday < 7; weekday += 2) { var label = Text("一二三四五六日"[weekday].ToString(), 10, true); Canvas.SetTop(label, 24 + weekday * cell); canvas.Children.Add(label); }
        foreach (var day in _model.CompletionDays)
        {
            var column = (day.Date.DayNumber - start.DayNumber) / 7; var row = ((int)day.Date.DayOfWeek + 6) % 7;
            var button = (Button)Day(day.Date, true, false); button.Content = null; button.Width = button.Height = cell - 2; button.Margin = new Thickness(0);
            Canvas.SetLeft(button, 28 + column * cell); Canvas.SetTop(button, 24 + row * cell); canvas.Children.Add(button);
            if (day.Date.Day == 1) { var month = Text(day.Date.ToString("M月"), 10, true); Canvas.SetLeft(month, 28 + column * cell); canvas.Children.Add(month); }
        }
        return canvas;
    }
    private FrameworkElement Month(DateOnly first)
    {
        var body = new StackPanel(); var title = Text(first.ToString("yyyy年M月"), 15); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(3, 0, 0, 8); body.Children.Add(title);
        var weekdays = new UniformGrid { Columns = 7, Height = 26 };
        foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Text(name, 11, true); label.TextAlignment = TextAlignment.Center; label.VerticalAlignment = VerticalAlignment.Center; weekdays.Children.Add(label); }
        var start = CalendarRules.Monday(first);
        var rows = (first.DayNumber - start.DayNumber + DateTime.DaysInMonth(first.Year, first.Month) + 6) / 7;
        body.Children.Add(weekdays); var grid = new UniformGrid { Columns = 7, Rows = rows }; var height = Math.Clamp((_viewport.ActualHeight - 110) / rows - 4, 40, 66);
        for (var index = 0; index < rows * 7; index++) { var day = start.AddDays(index); if (day.Month == first.Month) { var button = (Button)Day(day, false, false); button.Height = height; grid.Children.Add(button); } else grid.Children.Add(new Border { Height = height + 4 }); }
        body.Children.Add(grid); var card = new Border { Child = body, Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 10), CornerRadius = new(12), BorderThickness = new(1) };
        card.SetResourceReference(Border.BackgroundProperty, "CardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return card;
    }
    private FrameworkElement Day(DateOnly date, bool compact, bool week)
    {
        var day = _model!.CompletionDays.Single(item => item.Date == date); var background = Fill(day.Future ? 0 : day.Ratio);
        var foreground = Contrast(background.Color, ((SolidColorBrush)FindResource("TextBrush")).Color) >= Contrast(background.Color, ((SolidColorBrush)FindResource("PrimaryTextBrush")).Color) ? "TextBrush" : "PrimaryTextBrush";
        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var number = Text(week ? date.ToString("M/d") : date.Day.ToString(), compact ? 11 : week ? 15 : 13); number.TextAlignment = TextAlignment.Center; number.SetResourceReference(TextBlock.ForegroundProperty, foreground); content.Children.Add(number);
        if (!compact && !week) { number.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; number.LineHeight = 18; }
        if (!compact)
        {
            var amount = Text(day.Entries.Count == 0 ? "无计划" : day.Future ? "待执行" : day.Ratio.ToString("P0"), week ? 11 : 10); amount.TextAlignment = TextAlignment.Center; amount.Margin = new Thickness(0, week ? 5 : 2, 0, 0); amount.SetResourceReference(TextBlock.ForegroundProperty, foreground); if (!week) { amount.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; amount.LineHeight = 14; } content.Children.Add(amount);
        }
        var tooltip = date.ToString("yyyy-MM-dd") + (day.Entries.Count == 0 ? " · 无计划" : day.Future ? " · 未来计划" : $" · 完成度 {day.Ratio:P0} · 已完成 {day.Completed}/{day.Entries.Count}");
        tooltip += string.Concat(day.Entries.Select(item => "\n" + item.Name + " · " + (day.Future ? "待执行" : item.IsHabit ? item.Complete ? "已打卡" : "未打卡" : GoalRow.Amount(item.Metric, item.Value) + " / " + GoalRow.Amount(item.Metric, item.Target))));
        var button = new Button { Content = content, Tag = day, Background = background, Height = compact ? 26 : week ? 120 : 71, Margin = new Thickness(2), ToolTip = tooltip, BorderThickness = new Thickness(date == _model.Today ? 2 : day.Future && day.Entries.Count > 0 ? 1 : 0) };
        button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.SetResourceReference(Button.BorderBrushProperty, date == _model.Today ? "AccentBrush" : "BorderBrush");
        ToolTipService.SetInitialShowDelay(button, 100); System.Windows.Automation.AutomationProperties.SetName(button, tooltip);
        button.Click += (_, _) =>
        {
            var main = (MainViewModel)Window.GetWindow(this).DataContext;
            if (_model.SelectedCompletion?.HabitId is { } id && main.Calendar.Store.GetHabits().FirstOrDefault(item => item.Id == id) is { } habit) new HabitHistoryWindow(main, habit, date) { Owner = Window.GetWindow(this) }.ShowDialog();
            else new CompletionDayWindow(main, date) { Owner = Window.GetWindow(this) }.ShowDialog();
        };
        return button;
    }
    private SolidColorBrush Fill(double ratio)
    {
        var low = ((SolidColorBrush)FindResource("SubtleBrush")).Color; var high = ((SolidColorBrush)FindResource("AccentBrush")).Color; var amount = ratio <= 0 ? 0 : .18 + .82 * ratio;
        return new(Color.FromRgb((byte)Math.Round(low.R + (high.R - low.R) * amount), (byte)Math.Round(low.G + (high.G - low.G) * amount), (byte)Math.Round(low.B + (high.B - low.B) * amount)));
    }
    private static double Contrast(Color first, Color second)
    {
        static double Luminance(Color color) { static double Linear(byte value) { var channel = value / 255d; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); } return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B); }
        var a = Luminance(first); var b = Luminance(second); return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }
    private static TextBlock Text(string text, double size, bool muted = false)
    { var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return label; }
}
