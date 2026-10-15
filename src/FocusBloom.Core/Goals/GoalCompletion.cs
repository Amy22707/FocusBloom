using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;

namespace FocusBloom.Core.Goals;

public sealed record CompletionEntry(Guid Id, string Name, bool IsHabit, GoalMetric Metric, double Value, double Target)
{
    public double Ratio => Math.Min(1, Value / Target);
    public bool Complete => Value >= Target;
}
public sealed record CompletionDay(DateOnly Date, IReadOnlyList<CompletionEntry> Entries, bool Future)
{
    public double Ratio => Entries.Count == 0 ? 0 : Entries.Average(item => item.Ratio);
    public int Completed => Entries.Count(item => item.Complete);
}

public static class GoalCompletion
{
    public static IReadOnlyList<CompletionDay> Build(IReadOnlyList<FocusGoal> goals, IReadOnlyList<Habit> habits,
        IReadOnlyList<FocusRecord> records, IReadOnlyList<HabitCheckIn> checkIns, DateOnly from, DateOnly to, DateOnly today)
    {
        var entries = Enumerable.Range(from.DayNumber, to.DayNumber - from.DayNumber + 1).ToDictionary(number => DateOnly.FromDayNumber(number), _ => new List<CompletionEntry>());
        var checkedDays = checkIns.Select(item => (item.HabitId, item.Date)).ToHashSet();
        foreach (var goal in goals)
        {
            var lower = GoalRules.Period(goal, from).From; var upper = to < today ? to : today;
            if (goal.Until is { } until && upper > until) upper = until;
            var amounts = goal.Metric == GoalMetric.CheckInDays
                ? checkIns.Where(item => item.HabitId == goal.HabitId && item.Date >= lower && item.Date <= upper).Select(item => item.Date).Distinct().ToDictionary(day => day, _ => 1d)
                : FocusStatistics.Build(records, new(lower, upper, goal.ProjectId is { } project ? new HashSet<Guid> { project } : null)).Days
                    .ToDictionary(day => day.Date, day => goal.Metric == GoalMetric.Minutes ? day.Duration.TotalMinutes : day.Count);
            foreach (var (day, list) in entries.Where(pair => pair.Key >= goal.Start && (goal.Until is null || pair.Key <= goal.Until)))
            {
                var period = GoalRules.Period(goal, day);
                var amount = day > today ? 0 : amounts.Where(pair => pair.Key >= period.From && pair.Key <= day).Sum(pair => pair.Value);
                list.Add(new(goal.Id, goal.Name, false, goal.Metric, amount, goal.Target));
            }
        }
        foreach (var habit in habits.Where(item => !item.Deleted))
            foreach (var (day, list) in entries.Where(pair => GoalRules.Due(habit, pair.Key) || checkedDays.Contains((habit.Id, pair.Key))))
                list.Add(new(habit.Id, habit.Name, true, GoalMetric.CheckInDays, day <= today && checkedDays.Contains((habit.Id, day)) ? 1 : 0, 1));
        return entries.Select(pair => new CompletionDay(pair.Key, pair.Value, pair.Key > today)).ToArray();
    }
}
