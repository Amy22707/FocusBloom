using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record GoalRow(GoalProgress Progress, string Scope)
{
    public Guid Id => Progress.Goal.Id;
    public string Name => Progress.Goal.Name;
    public string Period => Progress.Goal.Cycle == GoalCycle.Daily ? $"每天 · {Progress.From:M/d}" : Progress.Goal.Cycle == GoalCycle.Total && Progress.Goal.Until is null ? $"累计 · {Progress.From:M/d}起" : (Progress.Goal.Cycle switch { GoalCycle.Weekly => "每周", GoalCycle.Monthly => "每月", _ => "累计" }) + $" · {Progress.From:M/d}—{Progress.To:M/d}";
    public string ValueText => Amount(Progress.Goal.Metric, Progress.Value) + " / " + Amount(Progress.Goal.Metric, Progress.Goal.Target);
    public double Ratio => Progress.Ratio;
    public string State => !Progress.Active ? Progress.Goal.Start > Progress.AsOf ? $"{Progress.Goal.Start:M月d日}开始" : Progress.Complete ? "已结束 · 已达成" : "已结束" : Progress.Complete ? "已达成 ✓" : "还差 " + Amount(Progress.Goal.Metric, Progress.Goal.Target - Progress.Value);
    public static string Amount(GoalMetric metric, double value) => metric == GoalMetric.Minutes ? value >= 60 && value % 60 == 0 ? $"{value / 60:0}小时" : StatisticsViewModel.Duration(TimeSpan.FromMinutes(value)) : $"{value:0.##}" + (metric == GoalMetric.Sessions ? "次" : "天");
}
public sealed record HabitRow(Habit Habit, HabitProgress Progress, bool CheckedToday, bool CanCheck, string Scope, DateOnly Today)
{
    public Guid Id => Habit.Id;
    public string Name => Habit.Name;
    public string Schedule => ScheduleFor(Habit);
    public static string ScheduleFor(Habit habit) => habit.Weekdays.Count == 7 ? "每天" : habit.Weekdays.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) ? "工作日" : string.Join("、", habit.Weekdays.Select(day => "周" + "一二三四五六日"[day - 1]));
    public string Streak => $"连续完成 {Progress.Streak} 个计划日";
    public string Month => $"本月 {Progress.Completed} / {Progress.Planned}" + (Progress.Planned > 0 ? $" · {(double)Progress.Completed / Progress.Planned:P0}" : "");
    public string Reminder => Habit.Reminder ? $"{Habit.ReminderTime ?? new TimeOnly(20, 0):HH:mm} 提醒" : "未开启提醒";
    public string CheckText => CheckedToday ? "已打卡 ✓" : Habit.Start > Today ? "尚未开始" : Habit.Until < Today ? "已结束" : !CanCheck ? "今天不执行" : "今天打卡";
}

public sealed partial class GoalsViewModel : ObservableObject
{
    private sealed record SavedOrder(Guid[] Goals, Guid[] Habits);
    private readonly IFocusStore _store;
    private readonly Action<string> _report;
    private readonly TimeProvider _clock;
    private DateOnly _lastToday;
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public string TodayText => $"{Today:yyyy年M月d日} · 专注结束后自动更新目标";
    public ObservableCollection<GoalRow> Goals { get; } = new();
    public ObservableCollection<HabitRow> Habits { get; } = new();
    public bool NoGoals => Goals.Count == 0;
    public bool NoHabits => Habits.Count == 0;
    public event EventHandler? Changed;
    public GoalsViewModel(IFocusStore store, Action<string> report, TimeProvider? clock = null)
    {
        _store = store; _report = report; _clock = clock ?? TimeProvider.System; _lastToday = Today; _anchor = Today.ToDateTime(TimeOnly.MinValue);
        _view = store.GetSetting("goals.view") is { } view && Views.Contains(view) ? view : "日";
        PreviousCommand = new(() => MovePeriod(-1)); NextCommand = new(() => MovePeriod(1)); TodayCommand = new(() => Anchor = Today.ToDateTime(TimeOnly.MinValue)); Reload();
    }
    public void Reload()
    {
        if (Today != _lastToday && Day == _lastToday) { _anchor = Today.ToDateTime(TimeOnly.MinValue); Notify(nameof(Anchor)); } _lastToday = Today;
        var records = _store.GetRecords(); var checks = _store.GetHabitCheckIns(); var habits = _store.GetHabits(true).ToDictionary(item => item.Id); var projects = _store.GetProjects(true).ToDictionary(item => item.Id);
        Goals.Clear(); Habits.Clear();
        foreach (var goal in _store.GetGoals())
        {
            var scope = goal.Metric == GoalMetric.CheckInDays ? "习惯 · " + (goal.HabitId is { } habit && habits.TryGetValue(habit, out var linked) ? linked.Name + (linked.Deleted ? "（已删除）" : "") : "未关联") : goal.ProjectId is { } project ? projects.GetValueOrDefault(project)?.Name ?? "历史项目" : "全部项目";
            Goals.Add(new(GoalRules.Progress(goal, records, checks, Today), scope));
        }
        foreach (var habit in habits.Values.Where(item => !item.Deleted))
        {
            var done = checks.Any(item => item.HabitId == habit.Id && item.Date == Today);
            Habits.Add(new(habit, GoalRules.HabitStatus(habit, checks, Today), done, done || GoalRules.Due(habit, Today), habit.ProjectId is { } id ? projects.GetValueOrDefault(id)?.Name ?? "历史项目" : "不限定项目", Today));
        }
        if (_store.GetSetting("goals.order") is { } json && JsonSerializer.Deserialize<SavedOrder>(json) is { } order) { CollectionOrder.Apply(Goals, order.Goals, item => item.Id); CollectionOrder.Apply(Habits, order.Habits, item => item.Id); }
        Notify(nameof(TodayText)); Notify(nameof(NoGoals)); Notify(nameof(NoHabits));
        ReloadCompletion(records, checks, habits.Values.ToArray());
    }
    public void SaveGoal(FocusGoal goal) { _store.SaveGoal(goal); Updated(); }
    public void DeleteGoal(Guid id) { _store.DeleteGoal(id); Updated(); }
    public void SaveHabit(Habit habit) { _store.SaveHabit(habit); Updated(); }
    public void DeleteHabit(Guid id) { _store.DeleteHabit(id); Updated(); }
    public bool CheckIn(Guid habitId, DateOnly date)
    {
        var habit = _store.GetHabits().FirstOrDefault(item => item.Id == habitId);
        var done = _store.GetHabitCheckIns().Any(item => item.HabitId == habitId && item.Date == date);
        if (habit is null || date > Today || !done && !GoalRules.Due(habit, date)) { _report("请选择今天或过去的计划执行日，未来日期不能打卡。"); return false; }
        _store.SetHabitCheckIn(habitId, date, !done); Updated(); return true;
    }
    public void MoveGoal(GoalRow row, OrderMove direction, GoalRow? target) { if (CollectionOrder.Move(Goals, row, direction, targetItem: target)) { SaveOrder(); RefreshDaily(); } }
    public void MoveHabit(HabitRow row, OrderMove direction, HabitRow? target) { if (CollectionOrder.Move(Habits, row, direction, targetItem: target)) { SaveOrder(); RefreshDaily(); } }
    private void SaveOrder() => _store.SaveSetting("goals.order", JsonSerializer.Serialize(new SavedOrder(Goals.Select(item => item.Id).ToArray(), Habits.Select(item => item.Id).ToArray())));
    private void Updated() { Reload(); Changed?.Invoke(this, EventArgs.Empty); }
}
