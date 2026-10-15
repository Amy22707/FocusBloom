using FocusBloom.Core.Todos;

namespace FocusBloom.Core.Calendar;

public static class ReminderRules
{
    public static IReadOnlyList<ReminderOccurrence> Build(IEnumerable<TodoItem> todos, IEnumerable<DateEvent> dates,
        IEnumerable<CalendarPlan> plans, SchoolTerm term, IEnumerable<SchoolCourse> courses, IEnumerable<CourseChange> changes,
        Func<string, Guid, ReminderOptions> todoOptions, DateOnly today, DateOnly? missedFrom = null)
    {
        var reminders = new List<ReminderOccurrence>();
        var lower = missedFrom ?? today.AddDays(-7);
        void Add(string kind, Guid id, string title, DateOnly day, TimeOnly? time, ReminderOptions options, string detail, DateOnly? original = null, Guid? meetingId = null)
        {
            if (!options.Enabled) return;
            var happens = day.ToDateTime(time ?? TimeOnly.MaxValue);
            var at = CalendarRules.ReminderAt(day, time, options);
            var key = $"{kind}:{id}:{day:yyyyMMdd}:{time?.ToString("HHmm") ?? "day"}:{at:yyyyMMddHHmm}:{original?.ToString("yyyyMMdd") ?? ""}" + (meetingId is { } meeting ? $":{meeting}" : "");
            reminders.Add(new(key, kind, id, title, at, new DateTimeOffset(happens, TimeZoneInfo.Local.GetUtcOffset(happens)), detail, original, time is null, meetingId));
        }
        foreach (var todo in todos.Where(item => item.Status != TodoStatus.Completed && item.DueDate is not null))
            Add("待办", todo.Id, todo.Title, todo.DueDate!.Value, todo.DueTime, todoOptions("待办", todo.Id), "截止日期");
        foreach (var item in dates)
        {
            var from = item.Date;
            var days = item.Repeat == DateRepeat.None
                ? item.Mode == DateCountMode.CountUp && item.Date < today ? Array.Empty<DateOnly>() : new[] { item.Date }
                : CalendarRules.Occurrences(item, from > lower ? from : lower, today.AddDays(90));
            if (item.Repeat != DateRepeat.None && CalendarRules.NextDate(item, today) is { } next && next > today.AddDays(90)) days = days.Append(next);
            foreach (var day in days.Where(day => !(item.Mode == DateCountMode.CountUp && day == item.Date && day < today))) Add("日期事项", item.Id, item.Title, day, item.Time, item.Reminder ?? new(), item.Notes);
        }
        foreach (var plan in plans) Add("安排", plan.Id, plan.Title, plan.Date, plan.Start, plan.Reminder ?? new(false), "计划安排");
        var courseList = courses.ToArray();
        var changeList = changes.ToArray();
        var courseEnd = changeList.Where(change => !change.Cancelled && change.NewDate is not null).Select(change => change.NewDate!.Value)
            .Append(CalendarRules.Monday(term.Start).AddDays(term.Weeks * 7 - 1)).Max();
        foreach (var entry in CalendarRules.CourseEntries(term, courseList, changeList, lower, courseEnd))
            Add("课程", entry.Id, entry.Title, entry.Date, entry.Start, courseList.Single(course => course.Id == entry.Id).Reminder ?? new(true, 10), entry.Detail, entry.OriginalDate, entry.MeetingId);
        return reminders.OrderBy(item => item.At).ToArray();
    }
}
