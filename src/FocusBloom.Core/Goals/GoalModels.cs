using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;

namespace FocusBloom.Core.Goals;

public enum GoalMetric { Minutes, Sessions, CheckInDays }
public enum GoalCycle { Daily, Weekly, Monthly, Total }
public sealed record FocusGoal(Guid Id, string Name, GoalMetric Metric, double Target, GoalCycle Cycle,
    DateOnly Start, DateOnly? Until = null, Guid? ProjectId = null, Guid? HabitId = null);
public sealed record Habit(Guid Id, string Name, IReadOnlyList<int> Weekdays, DateOnly Start, DateOnly? Until = null,
    bool Reminder = false, TimeOnly? ReminderTime = null, Guid? ProjectId = null, bool Deleted = false);
public sealed record HabitCheckIn(Guid HabitId, DateOnly Date);
public sealed record GoalProgress(FocusGoal Goal, DateOnly From, DateOnly To, double Value, bool Active, DateOnly AsOf)
{
    public bool Complete => Value >= Goal.Target;
    public double Ratio => Math.Min(1, Value / Goal.Target);
}
public sealed record HabitProgress(int Completed, int Planned, int Streak);

public static class GoalRules
{
    public static bool Due(Habit habit, DateOnly date) => !habit.Deleted && date >= habit.Start && (habit.Until is null || date <= habit.Until)
        && habit.Weekdays.Contains(((int)date.DayOfWeek + 6) % 7 + 1);
    public static (DateOnly From, DateOnly To) Period(FocusGoal goal, DateOnly date)
    {
        var anchor = date < goal.Start ? goal.Start : goal.Until is { } last && date > last ? last : date;
        var from = goal.Cycle switch { GoalCycle.Daily => anchor, GoalCycle.Weekly => Calendar.CalendarRules.Monday(anchor), GoalCycle.Monthly => new DateOnly(anchor.Year, anchor.Month, 1), _ => goal.Start };
        var to = goal.Cycle switch { GoalCycle.Daily => anchor, GoalCycle.Weekly => from.AddDays(6), GoalCycle.Monthly => new DateOnly(anchor.Year, anchor.Month, DateTime.DaysInMonth(anchor.Year, anchor.Month)), _ => goal.Until ?? anchor };
        if (from < goal.Start) from = goal.Start;
        if (goal.Until is { } until && to > until) to = until;
        return (from, to);
    }
    public static GoalProgress Progress(FocusGoal goal, IEnumerable<FocusRecord> records, IEnumerable<HabitCheckIn> checkIns, DateOnly date)
    {
        var (from, to) = Period(goal, date);
        var active = date >= goal.Start && (goal.Until is null || date <= goal.Until);
        var cutoff = to > date ? date : to;
        double value = 0;
        if (cutoff >= from)
        {
            if (goal.Metric == GoalMetric.CheckInDays) value = checkIns.Where(item => item.HabitId == goal.HabitId && item.Date >= from && item.Date <= cutoff).Select(item => item.Date).Distinct().Count();
            else
            {
                var data = FocusStatistics.Build(records, new(from, cutoff, goal.ProjectId is { } id ? new HashSet<Guid> { id } : null));
                value = goal.Metric == GoalMetric.Minutes ? data.Duration.TotalMinutes : data.Count;
            }
        }
        return new(goal, from, to, value, active, date);
    }
    public static HabitProgress HabitStatus(Habit habit, IEnumerable<HabitCheckIn> checkIns, DateOnly today)
    {
        var dates = checkIns.Where(item => item.HabitId == habit.Id).Select(item => item.Date).ToHashSet();
        var month = new DateOnly(today.Year, today.Month, 1); var completed = 0; var planned = 0;
        for (var date = month; date <= today; date = date.AddDays(1)) if (Due(habit, date)) { planned++; if (dates.Contains(date)) completed++; }
        var streak = 0; var start = habit.Until is { } until && until < today ? until : today;
        for (var date = start; date >= habit.Start; date = date.AddDays(-1))
        {
            if (!Due(habit, date)) continue;
            if (date == today && !dates.Contains(date)) continue;
            if (!dates.Contains(date)) break;
            streak++;
        }
        return new(completed, planned, streak);
    }
}
