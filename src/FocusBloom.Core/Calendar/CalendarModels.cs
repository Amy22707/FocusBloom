namespace FocusBloom.Core.Calendar;

public enum DateCountMode { Countdown, CountUp }
public enum DateRepeat { None, Daily, Monthly, Yearly }
public enum WeekPattern { All, Odd, Even }
public sealed record ReminderOptions(bool Enabled = true, int MinutesBefore = 30, TimeOnly? DayTime = null);
public sealed record DateEvent(Guid Id, string Title, DateOnly Date, TimeOnly? Time = null,
    DateCountMode Mode = DateCountMode.Countdown, DateRepeat Repeat = DateRepeat.None, DateOnly? RepeatUntil = null,
    string Notes = "", string? CoverPath = null, bool Pinned = false, Guid? ProjectId = null, Guid? TodoId = null,
    ReminderOptions? Reminder = null);
public sealed record CalendarPlan(Guid Id, string Title, DateOnly Date, TimeOnly Start, int Minutes = 30,
    Guid? TodoId = null, Guid? ProjectId = null, string Notes = "", ReminderOptions? Reminder = null);
public sealed record ClassPeriod(int Number, TimeOnly Start, TimeOnly End);
public sealed record SchoolTerm(DateOnly Start, int Weeks, IReadOnlyList<ClassPeriod> Periods)
{
    public static SchoolTerm Default(DateOnly today) => new(CalendarRules.Monday(today), 20,
        Enumerable.Range(1, 12).Select(number => new ClassPeriod(number,
            new TimeOnly(8, 0).AddMinutes((number - 1) * 60), new TimeOnly(8, 50).AddMinutes((number - 1) * 60))).ToArray());
}
public sealed record CourseMeeting(Guid Id, int Weekday, int StartPeriod, int EndPeriod,
    int FirstWeek = 1, int LastWeek = 20, WeekPattern Pattern = WeekPattern.All, string Weeks = "", string Room = "");
public sealed record SchoolCourse(Guid Id, string Name, int Weekday, int StartPeriod, int EndPeriod,
    int FirstWeek = 1, int LastWeek = 20, WeekPattern Pattern = WeekPattern.All, string Weeks = "",
    string Room = "", string Teacher = "", string Color = "#7B97A6", Guid? ProjectId = null, ReminderOptions? Reminder = null,
    IReadOnlyList<CourseMeeting>? Meetings = null);
public sealed record CourseChange(Guid Id, Guid CourseId, DateOnly OriginalDate, bool Cancelled,
    DateOnly? NewDate = null, int? StartPeriod = null, int? EndPeriod = null, string? Room = null, Guid? MeetingId = null);
public sealed record CalendarEntry(string Kind, Guid Id, string Title, DateOnly Date, TimeOnly? Start = null,
    TimeOnly? End = null, string Detail = "", string Color = "", Guid? TodoId = null, Guid? ProjectId = null,
    bool Completed = false, DateOnly? OriginalDate = null, double? EndMinutes = null, Guid? MeetingId = null);
public sealed record ReminderOccurrence(string Key, string Kind, Guid Id, string Title, DateTimeOffset At,
    DateTimeOffset HappensAt, string Detail, DateOnly? OriginalDate = null, bool AllDay = false, Guid? MeetingId = null);
