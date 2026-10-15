namespace FocusBloom.Core.Calendar;

public sealed record TimelinePlacement(CalendarEntry Entry, int Column, int Columns);
public static class TimelineLayout
{
    public static IReadOnlyList<TimelinePlacement> Place(IEnumerable<CalendarEntry> source, double minimumMinutes = 30)
    {
        var result = new List<TimelinePlacement>(); var cluster = new List<(CalendarEntry Entry, int Column)>();
        var ends = new List<double>(); var clusterEnd = 0d;
        void Flush() { foreach (var (entry, column) in cluster) result.Add(new(entry, column, ends.Count)); cluster.Clear(); ends.Clear(); }
        foreach (var entry in source.OrderBy(entry => entry.Start))
        {
            var start = entry.Start!.Value.ToTimeSpan().TotalMinutes;
            var end = Math.Max(start + minimumMinutes, entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes ?? start);
            if (cluster.Count > 0 && start >= clusterEnd) Flush();
            var column = ends.FindIndex(value => value <= start);
            if (column < 0) { column = ends.Count; ends.Add(end); } else ends[column] = end;
            cluster.Add((entry, column)); clusterEnd = Math.Max(cluster.Count == 1 ? end : clusterEnd, end);
        }
        if (cluster.Count > 0) Flush(); return result;
    }
}
