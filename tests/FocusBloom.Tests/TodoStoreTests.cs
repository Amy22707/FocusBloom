using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class TodoStoreTests
{
    [Fact]
    public void TodoMetadataAssociationAndOutcomesSurviveReopenAndDeletion()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomTodoStore", Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "test.db");
        try
        {
            var store = new SqliteFocusStore(path);
            store.SaveTodoCategory("学习");
            var folder = new TodoFolder(Guid.NewGuid(), "论文", "学习"); store.SaveTodoFolder(folder);
            var todo = new TodoItem(Guid.NewGuid(), folder.Id, "阅读文献", TodoStatus.InProgress, TodoPriority.High,
                new DateOnly(2026, 10, 20), new TimeOnly(18, 30), "摘录关键论点", 60, "从第三篇继续", store.GetProjects()[0].Id, true, true);
            store.SaveTodo(todo);
            var clock = new ManualClock(); var timer = new FocusTimer(clock);
            timer.Start(new(todo.PreferredProjectId!.Value, TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero, Strict: true, TodoId: todo.Id));
            timer.RegisterInterception(); timer.RegisterInterception();
            clock.Advance(TimeSpan.FromSeconds(20)); timer.End();
            store.SaveRecord(timer.LastRecord!);
            var outcome = new FocusOutcome(timer.LastRecord!.Id, "读完一篇", 3, "页", "整理摘录"); store.SaveOutcome(outcome);
            store = new SqliteFocusStore(path);
            Assert.Equal(todo, Assert.Single(store.GetTodos())); Assert.Equal(outcome, store.GetOutcome(outcome.SessionId));
            Assert.Equal(todo.Id, Assert.Single(store.GetRecords()).TodoId);
            Assert.Equal(2, Assert.Single(store.GetRecords()).InterceptionCount);
            store.RenameTodoCategory("学习", "研究"); Assert.Equal("研究", Assert.Single(store.GetTodoFolders()).Category);
            store.DeleteTodoFolder(folder.Id);
            Assert.Empty(store.GetTodos()); Assert.Empty(store.GetTodoFolders());
            Assert.Equal(todo.Id, Assert.Single(store.GetTodos(true)).Id);
            Assert.Equal(todo.Id, Assert.Single(store.GetRecords()).TodoId); Assert.Equal(outcome, store.GetOutcome(outcome.SessionId));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void OutcomesAlsoSupportSessionsWithoutTodosAndCanBeEdited()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomOutcomeStore", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var clock = new ManualClock(); var timer = new FocusTimer(clock);
            timer.Start(new(store.GetProjects()[0].Id, TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero));
            clock.Advance(TimeSpan.FromMinutes(2)); timer.End(); store.SaveRecord(timer.LastRecord!);
            var outcome = new FocusOutcome(timer.LastRecord!.Id, "完成草稿", NextStep: "检查引用"); store.SaveOutcome(outcome);
            store.SaveOutcome(outcome with { Summary = "完成修订稿" });
            Assert.Null(Assert.Single(store.GetRecords()).TodoId);
            Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(store.GetRecords()).FocusDuration);
            Assert.Equal("完成修订稿", Assert.Single(store.GetOutcomes()).Summary);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
