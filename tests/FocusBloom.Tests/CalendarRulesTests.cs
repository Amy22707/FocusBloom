using FocusBloom.Core.Calendar;
using FocusBloom.Core.Todos;
using Xunit;

namespace FocusBloom.Tests;

public sealed class CalendarRulesTests
{
    [Fact]
    public void MonthlyAndYearlyDatesKeepTheAnchorAndRespectTheEndDate()
    {
        var monthly = new DateEvent(Guid.NewGuid(), "每月31日", new(2026, 1, 31), Repeat: DateRepeat.Monthly);
        Assert.Equal(new[] { new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31) }, CalendarRules.Occurrences(monthly, new(2026, 1, 1), new(2026, 3, 31)));
        var annual = monthly with { Date = new(2024, 2, 29), Repeat = DateRepeat.Yearly };
        Assert.Equal(new DateOnly(2025, 2, 28), CalendarRules.NextDate(annual, new(2025, 1, 1)));
        Assert.Equal(new DateOnly(2028, 2, 29), CalendarRules.NextDate(annual, new(2028, 1, 1)));
        Assert.Null(CalendarRules.NextDate(monthly with { RepeatUntil = new(2026, 2, 28) }, new(2026, 3, 1)));
        Assert.Equal(new DateOnly(2030, 1, 31), CalendarRules.NextDate(monthly with { Date = new(2030, 1, 31) }, new(2026, 10, 14)));
    }
    [Fact]
    public void CoursesUseTeachingWeeksOddEvenAndSingleOccurrenceChanges()
    {
        var term = new SchoolTerm(new(2026, 9, 7), 6, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)) });
        var course = new SchoolCourse(Guid.NewGuid(), "数学", 1, 1, 2, 1, 6, WeekPattern.Odd, "1-6", "101");
        var changes = new[] { new CourseChange(Guid.NewGuid(), course.Id, new(2026, 9, 7), false, new(2026, 9, 9), 2, 2, "202"), new CourseChange(Guid.NewGuid(), course.Id, new(2026, 9, 21), true) };
        var entries = CalendarRules.CourseEntries(term, new[] { course }, changes, new(2026, 9, 1), new(2026, 10, 30));
        Assert.Equal(new[] { new DateOnly(2026, 9, 9), new DateOnly(2026, 10, 5) }, entries.Select(entry => entry.Date));
        Assert.Equal(new TimeOnly(9, 0), entries[0].Start); Assert.Contains("202", entries[0].Detail);
        Assert.Equal(new DateOnly(2026, 9, 7), entries[0].OriginalDate);
        Assert.Equal(0, CalendarRules.TeachingWeek(term, new(2026, 9, 6))); Assert.Equal(1, CalendarRules.TeachingWeek(term, new(2026, 9, 13)));
        Assert.Equal(new[] { 1, 3, 5, 6, 7 }, CalendarRules.ParseWeeks("1,3,5-7"));
        Assert.Throws<FormatException>(() => CalendarRules.ParseWeeks("4-2"));
    }
    [Fact]
    public void ReminderDefaultsAndCourseAdjustmentsHaveDistinctOccurrences()
    {
        var day = new DateOnly(2026, 10, 14); var folder = Guid.NewGuid();
        var noTime = new TodoItem(Guid.NewGuid(), folder, "日期截止", DueDate: day);
        var timed = noTime with { Id = Guid.NewGuid(), Title = "时间截止", DueTime = new(0, 10) };
        var term = new SchoolTerm(new(2026, 10, 12), 2, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)) });
        var course = new SchoolCourse(Guid.NewGuid(), "课程", 1, 1, 1, 1, 2);
        var changes = new[] { new CourseChange(Guid.NewGuid(), course.Id, new(2026, 10, 12), false, new(2026, 10, 19)) };
        var result = ReminderRules.Build(new[] { noTime, timed, noTime with { Id = Guid.NewGuid(), Status = TodoStatus.Completed } }, Array.Empty<DateEvent>(), Array.Empty<CalendarPlan>(), term, new[] { course }, changes, (_, _) => new(), day);
        Assert.Equal(new DateTime(2026, 10, 14, 8, 0, 0), result.Single(item => item.Id == noTime.Id).At.LocalDateTime);
        Assert.Equal(new DateTime(2026, 10, 13, 23, 40, 0), result.Single(item => item.Id == timed.Id).At.LocalDateTime);
        var lessons = result.Where(item => item.Kind == "课程").ToArray(); Assert.Equal(2, lessons.Length); Assert.NotEqual(lessons[0].Key, lessons[1].Key);
        Assert.Equal(new DateTime(2026, 10, 19, 7, 50, 0), lessons[0].At.LocalDateTime);
    }
    [Fact]
    public void CountUpSkipsHistoricalAnchorButKeepsRecentRepeatedReminders()
    {
        var item = new DateEvent(Guid.NewGuid(), "纪念日", new(2020, 10, 13), Mode: DateCountMode.CountUp, Repeat: DateRepeat.Yearly);
        var term = SchoolTerm.Default(new(2026, 10, 14));
        var result = ReminderRules.Build(Array.Empty<TodoItem>(), new[] { item, item with { Id = Guid.NewGuid(), Repeat = DateRepeat.None } }, Array.Empty<CalendarPlan>(), term, Array.Empty<SchoolCourse>(), Array.Empty<CourseChange>(), (_, _) => new(), new(2026, 10, 14));
        Assert.Equal(2, result.Count); Assert.Equal(new DateTime(2026, 10, 13, 8, 0, 0), result[0].At.LocalDateTime); Assert.Equal(new DateTime(2027, 10, 13, 8, 0, 0), result[1].At.LocalDateTime);
    }
    [Fact]
    public void OverlappingTimeBlocksHaveSeparateColumnsAndResetAfterTheCluster()
    {
        var day = new DateOnly(2026, 10, 14);
        var entries = new[] { new CalendarEntry("安排", Guid.NewGuid(), "A", day, new(8, 0), new(9, 0)), new CalendarEntry("专注", Guid.NewGuid(), "B", day, new(8, 30), new(9, 30)), new CalendarEntry("待办", Guid.NewGuid(), "C", day, new(10, 0)) };
        var layout = TimelineLayout.Place(entries); Assert.Equal(2, layout[0].Columns); Assert.Equal(2, layout[1].Columns); Assert.NotEqual(layout[0].Column, layout[1].Column); Assert.Equal(1, layout[2].Columns);
    }
    [Fact]
    public void CalendarExportHasAllDayDatesAnchoredRecurrenceAndEscapedFoldedText()
    {
        var item = new DateEvent(Guid.NewGuid(), "考试,备考;计划", new(2026, 1, 31), Repeat: DateRepeat.Monthly, Notes: new string('学', 80));
        var ics = IcsExporter.DateEvent(item); Assert.Contains("DTSTART;VALUE=DATE:20260131", ics); Assert.Contains("DTEND;VALUE=DATE:20260201", ics);
        Assert.Contains("RRULE:FREQ=MONTHLY;BYMONTHDAY=31,-1;BYSETPOS=1", ics); Assert.Contains("考试\\,备考\\;计划", ics); Assert.Contains("TRIGGER:PT8H0M", ics);
        Assert.All(ics.Split("\r\n"), line => Assert.True(System.Text.Encoding.UTF8.GetByteCount(line) <= 75));
    }
}
