using System.Text;

namespace FocusBloom.Core.Calendar;

public static class IcsExporter
{
    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n").Replace(",", "\\,").Replace(";", "\\;");
    private static string Time(DateOnly day, TimeOnly time) => day.ToDateTime(time).ToString("yyyyMMdd'T'HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    private static List<string> Header() => new() { "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//FocusBloom//Calendar//ZH", "CALSCALE:GREGORIAN" };
    private static void Event(List<string> lines, string uid, string title, DateOnly date, TimeOnly? start, double? endMinutes, string detail)
    {
        lines.Add("BEGIN:VEVENT"); lines.Add("UID:" + uid + "@FocusBloom"); lines.Add("DTSTAMP:" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'"));
        lines.Add("SUMMARY:" + Escape(title));
        if (detail.Length > 0) lines.Add("DESCRIPTION:" + Escape(detail));
        if (start is null) { lines.Add("DTSTART;VALUE=DATE:" + date.ToString("yyyyMMdd")); lines.Add("DTEND;VALUE=DATE:" + date.AddDays(1).ToString("yyyyMMdd")); }
        else
        {
            lines.Add("DTSTART:" + Time(date, start.Value));
            var end = date.ToDateTime(TimeOnly.MinValue).AddMinutes(endMinutes ?? start.Value.ToTimeSpan().TotalMinutes + 1);
            lines.Add("DTEND:" + end.ToString("yyyyMMdd'T'HHmmss"));
        }
    }
    public static string Entries(IEnumerable<CalendarEntry> entries, Func<CalendarEntry, ReminderOptions?>? reminderOptions = null)
    {
        var lines = Header();
        foreach (var entry in entries)
        {
            var uid = entry.Kind == "课程" ? $"{entry.Id:N}-{entry.OriginalDate ?? entry.Date:yyyyMMdd}-{entry.MeetingId ?? Guid.Empty:N}"
                : $"{entry.Id:N}-{entry.Date:yyyyMMdd}-{entry.Start?.ToString("HHmm") ?? "day"}";
            Event(lines, uid, entry.Title, entry.Date, entry.Start, entry.EndMinutes ?? entry.End?.ToTimeSpan().TotalMinutes, entry.Detail);
            if (reminderOptions?.Invoke(entry) is { Enabled: true } reminder) Alarm(lines, entry.Title, entry.Start, reminder);
            lines.Add("END:VEVENT");
        }
        return Finish(lines);
    }
    public static string DateEvent(DateEvent item)
    {
        var lines = Header(); Event(lines, item.Id.ToString("N"), item.Title, item.Date, item.Time, null, item.Notes);
        if (item.Repeat != DateRepeat.None)
        {
            var rule = item.Repeat switch { DateRepeat.Daily => "FREQ=DAILY", DateRepeat.Monthly => $"FREQ=MONTHLY;BYMONTHDAY={item.Date.Day},-1;BYSETPOS=1", _ => $"FREQ=YEARLY;BYMONTH={item.Date.Month};BYMONTHDAY={item.Date.Day},-1;BYSETPOS=1" };
            if (item.RepeatUntil is { } until) rule += ";UNTIL=" + (item.Time is null ? until.ToString("yyyyMMdd") : Time(until, item.Time.Value));
            lines.Add("RRULE:" + rule);
        }
        if ((item.Reminder ?? new()).Enabled) Alarm(lines, item.Title, item.Time, item.Reminder ?? new());
        lines.Add("END:VEVENT"); return Finish(lines);
    }
    private static void Alarm(List<string> lines, string title, TimeOnly? time, ReminderOptions options)
    {
        lines.Add("BEGIN:VALARM"); lines.Add("ACTION:DISPLAY"); lines.Add("DESCRIPTION:" + Escape(title));
        lines.Add(time is not null ? $"TRIGGER:-PT{options.MinutesBefore}M" : $"TRIGGER:PT{(options.DayTime ?? new TimeOnly(8, 0)).Hour}H{(options.DayTime ?? new TimeOnly(8, 0)).Minute}M"); lines.Add("END:VALARM");
    }
    private static string Finish(List<string> lines)
    {
        lines.Add("END:VCALENDAR"); var result = new StringBuilder();
        foreach (var line in lines)
        {
            var bytes = 0;
            foreach (var rune in line.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 75) { result.Append("\r\n "); bytes = 1; }
                result.Append(rune); bytes += rune.Utf8SequenceLength;
            }
            result.Append("\r\n");
        }
        return result.ToString();
    }
}
