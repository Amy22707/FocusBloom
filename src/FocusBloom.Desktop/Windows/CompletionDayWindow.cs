using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class CompletionDayWindow : CalendarFormWindow
{
    private readonly DateOnly _date;
    public CompletionDayWindow(MainViewModel main, DateOnly date) : base(main, date.ToString("yyyy年M月d日") + " · 完成情况")
    { _date = date; SaveButton.Content = "关闭"; Render(); }
    private void Render()
    {
        Fields.Children.Clear(); var day = Main.Goals.CompletionDays.Single(item => item.Date == _date);
        Caption(day.Future ? "未来计划" : day.Entries.Count == 0 ? "这一天没有计划" : $"完成度 {day.Ratio:P0} · 已完成 {day.Completed}/{day.Entries.Count}");
        foreach (var entry in day.Entries)
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
            row.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var label = new TextBlock { Text = day.Future ? "待执行" : entry.IsHabit ? entry.Complete ? "已打卡" : "未打卡" : GoalRow.Amount(entry.Metric, entry.Value) + " / " + GoalRow.Amount(entry.Metric, entry.Target), Margin = new Thickness(0, 5, 0, 6) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); row.Children.Add(label);
            if (entry.IsHabit && Main.Calendar.Store.GetHabits().FirstOrDefault(item => item.Id == entry.Id) is { } habit)
            {
                var button = new Button { Content = "打卡日历", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(9, 5, 9, 5), FontSize = 12 };
                button.Click += (_, _) => { new HabitHistoryWindow(Main, habit, _date) { Owner = this }.ShowDialog(); Render(); }; row.Children.Add(button);
            }
            Fields.Children.Add(row);
        }
    }
    protected override void Save() => Close();
}
