namespace FocusBloom.Core.Statistics;

public sealed record InterceptionStatistics(int StrictSessions, int CountedSessions, long Total, int AffectedSessions)
{
    public int UnknownSessions => StrictSessions - CountedSessions;
    public double Average => CountedSessions == 0 ? 0 : (double)Total / CountedSessions;
    public int ZeroSessions => CountedSessions - AffectedSessions;
    public double? ZeroRatio => CountedSessions == 0 ? null : (double)ZeroSessions / CountedSessions;
    public TimeSpan CountedDuration { get; init; }
    public double? PerHour => CountedDuration > TimeSpan.Zero ? Total / CountedDuration.TotalHours : null;
    public static InterceptionStatistics Build(StatisticsSnapshot data)
    {
        var strict = data.Sessions.Where(item => item.EndedInRange && item.Record.Strict).Select(item => item.Record).ToArray();
        var counted = strict.Where(item => item.InterceptionCount is not null).ToArray();
        return new(strict.Length, counted.Length, counted.Sum(item => (long)item.InterceptionCount!.Value), counted.Count(item => item.InterceptionCount > 0))
        { CountedDuration = TimeSpan.FromTicks(counted.Sum(item => item.FocusDuration.Ticks)) };
    }
}
