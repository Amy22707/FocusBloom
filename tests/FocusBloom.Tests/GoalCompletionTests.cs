using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using Xunit;

namespace FocusBloom.Tests;

public sealed class GoalCompletionTests
{
    [Fact]
    public void CompletionUsesDailyPlanProgressAndKeepsFutureAndOffDaysSeparate()
    {
        var monday = new DateOnly(2026, 10, 12); var today = monday.AddDays(2);
        var reading = new Habit(Guid.NewGuid(), "阅读", new[] { 1, 2, 3, 4, 5 }, monday);
        var weekend = new Habit(Guid.NewGuid(), "周末运动", new[] { 6, 7 }, monday);
        var goal = new FocusGoal(Guid.NewGuid(), "阅读两天", GoalMetric.CheckInDays, 2, GoalCycle.Weekly, monday, HabitId: reading.Id);
        var checks = new[] { new HabitCheckIn(reading.Id, monday), new HabitCheckIn(reading.Id, today) };
        var days = GoalCompletion.Build(new[] { goal }, new[] { reading, weekend }, Array.Empty<FocusRecord>(), checks, monday.AddDays(-1), monday.AddDays(6), today);
        Assert.Empty(days[0].Entries); Assert.Equal(.75, days[1].Ratio); Assert.Equal(.25, days[2].Ratio); Assert.Equal(1, days[3].Ratio); Assert.Equal(2, days[3].Completed);
        Assert.True(days[4].Future); Assert.Equal(0, days[4].Ratio); Assert.Equal(2, days[4].Entries.Count); Assert.Contains(days[^1].Entries, item => item.Id == weekend.Id); Assert.DoesNotContain(days[^1].Entries, item => item.Id == reading.Id);
        var single = GoalCompletion.Build(Array.Empty<FocusGoal>(), new[] { reading }, Array.Empty<FocusRecord>(), checks, monday, monday.AddDays(6), today);
        Assert.Equal(new[] { 1d, 0d, 1d }, single.Take(3).Select(day => day.Ratio)); Assert.Empty(single[^1].Entries);
    }
    [Fact]
    public void CalendarGoalAmountsMatchGoalProgressAcrossDaysWeeksMonthsAndTotalCycles()
    {
        var start = new DateOnly(2026, 10, 30); var today = start.AddDays(4); var project = Guid.NewGuid(); var habit = Guid.NewGuid();
        DateTimeOffset At(DateOnly day, int hour) { var local = day.ToDateTime(new(hour, 0)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
        var records = new[] { new FocusRecord(Guid.NewGuid(), project, TimerMode.CountUp, At(start, 23), At(start.AddDays(1), 1), EndReason.Manual,
            new[] { new FocusInterval(IntervalKind.Focus, At(start, 23), At(start.AddDays(1), 0)), new FocusInterval(IntervalKind.Pause, At(start.AddDays(1), 0), At(start.AddDays(1), 1)) }),
            new FocusRecord(Guid.NewGuid(), project, TimerMode.CountUp, At(start.AddDays(3), 9), At(start.AddDays(3), 10), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, At(start.AddDays(3), 9), At(start.AddDays(3), 10)) }) };
        var checks = new[] { new HabitCheckIn(habit, start), new HabitCheckIn(habit, start.AddDays(2)) };
        foreach (var metric in Enum.GetValues<GoalMetric>()) foreach (var cycle in Enum.GetValues<GoalCycle>())
        {
            var goal = new FocusGoal(Guid.NewGuid(), "目标", metric, metric == GoalMetric.Minutes ? 120 : 2, cycle, start, today, project, habit);
            var days = GoalCompletion.Build(new[] { goal }, Array.Empty<Habit>(), records, checks, start.AddDays(-1), today.AddDays(1), today);
            Assert.Empty(days[0].Entries); Assert.Empty(days[^1].Entries);
            foreach (var day in days.Where(day => day.Entries.Count > 0))
            {
                var progress = GoalRules.Progress(goal, records, checks, day.Date); Assert.Equal(progress.Value, Assert.Single(day.Entries).Value); Assert.Equal(progress.Ratio, day.Ratio);
            }
        }
    }
}
