using FocusBloom.Core.Focus;

namespace FocusBloom.Core.Statistics;

public sealed record StatisticsFilter(DateOnly From, DateOnly To, IReadOnlySet<Guid>? Projects = null, Guid? TodoId = null, int? Hour = null);
public sealed record FocusSlice(Guid RecordId, Guid ProjectId, Guid? TodoId, DateOnly Date, int Hour, TimeSpan Duration);
public sealed record SessionStatistics(FocusRecord Record, TimeSpan IncludedDuration, bool EndedInRange);
public sealed record DayStatistics(DateOnly Date, TimeSpan Duration, int Count);
public sealed record ProjectStatistics(Guid ProjectId, TimeSpan Duration, int Count);
public sealed record StatisticsBucket(string Label, DateOnly From, DateOnly To, TimeSpan Duration, int Count, int? Hour = null);
public sealed record StatisticsSnapshot(StatisticsFilter Filter, TimeSpan Duration, int Count, TimeSpan Average,
    IReadOnlyList<FocusSlice> Slices, IReadOnlyList<SessionStatistics> Sessions, IReadOnlyList<DayStatistics> Days,
    IReadOnlyList<ProjectStatistics> Projects)
{
    public int ActiveDays => Days.Count(day => day.Duration > TimeSpan.Zero);
}

public static class FocusStatistics
{
    public static StatisticsSnapshot Build(IEnumerable<FocusRecord> records, StatisticsFilter filter)
    {
        var slices = new List<FocusSlice>(); var sessions = new List<SessionStatistics>();
        if (filter.To < filter.From) return new(filter, TimeSpan.Zero, 0, TimeSpan.Zero, slices, sessions, Array.Empty<DayStatistics>(), Array.Empty<ProjectStatistics>());
        DateTimeOffset Local(DateTime time) => new(time, TimeZoneInfo.Local.GetUtcOffset(time));
        var from = Local(filter.From.ToDateTime(TimeOnly.MinValue)); var until = Local(filter.To.AddDays(1).ToDateTime(TimeOnly.MinValue));
        foreach (var record in records.Where(record => (filter.Projects is null || filter.Projects.Contains(record.ProjectId)) && (filter.TodoId is null || record.TodoId == filter.TodoId)))
        {
            long ticks = 0;
            foreach (var interval in record.Intervals.Where(interval => interval.Kind == IntervalKind.Focus))
            {
                var cursor = interval.StartedAt > from ? interval.StartedAt : from; var end = interval.EndedAt < until ? interval.EndedAt : until;
                while (cursor < end)
                {
                    var local = cursor.LocalDateTime;
                    var next = Local(local.Date.AddHours(local.Hour + 1)); if (next > end) next = end;
                    if (filter.Hour is null || filter.Hour == local.Hour)
                    {
                        var duration = next - cursor; ticks += duration.Ticks;
                        slices.Add(new(record.Id, record.ProjectId, record.TodoId, DateOnly.FromDateTime(local), local.Hour, duration));
                    }
                    cursor = next;
                }
            }
            var ended = record.EndedAt >= from && record.EndedAt < until && (filter.Hour is null || record.EndedAt.LocalDateTime.Hour == filter.Hour);
            if (ticks > 0 || ended) sessions.Add(new(record, TimeSpan.FromTicks(ticks), ended));
        }
        var counted = sessions.Where(session => session.EndedInRange).ToArray();
        var dailyTicks = slices.GroupBy(slice => slice.Date).ToDictionary(group => group.Key, group => group.Sum(slice => slice.Duration.Ticks));
        var dailyCounts = counted.GroupBy(session => DateOnly.FromDateTime(session.Record.EndedAt.LocalDateTime)).ToDictionary(group => group.Key, group => group.Count());
        var days = dailyTicks.Keys.Union(dailyCounts.Keys).Order().Select(day => new DayStatistics(day, TimeSpan.FromTicks(dailyTicks.GetValueOrDefault(day)), dailyCounts.GetValueOrDefault(day))).ToArray();
        var projectTicks = slices.GroupBy(slice => slice.ProjectId).ToDictionary(group => group.Key, group => group.Sum(slice => slice.Duration.Ticks));
        var projectCounts = counted.GroupBy(session => session.Record.ProjectId).ToDictionary(group => group.Key, group => group.Count());
        var projects = projectTicks.Keys.Union(projectCounts.Keys).Select(id => new ProjectStatistics(id, TimeSpan.FromTicks(projectTicks.GetValueOrDefault(id)), projectCounts.GetValueOrDefault(id))).OrderByDescending(project => project.Duration).ToArray();
        return new(filter, TimeSpan.FromTicks(slices.Sum(slice => slice.Duration.Ticks)), counted.Length,
            counted.Length == 0 ? TimeSpan.Zero : TimeSpan.FromTicks(counted.Sum(session => session.Record.FocusDuration.Ticks) / counted.Length),
            slices, sessions.OrderByDescending(session => session.Record.EndedAt).ToArray(), days, projects);
    }
    public static IReadOnlyList<StatisticsBucket> Hours(StatisticsSnapshot data) => Enumerable.Range(0, 24).Select(hour => new StatisticsBucket($"{hour:00}时", data.Filter.From, data.Filter.To,
        TimeSpan.FromTicks(data.Slices.Where(slice => slice.Hour == hour).Sum(slice => slice.Duration.Ticks)),
        data.Sessions.Count(session => session.EndedInRange && session.Record.EndedAt.LocalDateTime.Hour == hour), hour)).ToArray();
    public static IReadOnlyList<StatisticsBucket> Trend(StatisticsSnapshot data, bool hourly, bool monthly)
    {
        if (hourly) return Hours(data);
        var from = data.Filter.From; var to = data.Filter.To; if (to < from) return Array.Empty<StatisticsBucket>();
        var result = new List<StatisticsBucket>(); var span = to.DayNumber - from.DayNumber + 1;
        for (var cursor = from; cursor <= to;)
        {
            var end = monthly || span > 62 ? span > 730 ? new DateOnly(cursor.Year, 12, 31) : new DateOnly(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month)) : cursor;
            if (end > to) end = to;
            var days = data.Days.Where(day => day.Date >= cursor && day.Date <= end).ToArray();
            var label = span > 730 ? cursor.Year.ToString() : monthly || span > 62 ? (from.Year == to.Year ? $"{cursor.Month}月" : $"{cursor:yy-MM}") : $"{cursor:M/d}";
            result.Add(new(label, cursor, end, TimeSpan.FromTicks(days.Sum(day => day.Duration.Ticks)), days.Sum(day => day.Count))); cursor = end.AddDays(1);
        }
        return result;
    }
}
