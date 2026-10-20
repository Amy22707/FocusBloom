using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record CompletionChoice(string Name, Guid? GoalId = null, Guid? HabitId = null);

public sealed partial class GoalsViewModel
{
    private string _view = "卡片";
    private DateTime _anchor;
    private CompletionChoice? _completionChoice;
    private IReadOnlyList<FocusRecord> _completionRecords = Array.Empty<FocusRecord>();
    private IReadOnlyList<HabitCheckIn> _completionChecks = Array.Empty<HabitCheckIn>();
    private IReadOnlyList<Habit> _completionHabits = Array.Empty<Habit>();
    public IReadOnlyList<string> Views { get; } = new[] { "日", "卡片", "周", "月", "年" };
    public ObservableCollection<CompletionChoice> CompletionChoices { get; } = new();
    public string View { get => _view; set { if (_view == value || !Views.Contains(value)) return; _view = value; _store.SaveSetting("goals.view", value); RefreshCompletion(); RefreshDaily(); Notify(); Notify(nameof(ShowCards)); Notify(nameof(ShowDaily)); Notify(nameof(ShowCompletion)); } }
    public bool ShowCards => View == "卡片";
    public bool ShowDaily => View == "日";
    public bool ShowCompletion => !ShowCards && !ShowDaily;
    public DateTime Anchor { get => _anchor; set { if (_anchor == value.Date) return; _anchor = value.Date; Notify(); RefreshCompletion(); RefreshDaily(); } }
    public CompletionChoice? SelectedCompletion { get => _completionChoice; set { if (value is null || _completionChoice == value) return; _completionChoice = value; Notify(); RefreshCompletion(); } }
    public DateOnly CompletionFrom { get; private set; }
    public DateOnly CompletionTo { get; private set; }
    public IReadOnlyList<CompletionDay> CompletionDays { get; private set; } = Array.Empty<CompletionDay>();
    public string CompletionLabel => View == "年" ? $"{Anchor:yyyy年}" : View == "月" ? $"{Anchor:yyyy年M月}" : $"{CompletionFrom:yyyy.M.d} — {CompletionTo:M.d}";
    public string CompletionSummary { get; private set; } = "";
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand TodayCommand { get; }
    public event EventHandler? CompletionChanged;
    private void ReloadCompletion(IReadOnlyList<FocusRecord> records, IReadOnlyList<HabitCheckIn> checks, IReadOnlyList<Habit> habits)
    {
        _completionRecords = records; _completionChecks = checks; _completionHabits = habits;
        var previous = _completionChoice; CompletionChoices.Clear(); CompletionChoices.Add(new("整体"));
        foreach (var goal in Goals) CompletionChoices.Add(new("目标 · " + goal.Name, GoalId: goal.Id));
        foreach (var habit in Habits) CompletionChoices.Add(new("习惯 · " + habit.Name, HabitId: habit.Id));
        _completionChoice = CompletionChoices.FirstOrDefault(item => item.GoalId == previous?.GoalId && item.HabitId == previous?.HabitId) ?? CompletionChoices[0];
        Notify(nameof(SelectedCompletion)); RefreshCompletion(); RefreshDaily();
    }
    private void MovePeriod(int offset) => Anchor = View == "日" ? Anchor.AddDays(offset) : View == "年" ? Anchor.AddYears(offset) : View == "月" ? Anchor.AddMonths(offset) : Anchor.AddDays(7 * offset);
    private void RefreshCompletion()
    {
        if (!ShowCompletion) return;
        var anchor = DateOnly.FromDateTime(Anchor);
        CompletionFrom = View == "年" ? new(anchor.Year, 1, 1) : View == "月" ? new(anchor.Year, anchor.Month, 1) : FocusBloom.Core.Calendar.CalendarRules.Monday(anchor);
        CompletionTo = View == "年" ? new(anchor.Year, 12, 31) : View == "月" ? new(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)) : CompletionFrom.AddDays(6);
        var goals = Goals.Select(item => item.Progress.Goal).Where(item => SelectedCompletion?.HabitId is null && (SelectedCompletion?.GoalId is null || item.Id == SelectedCompletion.GoalId)).ToArray();
        var habits = _completionHabits.Where(item => !item.Deleted && SelectedCompletion?.GoalId is null && (SelectedCompletion?.HabitId is null || item.Id == SelectedCompletion.HabitId)).ToArray();
        CompletionDays = GoalCompletion.Build(goals, habits, _completionRecords, _completionChecks, CompletionFrom, CompletionTo, Today);
        var planned = CompletionDays.Where(day => !day.Future && day.Entries.Count > 0).ToArray();
        CompletionSummary = planned.Length == 0 ? "所选范围暂无已到期的计划" : $"平均完成度 {planned.Average(day => day.Ratio):P0} · 全部完成 {planned.Count(day => day.Ratio >= 1)} / {planned.Length} 天";
        Notify(nameof(CompletionLabel)); Notify(nameof(CompletionSummary)); CompletionChanged?.Invoke(this, EventArgs.Empty);
    }
}
