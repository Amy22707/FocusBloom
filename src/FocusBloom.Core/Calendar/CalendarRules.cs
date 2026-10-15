namespace FocusBloom.Core.Calendar;

public static class CalendarRules
{
    public static DateOnly Monday(DateOnly date) => date.AddDays(-((int)date.DayOfWeek + 6) % 7);
    public static int TeachingWeek(SchoolTerm term, DateOnly date) => (int)Math.Floor((date.DayNumber - Monday(term.Start).DayNumber) / 7d) + 1;
    public static IReadOnlyList<CourseMeeting> Meetings(SchoolCourse course) => course.Meetings is { Count: > 0 } meetings ? meetings
        : new[] { new CourseMeeting(Guid.Empty, course.Weekday, course.StartPeriod, course.EndPeriod, course.FirstWeek, course.LastWeek, course.Pattern, course.Weeks, course.Room) };
    public static bool Applies(SchoolCourse course, int week) => Meetings(course).Any(meeting => Applies(meeting, week));
    public static bool Applies(CourseMeeting meeting, int week) => week >= meeting.FirstWeek && week <= meeting.LastWeek
        && (meeting.Pattern == WeekPattern.All || week % 2 == (meeting.Pattern == WeekPattern.Odd ? 1 : 0))
        && (string.IsNullOrWhiteSpace(meeting.Weeks) || ParseWeeks(meeting.Weeks).Contains(week));
    public static IReadOnlyList<int> ParseWeeks(string text)
    {
        var result = new SortedSet<int>();
        foreach (var section in text.Replace('，', ',').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = section.Split('-', StringSplitOptions.TrimEntries);
            if (range.Length is < 1 or > 2 || !int.TryParse(range[0], out var first) || first is < 1 or > 60
                || (range.Length == 2 && (!int.TryParse(range[1], out var last) || last < first || last > 60)))
                throw new FormatException("周次格式为1-16或1,3,5-8，范围为1至60。");
            var end = range.Length == 2 ? int.Parse(range[1]) : first;
            for (var week = first; week <= end; week++) result.Add(week);
        }
        return result.ToArray();
    }
    public static IEnumerable<DateOnly> Occurrences(DateEvent item, DateOnly from, DateOnly to)
    {
        if (item.RepeatUntil is { } until && until < to) to = until;
        if (to < item.Date || to < from) yield break;
        if (item.Repeat == DateRepeat.None) { if (item.Date >= from && item.Date <= to) yield return item.Date; yield break; }
        if (item.Repeat == DateRepeat.Daily)
        { for (var day = from > item.Date ? from : item.Date; day <= to; day = day.AddDays(1)) yield return day; yield break; }
        var offset = item.Repeat == DateRepeat.Monthly ? Math.Max(0, (from.Year - item.Date.Year) * 12 + from.Month - item.Date.Month) : Math.Max(0, from.Year - item.Date.Year);
        while (true)
        {
            var anchor = item.Repeat == DateRepeat.Monthly ? new DateOnly(item.Date.Year, item.Date.Month, 1).AddMonths(offset)
                : new DateOnly(item.Date.Year + offset, item.Date.Month, 1);
            var day = new DateOnly(anchor.Year, anchor.Month, Math.Min(item.Date.Day, DateTime.DaysInMonth(anchor.Year, anchor.Month)));
            if (day > to) yield break;
            if (day >= item.Date && day >= from) yield return day;
            offset++;
        }
    }
    public static DateOnly? NextDate(DateEvent item, DateOnly today) => item.Repeat == DateRepeat.None || today <= item.Date
        ? item.Date : Occurrences(item, today, today.AddYears(2)).Select(day => (DateOnly?)day).FirstOrDefault();
    public static DateTimeOffset ReminderAt(DateOnly date, TimeOnly? time, ReminderOptions options)
    {
        var local = date.ToDateTime(time ?? options.DayTime ?? new TimeOnly(8, 0));
        if (time is not null) local = local.AddMinutes(-options.MinutesBefore);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
    public static IReadOnlyList<CalendarEntry> CourseEntries(SchoolTerm term, IEnumerable<SchoolCourse> courses,
        IEnumerable<CourseChange> changes, DateOnly from, DateOnly to)
    {
        var result = new List<CalendarEntry>(); var overrides = changes.ToArray();
        foreach (var course in courses)
        {
            foreach (var meeting in Meetings(course))
            {
            for (var week = 1; week <= term.Weeks; week++)
            {
                if (!Applies(meeting, week)) continue;
                var original = Monday(term.Start).AddDays((week - 1) * 7 + meeting.Weekday - 1);
                var change = overrides.FirstOrDefault(item => item.CourseId == course.Id && item.OriginalDate == original && (item.MeetingId ?? Guid.Empty) == meeting.Id);
                if (change?.Cancelled == true) continue;
                var day = change?.NewDate ?? original;
                if (day < from || day > to) continue;
                var start = term.Periods.FirstOrDefault(period => period.Number == (change?.StartPeriod ?? meeting.StartPeriod));
                var end = term.Periods.FirstOrDefault(period => period.Number == (change?.EndPeriod ?? meeting.EndPeriod));
                if (start is null || end is null) continue;
                result.Add(new("课程", course.Id, course.Name, day, start.Start, end.End,
                    $"第{week}周 · {change?.Room ?? meeting.Room}" + (course.Teacher.Length > 0 ? $" · {course.Teacher}" : "")
                    + (change is not null ? " · 临时调课" : ""), course.Color, ProjectId: course.ProjectId, OriginalDate: original, MeetingId: meeting.Id == Guid.Empty ? null : meeting.Id));
            }
            }
        }
        return result;
    }
}
