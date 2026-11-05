using System;
using System.Collections.ObjectModel;
using System.Linq;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record HabitDayRow(Habit Habit, DateOnly Date, bool Checked, bool Planned, bool Future, int TotalDays, int Streak)
{
    private static readonly string[] Colors = { "#9EC9B0", "#D9ADAE", "#E1CD8D", "#A7BCD5", "#B9ABD2", "#DDB99B" };
    public Guid Id => Habit.Id;
    public string Name => Habit.Name;
    public string Color => Colors[Habit.Id.ToByteArray()[0] % Colors.Length];
    public bool CanCheck => !Future && (Checked || Planned);
    public string Schedule => HabitRow.ScheduleFor(Habit);
    public string Detail => Checked && Streak > 0 ? UiText.Format("已打卡 · 连续完成 {0} 个计划日", Streak)
        : Date < Habit.Start && !Checked && !Future ? UiText.Format("{0}开始", Habit.Start.ToString(UiText.IsEnglish ? "MMM d" : "M月d日", UiText.DateCulture))
        : UiText.Get(Checked ? "已打卡" : Future ? "未来计划" : Habit.Until < Date ? "已结束" : !Planned ? "休息日" : "点击打卡");
}

public sealed partial class GoalsViewModel
{
    public ObservableCollection<HabitDayRow> DayHabits { get; } = new();
    public ObservableCollection<GoalRow> DayGoals { get; } = new();
    public DateOnly Day => DateOnly.FromDateTime(Anchor);
    public string DayTitle => Day == Today ? UiText.Get("今天") : Day == Today.AddDays(-1) ? UiText.Get("昨天") : Day.ToString(UiText.IsEnglish ? "MMM d" : "M月d日", UiText.DateCulture);
    public string DayCaption => Day.ToString(UiText.IsEnglish ? "MMM d, yyyy · dddd" : "yyyy年M月d日 · dddd", UiText.DateCulture);
    public int DayPlanned => DayHabits.Count(row => row.Planned || row.Checked);
    public int DayDone => DayHabits.Count(row => row.Checked);
    public double DayRatio => DayPlanned == 0 ? 0 : (double)DayDone / DayPlanned;
    public string DaySummary => Day > Today ? UiText.Format("计划 {0} 项习惯", DayPlanned) : UiText.Format("已完成 {0} / {1} 项习惯", DayDone, DayPlanned);
    public string DayEncouragement => UiText.Get(Day > Today ? "先看计划，到了当天再开始。" : DayPlanned == 0 ? "这一天，也可以给自己留一点空白。" : DayDone == DayPlanned ? "这一天的小事，都认真完成了。" : DayDone == 0 ? "从最容易的一件开始。" : "已经向前走了一步，按自己的节奏继续。");
    public bool NoDayHabits => DayHabits.Count == 0;
    public bool HasDayGoals => DayGoals.Count > 0;
    private void RefreshDaily()
    {
        DayHabits.Clear(); DayGoals.Clear();
        foreach (var row in Habits)
        {
            var done = _completionChecks.Any(check => check.HabitId == row.Id && check.Date == Day);
            var streak = GoalRules.HabitStatus(row.Habit, _completionChecks, Day > Today ? Today : Day).Streak;
            var total = _completionChecks.Count(check => check.HabitId == row.Id && check.Date <= Day && check.Date <= Today);
            DayHabits.Add(new(row.Habit, Day, done, GoalRules.Due(row.Habit, Day), Day > Today, total, streak));
        }
        foreach (var row in Goals.Where(row => row.Progress.Goal.Start <= Day && (row.Progress.Goal.Until is null || row.Progress.Goal.Until >= Day)))
        {
            var progress = GoalRules.Progress(row.Progress.Goal, _completionRecords, _completionChecks, Day);
            DayGoals.Add(new(Day > Today ? progress with { Value = 0 } : progress, row.Scope));
        }
        foreach (var name in new[] { nameof(Day), nameof(DayTitle), nameof(DayCaption), nameof(DaySummary), nameof(DayEncouragement), nameof(DayRatio), nameof(NoDayHabits), nameof(HasDayGoals) }) Notify(name);
    }
}
