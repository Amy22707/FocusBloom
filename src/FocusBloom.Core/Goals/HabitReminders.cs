using FocusBloom.Core.Calendar;

namespace FocusBloom.Core.Goals;

public static class HabitReminders
{
    public static IReadOnlyList<ReminderOccurrence> Build(IEnumerable<Habit> habits, IEnumerable<HabitCheckIn> checkIns, DateOnly today)
    {
        var result = new List<ReminderOccurrence>(); var checkedDays = checkIns.Select(item => (item.HabitId, item.Date)).ToHashSet();
        foreach (var habit in habits.Where(item => !item.Deleted && item.Reminder))
        {
            var first = habit.Start > today.AddDays(-7) ? habit.Start : today.AddDays(-7);
            var last = habit.Start > today.AddDays(30) ? habit.Start.AddDays(6) : today.AddDays(30);
            if (habit.Until is { } until && until < last) last = until;
            for (var day = first; day <= last; day = day.AddDays(1))
            {
                if (!GoalRules.Due(habit, day) || checkedDays.Contains((habit.Id, day))) continue;
                var time = habit.ReminderTime ?? new TimeOnly(20, 0); var at = CalendarRules.ReminderAt(day, time, new(true, 0));
                result.Add(new($"习惯:{habit.Id}:{day:yyyyMMdd}:{time:HHmm}", "习惯", habit.Id, habit.Name, at, at, "今天的习惯还未打卡"));
            }
        }
        return result.OrderBy(item => item.At).ToArray();
    }
}
