using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Todos;

namespace FocusBloom.Core.Reflection;

public sealed record MoodChoice(int Value, string Name, int Level, string Color);
public sealed record DailyReflection(DateOnly Date, int? Mood, string[] Tags, string Feeling = "", string Highlights = "", string NextStep = "", string[]? Photos = null);
public sealed record ReflectionReminder(bool Enabled = false, TimeOnly? Time = null);
public sealed record ReflectionDaySummary(TimeSpan Duration, int Sessions, IReadOnlyList<TodoItem> CompletedTodos,
    int PlannedHabits, int CheckedHabits, IReadOnlyList<FocusOutcome> Outcomes);

public static class ReflectionRules
{
    public static IReadOnlyList<MoodChoice> Moods { get; } = new[]
    {
        new MoodChoice(1, "低落", 1, "#9EB8D6"), new MoodChoice(2, "疲惫", 2, "#BAABA4"),
        new MoodChoice(3, "平静", 3, "#ADC7AB"), new MoodChoice(4, "愉快", 4, "#F0BE96"),
        new MoodChoice(5, "兴奋", 5, "#EBCB77"), new MoodChoice(6, "烦躁", 1, "#ADA1CD")
    };
    public static MoodChoice Mood(int value) => Moods.Single(item => item.Value == value);
    public static DailyReflection Normalize(DailyReflection item, DateOnly today)
    {
        if (item.Date > today) throw new InvalidOperationException("不能记录未来日期的心情，请选择今天或过去的日期。");
        if (item.Mood is { } value && !Moods.Any(choice => choice.Value == value)) throw new InvalidOperationException("请选择有效的心情。");
        var normalized = item with { Tags = item.Tags.Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), Feeling = item.Feeling.Trim(), Highlights = item.Highlights.Trim(), NextStep = item.NextStep.Trim(), Photos = item.Photos?.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
        if (normalized.Mood is null && normalized.Tags.Length == 0 && normalized.Feeling.Length == 0 && normalized.Highlights.Length == 0 && normalized.NextStep.Length == 0 && normalized.Photos is not { Length: > 0 }) throw new InvalidOperationException("选一个心情、添加图片，或写一点今天的感受与复盘。");
        return normalized;
    }
    public static ReflectionDaySummary Summary(DateOnly day, IReadOnlyList<FocusRecord> records, IReadOnlyList<TodoItem> todos,
        IReadOnlyList<Habit> habits, IReadOnlyList<HabitCheckIn> checks, IReadOnlyList<FocusOutcome> outcomes)
    {
        var statistics = FocusStatistics.Build(records, new(day, day)); var done = checks.Where(item => item.Date == day).Select(item => item.HabitId).ToHashSet();
        var ended = statistics.Sessions.Where(item => item.EndedInRange).Select(item => item.Record.Id).ToHashSet();
        var planned = habits.Count(habit => GoalRules.Due(habit, day) || done.Contains(habit.Id));
        return new(statistics.Duration, statistics.Count, todos.Where(todo => todo.Status == TodoStatus.Completed && todo.CompletedAt is { } at && DateOnly.FromDateTime(at.LocalDateTime) == day).ToArray(),
            planned, done.Count, outcomes.Where(item => ended.Contains(item.SessionId) && (item.Summary.Length > 0 || item.Quantity is not null || item.NextStep.Length > 0)).ToArray());
    }
}

public static class ReflectionReminders
{
    public static readonly Guid Id = Guid.Parse("a6eb69f0-d6cd-4647-a209-064d2bab7a32");
    public static IReadOnlyList<ReminderOccurrence> Build(ReflectionReminder options, IEnumerable<DailyReflection> entries, DateOnly today)
    {
        if (!options.Enabled) return Array.Empty<ReminderOccurrence>();
        var saved = entries.Select(item => item.Date).ToHashSet(); var time = options.Time ?? new TimeOnly(21, 0);
        return Enumerable.Range(0, 31).Select(today.AddDays).Where(day => !saved.Contains(day)).Select(day =>
        {
            var at = CalendarRules.ReminderAt(day, time, new(true, 0));
            return new ReminderOccurrence($"心情:{day:yyyyMMdd}:{time:HHmm}", "心情", Id, "记录今天的心情与复盘", at, at, "留一点时间回顾今天");
        }).ToArray();
    }
}
