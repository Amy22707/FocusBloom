using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusBloom.Tests;

public sealed class ReflectionTests
{
    [Fact]
    public void ReflectionRequiresSomeContentNormalizesTagsAndRejectsFutureDates()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var normalized = ReflectionRules.Normalize(new(today, 4, new[] { " 学习 ", "学习", "", "运动" }, " 还不错 ", "完成阅读", "写笔记"), today);
        Assert.Equal(new[] { "学习", "运动" }, normalized.Tags); Assert.Equal("还不错", normalized.Feeling);
        Assert.Null(ReflectionRules.Normalize(new(today.AddYears(-10), null, Array.Empty<string>(), Highlights: "以前的一天"), today).Mood);
        Assert.Throws<InvalidOperationException>(() => ReflectionRules.Normalize(new(today, null, Array.Empty<string>()), today));
        Assert.Throws<InvalidOperationException>(() => ReflectionRules.Normalize(new(today.AddDays(1), 3, Array.Empty<string>()), today));
        Assert.Throws<InvalidOperationException>(() => ReflectionRules.Normalize(new(today, 99, Array.Empty<string>()), today));
    }
    [Fact]
    public void DailyReflectionIsUniqueByDateSurvivesReopenAndDeletesWithoutTouchingFocusData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomReflectionStore", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var today = DateOnly.FromDateTime(DateTime.Today);
            store.SaveSetting("existing.preference", "保留"); store.SaveDailyReflection(new(today, 3, new[] { "学习" }, "平静的一天")); store.SaveDailyReflection(new(today.AddDays(-1), 4, Array.Empty<string>()));
            store.SaveDailyReflection(new(today, 5, new[] { "学习", "学习" }, "愉快", "完成实验", "整理总结")); store = new(path);
            Assert.Equal(2, store.GetDailyReflections().Count); var entry = store.GetDailyReflections().Single(item => item.Date == today); Assert.Equal(5, entry.Mood); Assert.Equal("整理总结", entry.NextStep); Assert.Single(entry.Tags);
            Assert.Throws<InvalidOperationException>(() => store.SaveDailyReflection(new(today.AddDays(1), 3, Array.Empty<string>())));
            store.DeleteDailyReflection(today); Assert.Single(store.GetDailyReflections()); Assert.Equal("保留", store.GetSetting("existing.preference")); Assert.Single(store.GetProjects());
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void DaySummarySplitsFocusTimeAndUsesActualCompletionDatesAndCheckIns()
    {
        var day = new DateOnly(2026, 10, 10); DateTimeOffset At(DateOnly date, int hour, int minute = 0) { var local = date.ToDateTime(new(hour, minute)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
        var record = new FocusRecord(Guid.NewGuid(), Guid.NewGuid(), TimerMode.CountUp, At(day, 23, 40), At(day.AddDays(1), 0, 20), EndReason.Manual,
            new[] { new FocusInterval(IntervalKind.Focus, At(day, 23, 40), At(day, 23, 50)), new FocusInterval(IntervalKind.Pause, At(day, 23, 50), At(day.AddDays(1), 0, 5)), new FocusInterval(IntervalKind.Focus, At(day.AddDays(1), 0, 5), At(day.AddDays(1), 0, 20)) });
        var todo = new TodoItem(Guid.NewGuid(), Guid.NewGuid(), "完成实验", TodoStatus.Completed, CompletedAt: At(day, 18)); var unknown = todo with { Id = Guid.NewGuid(), CompletedAt = null }; var pending = todo with { Id = Guid.NewGuid(), Status = TodoStatus.Pending };
        var habit = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), day); var second = habit with { Id = Guid.NewGuid(), Name = "运动" }; var checks = new[] { new HabitCheckIn(habit.Id, day) };
        var outcome = new FocusOutcome(record.Id, "读完资料", 3, "页", "整理笔记");
        var summary = ReflectionRules.Summary(day, new[] { record }, new[] { todo, unknown, pending }, new[] { habit, second }, checks, new[] { outcome });
        Assert.Equal(TimeSpan.FromMinutes(10), summary.Duration); Assert.Equal(0, summary.Sessions); Assert.Equal(todo.Id, Assert.Single(summary.CompletedTodos).Id); Assert.Equal(1, summary.CheckedHabits); Assert.Equal(2, summary.PlannedHabits); Assert.Empty(summary.Outcomes);
        var next = ReflectionRules.Summary(day.AddDays(1), new[] { record }, new[] { todo }, new[] { habit, second }, checks, new[] { outcome }); Assert.Equal(TimeSpan.FromMinutes(15), next.Duration); Assert.Equal(1, next.Sessions); Assert.Equal(outcome, Assert.Single(next.Outcomes)); Assert.Empty(next.CompletedTodos);
    }
    [Fact]
    public void TodoCompletionDateSurvivesEditsAndOldCompletedRowsKeepUnknownDates()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCompletionDates", Guid.NewGuid().ToString());
        try
        {
            var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var folder = new TodoFolder(Guid.NewGuid(), "事项", "未分类"); store.SaveTodoFolder(folder);
            var todo = new TodoItem(Guid.NewGuid(), folder.Id, "实验"); store.SaveTodo(todo); Assert.Null(Assert.Single(store.GetTodos()).CompletedAt);
            var at = DateTimeOffset.Now.AddDays(-2); store.SaveTodo(todo with { Status = TodoStatus.Completed, CompletedAt = at }); Assert.Equal(at, Assert.Single(store.GetTodos()).CompletedAt);
            store.SaveTodo(todo with { Status = TodoStatus.Completed, Title = "实验总结" }); Assert.Equal(at, Assert.Single(store.GetTodos()).CompletedAt);
            store.SaveTodo(todo with { Status = TodoStatus.Pending }); Assert.Null(Assert.Single(store.GetTodos()).CompletedAt);
            store.SaveTodo(todo with { Status = TodoStatus.Completed }); Assert.Equal(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(Assert.Single(store.GetTodos()).CompletedAt!.Value.LocalDateTime));
            using (var connection = new SqliteConnection("Data Source=" + path)) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "ALTER TABLE todo_items DROP COLUMN completed_at"; command.ExecuteNonQuery(); }
            store = new(path); Assert.Equal(TodoStatus.Completed, Assert.Single(store.GetTodos()).Status); Assert.Null(Assert.Single(store.GetTodos()).CompletedAt);
            store.SaveTodo(todo with { Status = TodoStatus.Completed, Notes = "补充说明" }); Assert.Null(Assert.Single(store.GetTodos()).CompletedAt);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void DailyRemindersAreOptionalSkipRecordedDaysAndSaveTheirPreferences()
    {
        var today = DateOnly.FromDateTime(DateTime.Today); Assert.Empty(ReflectionReminders.Build(new(), Array.Empty<DailyReflection>(), today));
        var options = new ReflectionReminder(true, new(21, 30)); var reminders = ReflectionReminders.Build(options, new[] { new DailyReflection(today, null, Array.Empty<string>(), Highlights: "复盘") }, today);
        Assert.Equal(30, reminders.Count); Assert.DoesNotContain(reminders, item => DateOnly.FromDateTime(item.At.LocalDateTime) == today); Assert.All(reminders, item => { Assert.Equal(21, item.At.LocalDateTime.Hour); Assert.Equal(30, item.At.LocalDateTime.Minute); Assert.Equal("心情", item.Kind); Assert.DoesNotContain("复盘内容", item.Detail); });
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomReflectionPreferences", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var model = new ReflectionViewModel(store); model.SaveReminder(options); model.SummaryExpanded = true;
            model.Save(new(today, 3, Array.Empty<string>())); model.Date = today.AddYears(-10).ToDateTime(TimeOnly.MinValue); Assert.True(model.CanEdit); model.Save(new(model.Day, 4, new[] { "回忆" })); Assert.Equal(today.AddYears(-10), model.Day);
            var reopened = new ReflectionViewModel(store); Assert.Equal(options, reopened.Reminder); Assert.True(reopened.SummaryExpanded); Assert.Equal(2, reopened.Entries.Count); model.Date = today.AddDays(1).ToDateTime(TimeOnly.MinValue); Assert.False(model.CanEdit);
        }
        finally { SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
