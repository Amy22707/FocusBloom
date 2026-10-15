using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class HabitHistoryWindow : CalendarFormWindow
{
    private readonly Habit _habit;
    private readonly DatePicker _date;
    private readonly UniformGrid _calendar = new() { Columns = 7, Rows = 7, Height = 300 };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 10, 0, 12), TextWrapping = TextWrapping.Wrap };
    public HabitHistoryWindow(MainViewModel main, Habit habit, DateOnly? selectedDate = null) : base(main, habit.Name + " · 打卡日历")
    {
        _habit = habit; SaveButton.Content = "关闭"; _date = DateInput("MonthInput", "选择月份", selectedDate ?? main.Goals.Today);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        foreach (var (text, offset) in new[] { ("‹ 上月", -1), ("本月", 0), ("下月 ›", 1) }) { var button = new Button { Content = text, Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) }; button.Click += (_, _) => _date.SelectedDate = offset == 0 ? main.Goals.Today.ToDateTime(TimeOnly.MinValue) : (_date.SelectedDate ?? DateTime.Today).AddMonths(offset); buttons.Children.Add(button); }
        Fields.Children.Add(buttons); Fields.Children.Add(_summary); Fields.Children.Add(_calendar); RegisterName("HistoryCalendar", _calendar);
        Caption("实心色块表示已打卡，边框表示计划执行日。点击今天或过去的计划日可打卡，撤销前需确认；未来日期仅查看。"); _date.SelectedDateChanged += (_, _) => Render();
        main.Goals.Changed += HistoryChanged; Closed += (_, _) => main.Goals.Changed -= HistoryChanged; Render(); Finish();
    }
    private void HistoryChanged(object? sender, EventArgs args) => Render();
    private void Render()
    {
        _calendar.Children.Clear(); var date = DateOnly.FromDateTime(_date.SelectedDate ?? DateTime.Today); var first = new DateOnly(date.Year, date.Month, 1); var start = FocusBloom.Core.Calendar.CalendarRules.Monday(first);
        var checkedDays = Main.Calendar.Store.GetHabitCheckIns().Where(item => item.HabitId == _habit.Id).Select(item => item.Date).ToHashSet();
        var completed = checkedDays.Count(day => day.Year == date.Year && day.Month == date.Month); _summary.Text = $"{date:yyyy年M月} · 已打卡 {completed} 天";
        foreach (var day in new[] { "一", "二", "三", "四", "五", "六", "日" }) _calendar.Children.Add(new TextBlock { Text = day, HorizontalAlignment = HorizontalAlignment.Center });
        for (var index = 0; index < 42; index++)
        {
            var day = start.AddDays(index); var checkedIn = checkedDays.Contains(day); var label = new TextBlock { Text = day.Day.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            label.SetResourceReference(TextBlock.ForegroundProperty, checkedIn ? "PrimaryTextBrush" : "TextBrush"); label.FontWeight = checkedIn ? FontWeights.SemiBold : FontWeights.Normal;
            var button = new Button { Content = label, Tag = day, IsEnabled = day <= Main.Goals.Today && !_habit.Deleted && (checkedIn || GoalRules.Due(_habit, day)) }; button.SetResourceReference(StyleProperty, "CompletionDateButton");
            System.Windows.Automation.AutomationProperties.SetName(button, $"{day:yyyy-MM-dd}" + (checkedIn ? " 已打卡" : " 未打卡"));
            button.Click += (_, _) => { if (checkedIn && !ConfirmDeletionWindow.Ask(this, $"撤销 {day:yyyy-MM-dd} 的打卡？")) return; Main.Goals.CheckIn(_habit.Id, day); };
            var card = new Border { Child = button, CornerRadius = new CornerRadius(6), Margin = new Thickness(2), BorderThickness = new Thickness(GoalRules.Due(_habit, day) ? 1 : 0), Tag = day, ToolTip = $"{day:yyyy-MM-dd}" + (checkedIn ? " · 已打卡" : day > Main.Goals.Today ? " · 未来日期" : GoalRules.Due(_habit, day) ? " · 点击打卡" : " · 不在执行计划内") };
            card.SetResourceReference(Border.BackgroundProperty, checkedIn ? "AccentBrush" : "SubtleBrush"); card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); if (day.Month != date.Month) card.Opacity = .35; _calendar.Children.Add(card);
        }
    }
    protected override void Save() => Close();
}
