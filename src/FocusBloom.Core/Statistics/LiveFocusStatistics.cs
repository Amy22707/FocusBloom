using FocusBloom.Core.Focus;

namespace FocusBloom.Core.Statistics;

public sealed record LiveFocusStatistics(TimeSpan Duration, IReadOnlyList<DayStatistics> Days, bool IncludesCurrent)
{
    public static LiveFocusStatistics Build(StatisticsSnapshot saved, FocusRecord? current)
    {
        if (current is null || saved.Sessions.Any(item => item.Record.Id == current.Id)) return new(saved.Duration, saved.Days, false);
        var live = FocusStatistics.Build(new[] { current }, saved.Filter);
        var days = saved.Days.Concat(live.Days).GroupBy(item => item.Date).OrderBy(group => group.Key)
            .Select(group => new DayStatistics(group.Key, TimeSpan.FromTicks(group.Sum(item => item.Duration.Ticks)), saved.Days.FirstOrDefault(item => item.Date == group.Key)?.Count ?? 0)).ToArray();
        return new(saved.Duration + live.Duration, days, live.Duration > TimeSpan.Zero);
    }
}
