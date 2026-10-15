using System.IO;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class CourseMeetingTests
{
    [Fact]
    public void MeetingsHaveIndependentWeekRulesChangesReminderKeysAndExportIds()
    {
        var day = new DateOnly(2026, 10, 12);
        var term = new SchoolTerm(day, 2, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)) });
        var first = new CourseMeeting(Guid.Empty, 1, 1, 1, LastWeek: 2, Room: "101");
        var second = first with { Id = Guid.NewGuid(), Room = "202", Pattern = WeekPattern.Odd };
        var third = first with { Id = Guid.NewGuid(), Weekday = 3, StartPeriod = 2, EndPeriod = 2, Weeks = "2" };
        var course = new SchoolCourse(Guid.NewGuid(), "数学", 1, 1, 1, Meetings: new[] { first, second, third });
        var changes = new[] { new CourseChange(Guid.NewGuid(), course.Id, day, true), new CourseChange(Guid.NewGuid(), course.Id, day, false, day.AddDays(1), Room: "303", MeetingId: second.Id) };
        var entries = CalendarRules.CourseEntries(term, new[] { course }, changes, day, day.AddDays(13));
        Assert.Equal(3, entries.Count); Assert.Contains(entries, entry => entry.Date == day.AddDays(1) && entry.MeetingId == second.Id && entry.Detail.Contains("303"));
        Assert.Contains(entries, entry => entry.Date == day.AddDays(7) && entry.MeetingId is null);
        Assert.Contains(entries, entry => entry.Date == day.AddDays(9) && entry.Start == new TimeOnly(9, 0));
        var simultaneous = course with { Meetings = new[] { first, second with { Pattern = WeekPattern.All } } };
        var reminders = ReminderRules.Build(Array.Empty<FocusBloom.Core.Todos.TodoItem>(), Array.Empty<DateEvent>(), Array.Empty<CalendarPlan>(), term, new[] { simultaneous }, Array.Empty<CourseChange>(), (_, _) => new(), day);
        Assert.Equal(4, reminders.Count); Assert.Equal(4, reminders.Select(item => item.Key).Distinct().Count());
        var sameTime = CalendarRules.CourseEntries(term, new[] { simultaneous }, Array.Empty<CourseChange>(), day, day);
        var ids = IcsExporter.Entries(sameTime).Replace("\r\n ", "").Split("\r\n").Where(line => line.StartsWith("UID:")).ToArray();
        Assert.Equal(2, ids.Distinct().Count());
        var originalId = ids.Single(id => id.Contains(second.Id.ToString("N")));
        var moved = CalendarRules.CourseEntries(term, new[] { simultaneous }, new[] { changes[1] }, day, day.AddDays(1));
        Assert.Contains(originalId, IcsExporter.Entries(moved).Replace("\r\n ", ""));
    }

    [Fact]
    public void ExistingCoursesAndMultiMeetingChangesSurviveReopenAndRemovingAMeetingCleansOnlyItsChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomMeetings", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var day = new DateOnly(2026, 10, 12);
            var course = new SchoolCourse(Guid.NewGuid(), "旧课程", 1, 1, 1, Room: "101"); store.SaveCourse(course);
            var original = new CourseChange(Guid.NewGuid(), course.Id, day, true); store.SaveCourseChange(original);
            store = new(path); Assert.Null(Assert.Single(store.GetCourses()).Meetings); Assert.Equal(Guid.Empty, Assert.Single(CalendarRules.Meetings(course)).Id);
            var meetings = new[] { CalendarRules.Meetings(course)[0], new CourseMeeting(Guid.NewGuid(), 1, 2, 2, Room: "202") };
            store.SaveCourse(course with { Meetings = meetings });
            var change = new CourseChange(Guid.NewGuid(), course.Id, day, false, day.AddDays(1), MeetingId: meetings[1].Id); store.SaveCourseChange(change);
            store = new(path); Assert.Equal(meetings, Assert.Single(store.GetCourses()).Meetings); Assert.Equal(2, store.GetCourseChanges().Count);
            store.SaveCourse(course with { Meetings = new[] { meetings[0] } }); Assert.Equal(original, Assert.Single(store.GetCourseChanges()));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ProjectPlansAndTaskPlansStayInSeparateCalendarsAndKeepSeparateViewPreferences()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarSplit", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 15); var project = store.GetProjects()[0].Id;
            var focusPlan = new CalendarPlan(Guid.NewGuid(), "项目计划", day, new(9, 0), 30, ProjectId: project);
            var taskPlan = focusPlan with { Id = Guid.NewGuid(), Title = "待办计划", TodoId = Guid.NewGuid(), ProjectId = null };
            var otherPlan = focusPlan with { Id = Guid.NewGuid(), Title = "独立安排", ProjectId = null };
            store.SaveCalendarPlan(focusPlan); store.SaveCalendarPlan(taskPlan); store.SaveCalendarPlan(otherPlan);
            store.SaveDateEvent(new(Guid.NewGuid(), "考试", day));
            var focus = new CalendarViewModel(store, true) { Mode = "周" }; var tasks = new CalendarViewModel(store) { Mode = "月" };
            Assert.Equal(focusPlan.Id, Assert.Single(focus.Entries(day, day)).Id);
            var taskEntries = tasks.Entries(day, day); Assert.Equal(2, taskEntries.Count); Assert.Contains(taskEntries, entry => entry.Id == taskPlan.Id);
            Assert.DoesNotContain(taskEntries, entry => entry.Id == focusPlan.Id || entry.Id == otherPlan.Id);
            Assert.Equal("周", new CalendarViewModel(store, true).Mode); Assert.Equal("月", new CalendarViewModel(store).Mode);
            Assert.DoesNotContain("课程表", focus.Modes); Assert.DoesNotContain("专注", tasks.Filters);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
