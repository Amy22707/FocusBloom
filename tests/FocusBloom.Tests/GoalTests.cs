using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class GoalTests
{
    private static DateTimeOffset At(DateOnly day, int hour, int minute) { var local = day.ToDateTime(new(hour, minute)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
    [Fact]
    public void GoalsUseActualFocusIntervalsAndEndDateCountsAcrossCycleBoundaries()
    {
        var sunday = new DateOnly(2026, 10, 11); var project = Guid.NewGuid();
        var record = new FocusRecord(Guid.NewGuid(), project, TimerMode.Pomodoro, At(sunday, 23, 45), At(sunday.AddDays(1), 0, 20), EndReason.CountdownElapsed,
            new[] { new FocusInterval(IntervalKind.Focus, At(sunday, 23, 45), At(sunday, 23, 55)), new FocusInterval(IntervalKind.Pause, At(sunday, 23, 55), At(sunday.AddDays(1), 0, 5)), new FocusInterval(IntervalKind.Focus, At(sunday.AddDays(1), 0, 5), At(sunday.AddDays(1), 0, 20)) });
        var goal = new FocusGoal(Guid.NewGuid(), "每日一刻", GoalMetric.Minutes, 15, GoalCycle.Daily, sunday.AddDays(-7), ProjectId: project);
        var first = GoalRules.Progress(goal, new[] { record }, Array.Empty<HabitCheckIn>(), sunday); Assert.Equal(10, first.Value); Assert.False(first.Complete);
        var next = GoalRules.Progress(goal, new[] { record }, Array.Empty<HabitCheckIn>(), sunday.AddDays(1)); Assert.Equal(15, next.Value); Assert.True(next.Complete);
        Assert.Equal(0, GoalRules.Progress(goal with { Metric = GoalMetric.Sessions }, new[] { record }, Array.Empty<HabitCheckIn>(), sunday).Value);
        Assert.Equal(1, GoalRules.Progress(goal with { Metric = GoalMetric.Sessions }, new[] { record }, Array.Empty<HabitCheckIn>(), sunday.AddDays(1)).Value);
        Assert.Equal(10, GoalRules.Progress(goal with { Cycle = GoalCycle.Weekly }, new[] { record }, Array.Empty<HabitCheckIn>(), sunday).Value);
        Assert.Equal(15, GoalRules.Progress(goal with { Cycle = GoalCycle.Weekly }, new[] { record }, Array.Empty<HabitCheckIn>(), sunday.AddDays(1)).Value);
        Assert.Equal(0, GoalRules.Progress(goal with { ProjectId = Guid.NewGuid() }, new[] { record }, Array.Empty<HabitCheckIn>(), sunday).Value);
    }
    [Fact]
    public void FutureAndEndedGoalsKeepTheirDatesAndCheckInDaysAreDistinct()
    {
        var today = new DateOnly(2026, 10, 15); var habit = Guid.NewGuid(); var goal = new FocusGoal(Guid.NewGuid(), "阅读30天", GoalMetric.CheckInDays, 30, GoalCycle.Total, today, HabitId: habit);
        var checks = new[] { new HabitCheckIn(habit, today), new HabitCheckIn(habit, today), new HabitCheckIn(Guid.NewGuid(), today), new HabitCheckIn(habit, today.AddDays(1)) };
        Assert.Equal(1, GoalRules.Progress(goal, Array.Empty<FocusRecord>(), checks, today).Value);
        var planned = GoalRules.Progress(goal with { Start = today.AddDays(5) }, Array.Empty<FocusRecord>(), checks, today); Assert.False(planned.Active); Assert.Equal(0, planned.Value); Assert.True(planned.To >= planned.From);
        Assert.Contains("开始", new GoalRow(planned, "阅读").State);
        var ended = GoalRules.Progress(goal with { Until = today.AddDays(1) }, Array.Empty<FocusRecord>(), checks, today.AddDays(10)); Assert.False(ended.Active); Assert.Equal(2, ended.Value); Assert.Equal(today.AddDays(1), ended.To);
    }
    [Fact]
    public void HabitStreakSkipsOffDaysAndDoesNotBreakForPendingToday()
    {
        var friday = new DateOnly(2026, 10, 9); var habit = new Habit(Guid.NewGuid(), "工作日阅读", new[] { 1, 2, 3, 4, 5 }, friday);
        var checks = new[] { new HabitCheckIn(habit.Id, friday), new HabitCheckIn(habit.Id, friday.AddDays(3)) };
        Assert.False(GoalRules.Due(habit, friday.AddDays(1))); Assert.True(GoalRules.Due(habit, friday.AddDays(3)));
        var pending = GoalRules.HabitStatus(habit, checks, friday.AddDays(4)); Assert.Equal(2, pending.Streak); Assert.Equal(2, pending.Completed); Assert.Equal(3, pending.Planned);
        Assert.Equal(3, GoalRules.HabitStatus(habit, checks.Append(new(habit.Id, friday.AddDays(4))), friday.AddDays(4)).Streak);
        Assert.Equal(0, GoalRules.HabitStatus(habit, checks, friday.AddDays(5)).Streak);
        Assert.False(GoalRules.Due(habit with { Until = friday.AddDays(3) }, friday.AddDays(4)));
    }
    [Fact]
    public void CheckedDeletedAndOffDaysHaveNoHabitReminderAndFutureStartsAreScheduled()
    {
        var today = new DateOnly(2026, 10, 9); var habit = new Habit(Guid.NewGuid(), "阅读", new[] { 1, 2, 3, 4, 5 }, today, Reminder: true, ReminderTime: new(20, 0));
        var reminders = HabitReminders.Build(new[] { habit }, Array.Empty<HabitCheckIn>(), today); Assert.Contains(reminders, item => DateOnly.FromDateTime(item.At.LocalDateTime) == today); Assert.All(reminders, item => Assert.True(GoalRules.Due(habit, DateOnly.FromDateTime(item.At.LocalDateTime))));
        var checkedToday = HabitReminders.Build(new[] { habit }, new[] { new HabitCheckIn(habit.Id, today) }, today); Assert.DoesNotContain(checkedToday, item => DateOnly.FromDateTime(item.At.LocalDateTime) == today);
        Assert.Empty(HabitReminders.Build(new[] { habit with { Deleted = true } }, Array.Empty<HabitCheckIn>(), today)); Assert.Empty(HabitReminders.Build(new[] { habit with { Reminder = false } }, Array.Empty<HabitCheckIn>(), today));
        var future = habit with { Start = today.AddMonths(8) }; Assert.NotEmpty(HabitReminders.Build(new[] { future }, Array.Empty<HabitCheckIn>(), today));
        Assert.All(reminders, item => Assert.Equal(20, item.At.LocalDateTime.Hour)); Assert.Equal(reminders.Count, reminders.Select(item => item.Key).Distinct().Count());
    }
    [Fact]
    public void StatisticsGoalsFollowRelatedHabitProjectAndKeepGlobalGoals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomGoalStatistics", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var first = store.GetProjects()[0].Id;
            var second = new FocusBloom.Core.Projects.FocusProject(Guid.NewGuid(), "英语", "学习", "#7B97A6"); store.SaveProject(second);
            var habit = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), today, ProjectId: first); store.SaveHabit(habit); store.SetHabitCheckIn(habit.Id, today, true);
            store.SaveGoal(new(Guid.NewGuid(), "阅读两天", GoalMetric.CheckInDays, 2, GoalCycle.Total, today, HabitId: habit.Id));
            store.SaveGoal(new(Guid.NewGuid(), "全项目每日两次", GoalMetric.Sessions, 2, GoalCycle.Daily, today));
            var model = new StatisticsViewModel(store) { Range = "日", Anchor = DateTime.Today }; Assert.Equal(2, model.GoalProgress.Count);
            model.SelectedProject = model.ProjectChoices.Single(item => item.Id == second.Id); Assert.Equal("全项目每日两次", Assert.Single(model.GoalProgress).Name);
            model.SelectedProject = model.ProjectChoices.Single(item => item.Id == first); Assert.Equal(2, model.GoalProgress.Count); Assert.Contains("阅读两天", model.Export(false));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void CheckInsAndDefinitionsSurviveReopenDeletionPreservesHistoryAndPastCheckInsUpdateGoals()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomGoalData", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var clock = new ManualClock(); var status = ""; var model = new GoalsViewModel(store, text => status = text, clock); var today = model.Today;
            var habit = new Habit(Guid.NewGuid(), "每日阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-3)); var goal = new FocusGoal(Guid.NewGuid(), "累计阅读", GoalMetric.CheckInDays, 30, GoalCycle.Total, today.AddDays(-3), HabitId: habit.Id);
            model.SaveHabit(habit); model.SaveGoal(goal); Assert.True(model.CheckIn(habit.Id, today)); Assert.Equal(1, Assert.Single(model.Goals).Progress.Value);
            Assert.True(model.CheckIn(habit.Id, today.AddDays(-1))); Assert.Equal(2, Assert.Single(model.Goals).Progress.Value); Assert.False(model.CheckIn(habit.Id, today.AddDays(-4))); Assert.False(model.CheckIn(habit.Id, today.AddDays(1))); Assert.NotEmpty(status);
            store.SetHabitCheckIn(habit.Id, today, true); Assert.Equal(2, store.GetHabitCheckIns().Count);
            store = new(path); Assert.Single(store.GetGoals()); Assert.Equal(habit.Weekdays, Assert.Single(store.GetHabits()).Weekdays); Assert.Equal(2, store.GetHabitCheckIns().Count);
            model = new(store, _ => { }, clock); Assert.True(Assert.Single(model.Habits).CheckedToday); Assert.True(model.CheckIn(habit.Id, today)); Assert.Single(store.GetHabitCheckIns()); Assert.True(model.CheckIn(habit.Id, today.AddDays(-1))); Assert.Empty(store.GetHabitCheckIns());
            model.CheckIn(habit.Id, today); clock.Advance(TimeSpan.FromDays(1)); model.Reload(); Assert.False(Assert.Single(model.Habits).CheckedToday); Assert.Equal(1, Assert.Single(model.Goals).Progress.Value);
            model.DeleteHabit(habit.Id); Assert.Empty(store.GetHabits()); Assert.True(Assert.Single(store.GetHabits(true)).Deleted); Assert.Single(store.GetHabitCheckIns()); Assert.Equal(1, Assert.Single(model.Goals).Progress.Value);
            model.DeleteGoal(goal.Id); Assert.Empty(store.GetGoals()); Assert.Single(store.GetHabitCheckIns());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
