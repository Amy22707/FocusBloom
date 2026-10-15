using System.IO;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class CalendarStoreTests
{
    [Fact]
    public void CalendarDataAndReminderSettingsSurviveReopenAndDeletingACourseClearsItsChanges()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarStore", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path);
            var dates = new DateEvent(Guid.NewGuid(), "考试", new(2026, 10, 20), new(15, 0), Repeat: DateRepeat.Yearly, CoverPath: "cover.png", Pinned: true, Reminder: new(true, 45));
            var plan = new CalendarPlan(Guid.NewGuid(), "复习", new(2026, 10, 19), new(14, 0), 90, Reminder: new(false));
            var term = new SchoolTerm(new(2026, 9, 7), 20, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)) });
            var course = new SchoolCourse(Guid.NewGuid(), "数学", 1, 1, 1, Pattern: WeekPattern.Even, Weeks: "2,4-10", Room: "202");
            var change = new CourseChange(Guid.NewGuid(), course.Id, new(2026, 9, 14), false, new(2026, 9, 15));
            store.SaveDateEvent(dates); store.SaveCalendarPlan(plan); store.SaveSchoolTerm(term); store.SaveCourse(course); store.SaveCourseChange(change);
            var id = Guid.NewGuid(); store.SaveReminderOptions("待办", id, new(false, 20, new(9, 0))); store.MarkReminderHandled("test-key");
            store = new(path);
            Assert.Equal(dates, Assert.Single(store.GetDateEvents())); Assert.Equal(plan, Assert.Single(store.GetCalendarPlans())); Assert.Equal(course, Assert.Single(store.GetCourses())); Assert.Equal(change, Assert.Single(store.GetCourseChanges()));
            Assert.Equal(term.Start, store.GetSchoolTerm().Start); Assert.Equal(term.Periods, store.GetSchoolTerm().Periods);
            Assert.False(store.GetReminderOptions("待办", id).Enabled); Assert.True(store.IsReminderHandled("test-key"));
            store.DeleteCourse(course.Id); Assert.Empty(store.GetCourses()); Assert.Empty(store.GetCourseChanges()); Assert.Single(store.GetDateEvents());
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void CalendarKeepsDeadlineSeparateFromPlansAndSplitsActualFocusAtMidnight()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarEntries", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 14);
            var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); store.SaveTodoFolder(folder);
            var todo = new TodoItem(Guid.NewGuid(), folder.Id, "阅读", DueDate: day, EstimatedMinutes: 90); store.SaveTodo(todo);
            var project = store.GetProjects()[0].Id;
            DateTimeOffset At(int hour, int minute, int next = 0) { var local = day.AddDays(next).ToDateTime(new(hour, minute)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
            var record = new FocusRecord(Guid.NewGuid(), project, TimerMode.CountUp, At(23, 50), At(0, 20, 1), EndReason.Manual,
                new[] { new FocusInterval(IntervalKind.Focus, At(23, 50), At(0, 0, 1)), new FocusInterval(IntervalKind.Pause, At(0, 0, 1), At(0, 10, 1)), new FocusInterval(IntervalKind.Focus, At(0, 10, 1), At(0, 20, 1)) }, TodoId: todo.Id);
            store.SaveRecord(record);
            var model = new CalendarViewModel(store) { Mode = "日", Date = day };
            var entries = model.Entries(day, day.AddDays(1)); var deadline = Assert.Single(entries, entry => entry.Kind == "待办");
            Assert.Null(deadline.Start); Assert.DoesNotContain(entries, entry => entry.Kind == "安排");
            Assert.DoesNotContain(entries, entry => entry.Kind == "专注");
            var focusModel = new CalendarViewModel(store, true);
            var focusEntries = focusModel.Entries(day, day.AddDays(1)); Assert.DoesNotContain(focusEntries, entry => entry.Kind == "待办");
            var focus = focusEntries.Where(entry => entry.Kind == "专注").ToArray(); Assert.Equal(2, focus.Length); Assert.Equal(1440, focus[0].EndMinutes); Assert.Equal(new TimeOnly(0, 10), focus[1].Start);
            model.Mode = "周"; model.Move(1); Assert.Equal(day.AddDays(7), model.Date); model.Mode = "月"; model.Move(-1); Assert.Equal(day.AddDays(7).AddMonths(-1), model.Date);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
