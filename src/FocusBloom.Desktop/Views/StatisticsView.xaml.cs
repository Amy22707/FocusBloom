using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.Windows;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Views;

public partial class StatisticsView : UserControl
{
    private StatisticsViewModel? _model;
    private bool _rendering;
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public StatisticsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => { if (_model is not null) _model.Changed -= ModelChanged; _model = DataContext as StatisticsViewModel; if (_model is not null) _model.Changed += ModelChanged; Render(); };
        IsVisibleChanged += (_, _) => { if (IsVisible) Render(); };
        Loaded += (_, _) => { Appearance.Changed += ModelChanged; Render(); };
        Unloaded += (_, _) => Appearance.Changed -= ModelChanged;
    }
    private void ModelChanged(object? sender, EventArgs args) => Render();
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs args) => Render();
    public void Render()
    {
        if (_rendering || !IsVisible || _model is null || StatisticsViewport.ActualWidth < 200) return;
        _rendering = true;
        try
        {
            StatisticsContent.Children.Clear();
            if (!_model.IsValid) return;
            if (_model.ReviewMode) { StatisticsContent.Children.Add(Overview()); DrawReview(); StatisticsContent.Children.Add(GoalProgressCard()); return; }
            var modules = _model.Modules.Where(item => item.Visible).ToArray(); var width = StatisticsViewport.ActualWidth;
            for (var index = 0; index < modules.Length; index++)
            {
                var option = modules[index];
                var next = index + 1 < modules.Length ? modules[index + 1] : null;
                var compactPair = next is not null && (option.Key == "concentration" && next.Key == "shares" || option.Key == "shares" && next.Key == "concentration");
                if (next is not null && width >= 620 && (compactPair || option.Key == "trend" && next.Key == "shares" || option.Key == "shares" && next.Key == "trend"))
                {
                    var firstWeight = option.Key == "trend" ? 1.6 : 1; var secondWeight = next.Key == "trend" ? 1.6 : 1; var totalWeight = firstWeight + secondWeight;
                    var row = new Grid(); row.ColumnDefinitions.Add(new() { Width = new GridLength(firstWeight, GridUnitType.Star) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(10) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(secondWeight, GridUnitType.Star) });
                    var first = Module(option, (width - 10) * firstWeight / totalWeight); var secondOption = modules[++index]; var second = Module(secondOption, (width - 10) * secondWeight / totalWeight);
                    row.Children.Add(first); Grid.SetColumn(second, 2); row.Children.Add(second); StatisticsContent.Children.Add(row);
                }
                else if (option.Key == "concentration" && width >= 620)
                {
                    var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(10) }); row.ColumnDefinitions.Add(new());
                    row.Children.Add(Module(option, (width - 10) / 2)); StatisticsContent.Children.Add(row);
                }
                else StatisticsContent.Children.Add(Module(option, width));
            }
            if (modules.Length == 0) StatisticsContent.Children.Add(Text("尚未选择统计模块，点击上方‘模块’选择要展示的内容。", 12, true));
            else if (_model.Snapshot.Sessions.Count == 0) StatisticsContent.Children.Insert(0, Text("这个范围还没有专注记录，可以调整日期或筛选。", 12, true));
        }
        finally { _rendering = false; }
    }
    private void Modules_Click(object sender, RoutedEventArgs args) => new StatisticsModulesWindow(_model!) { Owner = Window.GetWindow(this) }.ShowDialog();
    private FrameworkElement Overview()
    {
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var metric in _model!.Metrics)
        {
            var body = new StackPanel(); body.Children.Add(Text(metric.Name, 11, true)); var value = Text(metric.Value, 20); value.FontWeight = FontWeights.SemiBold; value.Margin = new Thickness(0, 6, 0, 0); value.TextTrimming = TextTrimming.CharacterEllipsis; value.ToolTip = metric.Tooltip; body.Children.Add(value);
            var card = new Border { Child = body, CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Padding = new Thickness(14), Margin = new Thickness(0, 0, 8, 0) }; card.SetResourceReference(Border.BackgroundProperty, "CardBrush"); card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); grid.Children.Add(card);
        }
        return grid;
    }
    private FrameworkElement Module(StatisticsModuleOption option, double width)
    {
        FrameworkElement result;
        switch (option.Key)
        {
            case "overview": result = Overview(); break;
            case "trend":
                var bars = Card("投入趋势", out var barBody); var metric = new ComboBox { Width = 86, ItemsSource = _model!.ChartMetrics, SelectedItem = _model.ChartMetric, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -28, 0, 4) };
                metric.SelectionChanged += (_, _) => { if (metric.SelectedItem is string value) _model.ChartMetric = value; }; barBody.Children.Add(metric); barBody.Children.Add(Bars(_model.Trend, Math.Max(200, width - 32), true)); result = bars; break;
            case "shares":
                var shares = Card("投入占比", out var shareBody); var grouping = new ComboBox { Width = 98, ItemsSource = _model!.ShareModes, SelectedItem = _model.ShareMode, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -28, 0, 4) };
                grouping.SelectionChanged += (_, _) => { if (grouping.SelectedItem is string value) _model.ShareMode = value; }; shareBody.Children.Add(grouping); DrawShares(shareBody); result = shares; break;
            case "activity": var activity = Card("活跃时间段", out var hours); hours.Children.Add(Bars(_model!.Hours, Math.Max(200, width - 32), false)); result = activity; break;
            case "annual":
                var yearly = Card("年度投入", out var annual); var yearSelector = new ComboBox { Width = 90, ItemsSource = _model!.HeatmapYears, SelectedItem = _model.HeatmapYear, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -28, 0, 6) };
                yearSelector.SelectionChanged += (_, _) => { if (yearSelector.SelectedItem is int year) _model.HeatmapYear = year; }; annual.Children.Add(yearSelector); annual.Children.Add(Text("按当前项目／待办筛选展示全年，点击日期查看明细。", 11, true)); annual.Children.Add(Heatmap(Math.Max(200, width - 32))); result = yearly; break;
            case "goals": result = GoalProgressCard(); break;
            default: result = ConcentrationCard(); break;
        }
        result.Tag = option; return result;
    }
    private FrameworkElement ConcentrationCard()
    {
        var card = Card("专注程度", out var body); var data = _model!.Interceptions;
        var layout = new Grid { Margin = new Thickness(0, 0, 0, 8) }; layout.ColumnDefinitions.Add(new() { Width = new GridLength(128) }); layout.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); layout.ColumnDefinitions.Add(new()); body.Children.Add(layout);
        var ring = new Canvas { Width = 128, Height = 128 };
        var track = new Ellipse { Width = 112, Height = 112, StrokeThickness = 12 }; track.SetResourceReference(Shape.StrokeProperty, "BorderBrush"); Canvas.SetLeft(track, 8); Canvas.SetTop(track, 8); ring.Children.Add(track);
        Shape? fill = null;
        if (data.ZeroRatio is >= 1) { fill = new Ellipse { Width = 112, Height = 112, StrokeThickness = 12 }; Canvas.SetLeft(fill, 8); Canvas.SetTop(fill, 8); }
        else if (data.ZeroRatio is > 0 and < 1)
        {
            var angle = data.ZeroRatio.Value * Math.PI * 2 - Math.PI / 2; var geometry = new StreamGeometry();
            using (var drawing = geometry.Open()) { drawing.BeginFigure(new(64, 8), false, false); drawing.ArcTo(new(64 + 56 * Math.Cos(angle), 64 + 56 * Math.Sin(angle)), new(56, 56), 0, data.ZeroRatio > .5, SweepDirection.Clockwise, true, false); }
            fill = new Path { Data = geometry, StrokeThickness = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        }
        if (fill is not null) { fill.SetResourceReference(Shape.StrokeProperty, "AccentBrush"); ring.Children.Add(fill); }
        var percentage = Text(data.ZeroRatio is { } ratio ? ratio.ToString("P0") : "—", 24); percentage.Width = 128; percentage.TextAlignment = TextAlignment.Center; percentage.FontWeight = FontWeights.SemiBold; Canvas.SetTop(percentage, 39); ring.Children.Add(percentage);
        var caption = Text(data.ZeroRatio is null ? "暂无数据" : "零拦截场次", 10, true); caption.Width = 128; caption.TextAlignment = TextAlignment.Center; Canvas.SetTop(caption, 76); ring.Children.Add(caption);
        var chart = new Button { Content = ring, Tag = "interception-ratio", ToolTip = $"零拦截 {data.ZeroSessions} / 已统计严格专注 {data.CountedSessions} 次\n未统计记录不计入比例。" }; chart.SetResourceReference(StyleProperty, "StatisticsHit"); chart.Click += (_, _) => InterceptionDetails(null); layout.Children.Add(chart);
        var legend = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(legend, 2); layout.Children.Add(legend);
        legend.Children.Add(Text("每专注小时拦截", 10, true));
        var frequency = Text(_model.InterceptionRateText, 17); frequency.FontWeight = FontWeights.SemiBold; frequency.Margin = new Thickness(0, 3, 0, 4); frequency.ToolTip = "拦截总次数 ÷ 对应已统计严格专注的有效小时数，数值越低，触发拦截越少。有效时长排除暂停和休息。"; legend.Children.Add(frequency);
        var comparison = Text(_model.InterceptionComparisonText, 10, true); comparison.ToolTip = _model.InterceptionComparisonTooltip; comparison.Margin = new Thickness(0, 0, 0, 5); legend.Children.Add(comparison);
        foreach (var (name, count, zero) in new[] { ("零拦截", data.ZeroSessions, true), ("有拦截", data.AffectedSessions, false) })
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) }; var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center }; dot.SetResourceReference(Shape.FillProperty, zero ? "AccentBrush" : "BorderBrush"); row.Children.Add(dot); row.Children.Add(Text($"{name} {count} 次", 11));
            var button = new Button { Content = row, Tag = zero ? "zero-interceptions" : "with-interceptions", ToolTip = "查看对应严格模式记录" }; button.SetResourceReference(StyleProperty, "StatisticsHit"); button.Click += (_, _) => InterceptionDetails(zero); legend.Children.Add(button);
        }
        body.Children.Add(Text(data.CountedSessions == 0 ? data.StrictSessions == 0 ? "所选范围暂无严格模式记录" : "所选记录尚未保存拦截次数" : $"样本 {data.CountedSessions} 次 · 有效专注 {StatisticsViewModel.Duration(data.CountedDuration)} · 拦截 {data.Total} 次", 10, true));
        if (data.UnknownSessions > 0) body.Children.Add(Text($"{data.UnknownSessions} 次旧记录未统计，不计入比例", 10, true));
        return card;
    }
    private void InterceptionDetails(bool? zero)
    {
        var snapshot = _model!.Snapshot;
        var records = snapshot.Sessions.Where(item => item.EndedInRange && item.Record.Strict && (zero is null || zero == true && item.Record.InterceptionCount == 0 || zero == false && item.Record.InterceptionCount > 0)).Select(item => item.Record);
        Details(FocusStatistics.Build(records, snapshot.Filter), zero is null ? "严格模式专注" : zero == true ? "零拦截专注" : "有拦截的专注");
    }
    private TextBlock Text(string value, double size = 12, bool muted = false)
    { var result = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap }; result.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return result; }
    private void DurationTooltip(FrameworkElement element, string title, TimeSpan duration, int count)
    {
        var body = new StackPanel(); body.Children.Add(Text(title, 12, true));
        var total = Text($"总专注 {(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}", 16); total.FontWeight = FontWeights.SemiBold; total.Margin = new Thickness(0, 5, 0, 4); body.Children.Add(total);
        body.Children.Add(Text($"结束 {count} 次", 11, true));
        element.ToolTip = new ToolTip { Content = body, Style = (Style)FindResource("StatisticsTooltip"), Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };
        ToolTipService.SetInitialShowDelay(element, 100); ToolTipService.SetBetweenShowDelay(element, 0); ToolTipService.SetShowDuration(element, 30000);
    }
    private Border Card(string heading, out StackPanel body)
    {
        body = new StackPanel(); body.Children.Add(new TextBlock { Text = heading, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var border = new Border { Child = body, CornerRadius = new CornerRadius(16), Padding = new Thickness(16), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        border.SetResourceReference(Border.BackgroundProperty, "CardBrush"); border.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return border;
    }
    private FrameworkElement Bars(IReadOnlyList<StatisticsBucket> buckets, double width, bool trend)
    {
        var counts = _model!.ChartMetric == "次数"; var chart = new Canvas { Width = width, Height = 214, HorizontalAlignment = HorizontalAlignment.Left };
        if (buckets.Count == 0) return chart;
        const double left = 38, bottom = 180, top = 14;
        var max = Math.Max(1, buckets.Max(bucket => counts ? bucket.Count : bucket.Duration.TotalMinutes));
        var axis = new Line { X1 = left, X2 = width, Y1 = bottom, Y2 = bottom }; axis.SetResourceReference(Shape.StrokeProperty, "BorderBrush"); chart.Children.Add(axis);
        for (var tick = 0; tick <= 3; tick++)
        {
            var value = max * tick / 3; var y = bottom - (bottom - top) * tick / 3;
            var label = Text(value.ToString(value >= 10 ? "0" : "0.#"), 9, true); Canvas.SetTop(label, y - 5); chart.Children.Add(label);
            if (tick > 0) { var line = new Line { X1 = left, X2 = width, Y1 = y, Y2 = y }; line.SetResourceReference(Shape.StrokeProperty, "BorderBrush"); line.Opacity = .5; chart.Children.Add(line); }
        }
        chart.Children.Add(Text(counts ? "次" : "分钟", 9, true));
        var column = (width - left) / buckets.Count; var stride = Math.Max(1, (int)Math.Ceiling(buckets.Count / 8d));
        for (var index = 0; index < buckets.Count; index++)
        {
            var bucket = buckets[index]; var value = counts ? bucket.Count : bucket.Duration.TotalMinutes; var height = value / max * (bottom - top);
            var block = new Border { CornerRadius = new CornerRadius(3), Width = Math.Max(3, column * .65), Height = Math.Max(2, height), VerticalAlignment = VerticalAlignment.Bottom };
            block.SetResourceReference(Border.BackgroundProperty, value > 0 ? "AccentBrush" : "SubtleBrush");
            var button = new Button { Content = block, Width = column, Height = bottom - top, Tag = bucket };
            DurationTooltip(button, bucket.Hour is not null ? bucket.Label : $"{bucket.From:yyyy-MM-dd} — {bucket.To:yyyy-MM-dd}", bucket.Duration, bucket.Count);
            button.SetResourceReference(StyleProperty, "StatisticsHit"); button.VerticalContentAlignment = VerticalAlignment.Bottom;
            button.Click += (_, _) => Details(_model.Drill(bucket.From, bucket.To, hour: bucket.Hour), (trend ? "投入趋势 · " : "活跃时段 · ") + bucket.Label);
            Canvas.SetLeft(button, left + index * column); Canvas.SetTop(button, top); chart.Children.Add(button);
            if (index % stride == 0 || index == buckets.Count - 1) { var label = Text(bucket.Label, 9, true); Canvas.SetLeft(label, left + index * column); Canvas.SetTop(label, bottom + 8); chart.Children.Add(label); }
        }
        return chart;
    }
    private void DrawShares(StackPanel body)
    {
        var shares = _model!.Shares.Where(item => item.Duration > TimeSpan.Zero).ToArray(); var total = shares.Sum(item => item.Duration.Ticks);
        if (total == 0) { body.Children.Add(Text("暂无投入分布", 12, true)); return; }
        var pie = new Canvas { Width = 160, Height = 160, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 10) };
        double angle = -90;
        foreach (var share in shares)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(share.Color)); var sweep = (double)share.Duration.Ticks / total * 360;
            Shape sector;
            if (shares.Length == 1) sector = new Ellipse { Width = 148, Height = 148, Margin = new Thickness(6), Fill = brush };
            else
            {
                Point PointAt(double degrees) => new(80 + 74 * Math.Cos(degrees * Math.PI / 180), 80 + 74 * Math.Sin(degrees * Math.PI / 180));
                var geometry = new StreamGeometry(); using (var drawing = geometry.Open()) { drawing.BeginFigure(new(80, 80), true, true); drawing.LineTo(PointAt(angle), true, false); drawing.ArcTo(PointAt(angle + sweep), new(74, 74), 0, sweep > 180, SweepDirection.Clockwise, true, false); }
                sector = new Path { Data = geometry, Fill = brush }; angle += sweep;
            }
            sector.Cursor = Cursors.Hand; sector.ToolTip = $"{share.Name} · {StatisticsViewModel.Duration(share.Duration)}";
            sector.MouseLeftButtonUp += (_, _) => Details(_model.Drill(_model.Snapshot.Filter.From, _model.Snapshot.Filter.To, share.Projects), share.Name); pie.Children.Add(sector);
        }
        var hole = new Ellipse { Width = 94, Height = 94, IsHitTestVisible = false }; hole.SetResourceReference(Shape.FillProperty, "CardBrush"); Canvas.SetLeft(hole, 33); Canvas.SetTop(hole, 33); pie.Children.Add(hole);
        var center = Text(StatisticsViewModel.Duration(_model.Snapshot.Duration), 13); center.Width = 88; center.TextAlignment = TextAlignment.Center; center.IsHitTestVisible = false; Canvas.SetLeft(center, 36); Canvas.SetTop(center, 66); pie.Children.Add(center); body.Children.Add(pie);
        foreach (var share in shares)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) }; var percent = Text(((double)share.Duration.Ticks / total).ToString("P1"), 11, true); DockPanel.SetDock(percent, Dock.Right); row.Children.Add(percent);
            var dot = new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(share.Color)), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
            var name = Text(share.Name + " · " + StatisticsViewModel.Duration(share.Duration), 11); name.TextTrimming = TextTrimming.CharacterEllipsis; row.Children.Add(name);
            var button = new Button { Content = row, Tag = share, ToolTip = share.Name }; button.SetResourceReference(StyleProperty, "StatisticsHit"); button.Click += (_, _) => Details(_model.Drill(_model.Snapshot.Filter.From, _model.Snapshot.Filter.To, share.Projects), share.Name); body.Children.Add(button);
        }
    }
    private FrameworkElement Heatmap(double width)
    {
        var first = new DateOnly(_model!.HeatmapYear, 1, 1); var last = new DateOnly(_model.HeatmapYear, 12, 31); var start = FocusBloom.Core.Calendar.CalendarRules.Monday(first);
        var columns = (last.DayNumber - start.DayNumber) / 7 + 1; var cell = Math.Min(15, (width - 26) / columns); var chart = new Canvas { Width = width, Height = cell * 7 + 42, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        var days = _model.Annual.Days.ToDictionary(day => day.Date); var max = Math.Max(1, days.Values.Select(day => day.Duration.TotalMinutes).DefaultIfEmpty().Max());
        var baseColor = ((SolidColorBrush)FindResource("SubtleBrush")).Color; var accent = ((SolidColorBrush)FindResource("AccentBrush")).Color;
        for (var weekday = 0; weekday < 7; weekday += 2) { var label = Text(new[] { "一", "二", "三", "四", "五", "六", "日" }[weekday], 9, true); Canvas.SetTop(label, 20 + weekday * cell); chart.Children.Add(label); }
        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var date = day; var col = (day.DayNumber - start.DayNumber) / 7; var row = ((int)day.DayOfWeek + 6) % 7; var data = days.GetValueOrDefault(day); var minutes = data?.Duration.TotalMinutes ?? 0;
            var strength = minutes > 0 ? .2 + .8 * Math.Sqrt(minutes / max) : 0;
            byte Blend(byte low, byte high) => (byte)(low + (high - low) * strength);
            var button = new Button { Width = cell - 2, Height = cell - 2, Background = new SolidColorBrush(Color.FromRgb(Blend(baseColor.R, accent.R), Blend(baseColor.G, accent.G), Blend(baseColor.B, accent.B))), Tag = date };
            DurationTooltip(button, day.ToString("yyyy-MM-dd"), data?.Duration ?? TimeSpan.Zero, data?.Count ?? 0);
            button.SetResourceReference(StyleProperty, "StatisticsHit"); button.Click += (_, _) => Details(_model.Drill(date, date), $"{date:yyyy年M月d日}");
            Canvas.SetLeft(button, 26 + col * cell); Canvas.SetTop(button, 20 + row * cell); chart.Children.Add(button);
            if (day.Day == 1) { var label = Text($"{day.Month}月", 9, true); Canvas.SetLeft(label, 26 + col * cell); chart.Children.Add(label); }
        }
        var legend = Text("浅色投入少，深色投入多", 10, true); Canvas.SetLeft(legend, 26); Canvas.SetTop(legend, 24 + cell * 7); chart.Children.Add(legend); return chart;
    }
    private FrameworkElement GoalProgressCard()
    {
            var goals = Card($"目标进度 · 截至{_model!.GoalDate:M月d日}", out var goalBody);
            goalBody.Children.Add(Text("按目标自身的项目与周期累计，点击查看或调整目标。", 11, true));
            foreach (var goal in _model.GoalProgress)
            {
                var content = new StackPanel { Margin = new Thickness(0, 6, 0, 6) }; content.Children.Add(Text(goal.Name + " · " + goal.ValueText, 13)); content.Children.Add(Text(goal.Scope + " · " + goal.Period + " · " + goal.State, 10, true));
                content.Children.Add(new ProgressBar { Minimum = 0, Maximum = 1, Value = goal.Ratio, Height = 5, Margin = new Thickness(0, 6, 0, 0), Foreground = (Brush)FindResource("AccentBrush"), Background = (Brush)FindResource("SubtleBrush") });
                var button = new Button { Content = content, Tag = goal.Id }; button.SetResourceReference(StyleProperty, "StatisticsHit"); button.Click += (_, _) => new GoalEditorWindow(Main, goal.Progress.Goal) { Owner = Window.GetWindow(this) }.ShowDialog(); goalBody.Children.Add(button);
            }
            if (_model.GoalProgress.Count == 0) goalBody.Children.Add(Text("所选范围暂无关联目标。", 12, true));
            return goals;
    }
    private void DrawReview()
    {
        var tasks = Card("待办与累计投入", out var taskBody); taskBody.Children.Add(Text(_model!.TaskSummary, 12, true));
        taskBody.Children.Add(Text("预估与累计投入比较，完成状态为当前状态。", 11, true));
        var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        foreach (var task in _model.Tasks)
        {
            var panel = new StackPanel(); var heading = Text(task.Title, 15); heading.FontWeight = FontWeights.SemiBold; panel.Children.Add(heading);
            panel.Children.Add(Text(task.Folder + " · " + task.Status, 11, true));
            panel.Children.Add(Text("累计 " + task.Invested + " · 预估 " + task.Estimated, 12));
            if (task.Difference.Length > 0) panel.Children.Add(Text(task.Difference, 11, true));
            if (task.NextStep.Length > 0) panel.Children.Add(Text("下一步 · " + task.NextStep, 11, true));
            var open = new Button { Content = "查看待办", Tag = task.Id, Padding = new Thickness(10, 5, 10, 5), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            open.Click += (_, _) => { var item = Main.Todos.Items.FirstOrDefault(item => item.Id == task.Id); if (item is null) { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), "这条待办已删除，历史投入仍保留。" ); return; } if (new TodoDetailsWindow(Main, item) { Owner = Window.GetWindow(this) }.ShowDialog() == true) ((MainWindow)Window.GetWindow(this)).ReturnToFocus(); }; panel.Children.Add(open);
            var border = new Border { Child = panel, Padding = new Thickness(14), CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 8, 8) }; border.SetResourceReference(Border.BackgroundProperty, "FieldBrush"); grid.Children.Add(border);
        }
        if (_model.Tasks.Count == 0) taskBody.Children.Add(Text("这个范围没有关联待办，选择项目或待办可查看累计投入。", 12, true)); else taskBody.Children.Add(grid); StatisticsContent.Children.Add(tasks);
        var outcomes = Card("成果与下一步", out var outcomeBody); outcomeBody.Children.Add(Text(_model.OutcomeSummary, 12, true));
        if (_model.QuantitySummary.Length > 0) outcomeBody.Children.Add(Text("数量成果 · " + _model.QuantitySummary, 14));
        foreach (var outcome in _model.Outcomes)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 8) }; var title = Text(outcome.Project + (outcome.Todo.Length > 0 ? " / " + outcome.Todo : ""), 14); title.FontWeight = FontWeights.SemiBold; panel.Children.Add(title);
            panel.Children.Add(Text(outcome.Date, 10, true)); if (outcome.Text.Length > 0) panel.Children.Add(Text(outcome.Text)); if (outcome.NextStep.Length > 0) panel.Children.Add(Text("下一步 · " + outcome.NextStep, 11, true));
            var button = new Button { Content = panel, Tag = outcome.Id }; button.SetResourceReference(StyleProperty, "StatisticsHit"); button.Click += (_, _) => new OutcomeEditorWindow(Main, outcome.Id) { Owner = Window.GetWindow(this) }.ShowDialog(); outcomeBody.Children.Add(button);
        }
        if (_model.Outcomes.Count == 0) outcomeBody.Children.Add(Text("还没有填写成果，可以从时间轴或图表明细补记。", 12, true)); StatisticsContent.Children.Add(outcomes);
    }
    private void Details(StatisticsSnapshot data, string title) => new StatisticsDetailsWindow(Main, _model!, data, title) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void Export_Click(object sender, RoutedEventArgs args)
    {
        if (_model is null || !_model.IsValid) return;
        var dialog = new SaveFileDialog { Title = "导出专注摘要", FileName = $"专注摘要-{_model.Snapshot.Filter.From:yyyyMMdd}-{_model.Snapshot.Filter.To:yyyyMMdd}", Filter = "Markdown 摘要 (*.md)|*.md|CSV 记录明细 (*.csv)|*.csv", AddExtension = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { var csv = dialog.FilterIndex == 2; System.IO.File.WriteAllText(dialog.FileName, _model.Export(csv), new System.Text.UTF8Encoding(csv)); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), "导出失败：" + ex.Message); }
    }
}
