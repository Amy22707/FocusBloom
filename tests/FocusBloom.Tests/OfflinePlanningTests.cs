using System.IO;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class OfflinePlanningTests
{
    [Fact]
    public void PeriodicChecklistsKeepSubdivisionsSeparateAndPersistCompletion()
    {
        WithStore((store, path) =>
        {
            var day = new DateOnly(2026, 11, 1);
            var next = day.AddDays(1);
            var daily = ChecklistScope.For(ChecklistPeriod.Day, day);
            var weekly = ChecklistScope.For(ChecklistPeriod.Week, day);
            var weekDay = ChecklistScope.For(ChecklistPeriod.Week, day, subdivided: true);
            var monthly = ChecklistScope.For(ChecklistPeriod.Month, day);
            var monthWeek = ChecklistScope.For(ChecklistPeriod.Month, day, subdivided: true);
            var scopes = new[] { daily, weekly, weekDay, monthly, monthWeek };
            var lists = new PeriodicChecklist(store);
            var item = new ChecklistEntry(Guid.NewGuid(), "Read chapter 1", false);
            foreach (var scope in scopes) lists.Save(scope, [item with { Text = scope.ToString() }]);
            lists.Save(weekDay, [item with { Done = true }]);

            var reopened = new PeriodicChecklist(new SqliteFocusStore(path));
            foreach (var scope in scopes.Where(scope => scope != weekDay))
                Assert.Equal(item with { Text = scope.ToString() }, Assert.Single(reopened.Read(scope)));
            Assert.Equal(item with { Done = true }, Assert.Single(reopened.Read(weekDay)));
            Assert.Equal(monthly, ChecklistScope.For(ChecklistPeriod.Month, next));
            Assert.Empty(reopened.Read(ChecklistScope.For(ChecklistPeriod.Month, next, subdivided: true)));
            Assert.Empty(reopened.Read(ChecklistScope.For(ChecklistPeriod.Day, next)));
        });
    }

    [Fact]
    public void PresetCreatesLinkedEditableTasksAndStepsAndProgressUseRealRecords()
    {
        WithStore((store, path) =>
        {
            var day = new DateOnly(2026, 11, 4);
            var preset = ProjectPreset.All.Single(item => item.Name == "阅读计划");
            var project = new FocusProject(Guid.NewGuid(), "阅读札记", "学习", "#789989", new(preset.Goal, day, preset.Milestones));
            store.CreateProjectFromPreset(project, preset);
            var todos = store.GetTodos().ToArray();
            Assert.Equal(preset.Tasks.Select(item => item.Title), todos.Select(item => item.Title));
            Assert.All(todos, item => Assert.Equal(project.Id, item.PreferredProjectId));
            var first = todos[0];
            store.SaveTodo(first with { Steps = [new("浏览章节", true), new("标记重点")] });
            var reopened = new SqliteFocusStore(path);
            var saved = reopened.GetTodos().Single(item => item.Id == first.Id);
            Assert.Equal(new[] { true, false }, saved.Steps!.Select(step => step.Completed));
            Assert.Equal(TodoStatus.Pending, saved.Status);
            reopened.SaveTodo(saved with { Steps = [] });
            Assert.Null(reopened.GetTodos().Single(item => item.Id == first.Id).Steps);
            reopened.SaveTodo(todos[1] with { Status = TodoStatus.Completed });
            var start = new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero);
            reopened.SaveRecord(new(Guid.NewGuid(), project.Id, TimerMode.CountUp, start, start.AddMinutes(8),
                EndReason.Manual, [new(IntervalKind.Focus, start, start.AddMinutes(8))], TodoId: first.Id));
            reopened.SaveCalendarPlan(new(Guid.NewGuid(), "读书安排", day, new(10, 0), TodoId: first.Id, ProjectId: project.Id));
            var progress = ProjectProgress.Calculate(project.Id, day, reopened.GetTodos(), reopened.GetRecords(), reopened.GetCalendarPlans());
            Assert.Equal(1, progress.CompletedCount); Assert.Equal(8, progress.Invested.TotalMinutes);
            Assert.Equal(37, progress.EstimatedRemainingMinutes);
            Assert.Equal(todos[2].Id, Assert.Single(progress.Unscheduled).Id);
            var entry = Assert.Single(new CalendarViewModel(reopened).Entries(day, day), item => item.Kind == "项目截止");
            Assert.Equal(project.Id, entry.ProjectId);
        });
    }

    [Fact]
    public void SwitchingSemestersPreservesLegacyCoursesPeriodsAndChanges()
    {
        WithStore((store, path) =>
        {
            var first = new DateOnly(2026, 9, 7);
            store.SaveSchoolTerm(new(first, 18, [new(1, new(8, 10), new(9, 0))]));
            var course = new SchoolCourse(Guid.NewGuid(), "设计史", 1, 1, 1, LastWeek: 18);
            store.SaveCourse(course);
            var change = new CourseChange(Guid.NewGuid(), course.Id, first, true);
            store.SaveCourseChange(change);
            store.RenameSchoolSemester("秋季");
            var autumn = Assert.Single(store.GetSchoolSemesters());

            var second = new DateOnly(2027, 3, 1);
            store.CreateSchoolSemester("春季", second);
            Assert.Empty(store.GetCourses()); Assert.Empty(store.GetCourseChanges());
            var spring = store.GetSchoolSemesters().Single(item => item.Active);
            var other = new SchoolCourse(Guid.NewGuid(), "城市观察", 2, 1, 2);
            store.SaveCourse(other);
            Assert.Throws<InvalidOperationException>(() => store.CreateSchoolSemester("秋季", second));
            Assert.Equal(other, Assert.Single(store.GetCourses()));

            store = new SqliteFocusStore(path);
            store.SelectSchoolSemester(autumn.Id);
            Assert.Equal(course, Assert.Single(store.GetCourses()));
            Assert.Equal(change, Assert.Single(store.GetCourseChanges()));
            Assert.Equal(new TimeOnly(8, 10), Assert.Single(store.GetSchoolTerm().Periods).Start);
            store.SelectSchoolSemester(spring.Id);
            Assert.Equal(other, Assert.Single(store.GetCourses()));
            Assert.Empty(store.GetCourseChanges());
            Assert.Equal(second, store.GetSchoolTerm().Start);
        });
    }

    private static void WithStore(Action<SqliteFocusStore, string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"focusbloom-offline-planning-{Guid.NewGuid():N}.db");
        try { action(new(path), path); }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
