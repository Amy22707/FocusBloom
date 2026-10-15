using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Desktop.Windows;

internal sealed class CourseMeetingEditor : Expander
{
    private sealed record PeriodChoice(int Number, string Name);
    public Guid Id { get; }
    public ComboBox Weekday { get; }
    public ComboBox Start { get; }
    public ComboBox End { get; }
    public ComboBox Pattern { get; }
    public TextBox Weeks { get; }
    public TextBox Room { get; }
    public Button RemoveButton { get; }
    private readonly TextBlock _heading = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    public CourseMeetingEditor(CourseMeeting item, SchoolTerm term, Action changed, Action remove)
    {
        Id = item.Id; Margin = new Thickness(0, 0, 0, 10); HorizontalContentAlignment = HorizontalAlignment.Stretch;
        SetResourceReference(ForegroundProperty, "TextBrush");
        var header = new DockPanel(); RemoveButton = new Button { Content = "移除", Padding = new Thickness(8, 3, 8, 3), FontSize = 11, Margin = new Thickness(12, 0, 0, 0) };
        RemoveButton.Click += (_, _) => remove(); DockPanel.SetDock(RemoveButton, Dock.Right); header.Children.Add(RemoveButton); header.Children.Add(_heading); Header = header;
        var fields = new StackPanel(); var border = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(10), Child = fields, Margin = new Thickness(0, 8, 0, 0) };
        border.SetResourceReference(Border.BackgroundProperty, "FieldBrush"); Content = border;
        void Caption(Panel panel, string text) { var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 8, 0, 6) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(label); }
        Caption(fields, "星期"); Weekday = new ComboBox { ItemsSource = new[] { "周一", "周二", "周三", "周四", "周五", "周六", "周日" }, SelectedIndex = item.Weekday - 1 }; fields.Children.Add(Weekday);
        var periods = term.Periods.Select(period => new PeriodChoice(period.Number, $"第{period.Number}节 · {period.Start:HH:mm}")).ToArray();
        var ends = term.Periods.Select(period => new PeriodChoice(period.Number, $"第{period.Number}节 · {period.End:HH:mm}")).ToArray();
        Start = new ComboBox { ItemsSource = periods, DisplayMemberPath = "Name", SelectedItem = periods.FirstOrDefault(period => period.Number == item.StartPeriod) ?? periods[0] };
        End = new ComboBox { ItemsSource = ends, DisplayMemberPath = "Name", SelectedItem = ends.FirstOrDefault(period => period.Number == item.EndPeriod) ?? ends[0] };
        var row = new Grid(); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = new GridLength(12) }); row.ColumnDefinitions.Add(new());
        var a = new StackPanel(); Caption(a, "开始节次"); a.Children.Add(Start); row.Children.Add(a);
        var b = new StackPanel(); Caption(b, "结束节次"); b.Children.Add(End); Grid.SetColumn(b, 2); row.Children.Add(b); fields.Children.Add(row);
        Caption(fields, "适用周次 / 1-16或1,3,5-8"); Weeks = new TextBox { Text = item.Weeks.Length > 0 ? item.Weeks : $"{item.FirstWeek}-{item.LastWeek}" }; fields.Children.Add(Weeks);
        Caption(fields, "周次规则"); Pattern = new ComboBox { ItemsSource = new[] { "每周", "单周", "双周" }, SelectedIndex = (int)item.Pattern }; fields.Children.Add(Pattern);
        Caption(fields, "这个时段的教室"); Room = new TextBox { Text = item.Room }; fields.Children.Add(Room);
        void Update() { _heading.Text = $"{Weekday.SelectedItem} · 第{((PeriodChoice)Start.SelectedItem).Number}—{((PeriodChoice)End.SelectedItem).Number}节 · {Pattern.SelectedItem}"; changed(); }
        Weekday.SelectionChanged += (_, _) => Update();
        Start.SelectionChanged += (_, _) => { End.SelectedIndex = Math.Min(Start.SelectedIndex + 1, ends.Length - 1); Update(); };
        End.SelectionChanged += (_, _) => Update(); Pattern.SelectionChanged += (_, _) => Update(); Weeks.TextChanged += (_, _) => Update(); Room.TextChanged += (_, _) => Update();
        _heading.Text = $"{Weekday.SelectedItem} · 第{item.StartPeriod}—{item.EndPeriod}节 · {Pattern.SelectedItem}";
    }
    public CourseMeeting Read(int termWeeks)
    {
        var weeks = CalendarRules.ParseWeeks(Weeks.Text);
        if (weeks.Count == 0 || weeks.Max() > termWeeks || Start.SelectedItem is not PeriodChoice start || End.SelectedItem is not PeriodChoice end || end.Number < start.Number)
            throw new FormatException("请为每个上课时段填写学期内周次，以及有效的开始和结束节次。");
        return new(Id, Weekday.SelectedIndex + 1, start.Number, end.Number, weeks.Min(), weeks.Max(), (WeekPattern)Pattern.SelectedIndex, Weeks.Text.Trim(), Room.Text.Trim());
    }
}
