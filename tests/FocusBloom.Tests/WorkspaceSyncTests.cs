using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Sync;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class WorkspaceSyncTests
{
    public sealed class LocalPostgresqlFactAttribute : FactAttribute
    {
        public LocalPostgresqlFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FOCUSBLOOM_PGLITE")))
                Skip = "Set FOCUSBLOOM_PGLITE to the local PostgreSQL test package.";
        }
    }

    [LocalPostgresqlFact]
    public async Task TwoLocalProfilesRoundTripThroughPostgresql()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "FocusBloom.sln"))) root = root.Parent!;
        var dependency = Environment.GetEnvironmentVariable("FOCUSBLOOM_PGLITE")!;
        var requestJson = new JsonSerializerOptions(SyncJson.Options) { IgnoreReadOnlyProperties = false };
        var start = new ProcessStartInfo("node") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add(Path.Combine(root.FullName, "tests", "postgresql", "workspace-bridge.mjs"));
        start.Environment["FOCUSBLOOM_PGLITE"] = dependency;
        using var bridge = Process.Start(start)!;
        var data = Path.Combine(Path.GetTempPath(), $"focusbloom-sync-{Guid.NewGuid():N}");
        try
        {
            var a = new SqliteFocusStore(Path.Combine(data, "A.db"));
            var b = new SqliteFocusStore(Path.Combine(data, "B.db"));
            var outsider = new SqliteFocusStore(Path.Combine(data, "other.db"));
            var group = new TodoFolder(Guid.NewGuid(), "复习", "学习");
            var todo = new TodoItem(Guid.NewGuid(), group.Id, "整理笔记", ResumeNote: "第2章", EstimatedMinutes: 45);
            var day = DateOnly.FromDateTime(DateTime.Today.AddDays(-1));
            var date = new DateEvent(Guid.NewGuid(), "截止", day, CoverPath: "private-cover.png");
            var reflection = new DailyReflection(day, 4, ["学习"], Feeling: "第一版", Photos: ["private-photo.png"]);
            var habit = new Habit(Guid.NewGuid(), "阅读", [1, 2, 3, 4, 5, 6, 7], day);
            var project = a.GetProjects().Single();
            a.SaveProject(project with { Plan = new("完成复习", day.AddDays(7), [new("整理讲义", true)]) });
            a.SaveTodoCategory("学习"); a.SaveTodoFolder(group); a.SaveTodo(todo);
            a.SaveDateEvent(date); a.SaveDailyReflection(reflection); a.SaveHabit(habit); a.SetHabitCheckIn(habit.Id, day, true);
            for (var i = 0; i < 104; i++) a.SaveCalendarPlan(new(Guid.NewGuid(), $"计划 {i}", day, new TimeOnly(9, 0), 30));
            var at = DateTimeOffset.UtcNow.AddMinutes(-1);
            var record = new FocusRecord(Guid.NewGuid(), SqliteFocusStore.DefaultProjectId, TimerMode.CountUp, at, at.AddSeconds(30), EndReason.Manual, [new(FocusBloom.Core.Focus.IntervalKind.Focus, at, at.AddSeconds(30))], TodoId: todo.Id);
            a.SaveRecord(record); a.SaveOutcome(new(record.Id, "两页笔记", 2, "页", "继续整理"));
            await Sync(a); await Sync(b); await Sync(outsider, "other");
            Assert.Equal(todo.ResumeNote, Assert.Single(b.GetTodos()).ResumeNote);
            var plan = Assert.Single(b.GetProjects()).Plan!;
            Assert.Equal("完成复习", plan.Goal); Assert.True(Assert.Single(plan.Milestones).Completed);
            Assert.Equal(todo.Id, Assert.Single(b.GetRecords()).TodoId);
            Assert.Equal("继续整理", b.GetOutcome(record.Id)?.NextStep);
            Assert.Null(Assert.Single(b.GetDateEvents()).CoverPath);
            Assert.Null(Assert.Single(b.GetDailyReflections()).Photos);
            Assert.Single(b.GetHabitCheckIns()); Assert.Equal(104, b.GetCalendarPlans().Count);
            Assert.Empty(outsider.GetTodos()); Assert.Empty(outsider.GetDailyReflections());

            // Both clients edit while disconnected; reconnect and resolve the same-row conflict.
            b.SaveTodo(todo with { Title = "设备 B 修改" });
            a.SaveTodo(todo with { Title = "设备 A 修改" }); await Sync(a); await Sync(b);
            var conflict = Assert.Single(b.SyncConflicts());
            Assert.Equal("设备 B 修改", Assert.Single(b.GetTodos()).Title);
            b.ResolveSyncConflict(conflict, keepLocal: true); await Sync(b); await Sync(a);
            Assert.Equal("设备 B 修改", Assert.Single(a.GetTodos()).Title);
            b.SaveDailyReflection(reflection with { Feeling = "另一端补记", Photos = null }); b.SetHabitCheckIn(habit.Id, day, false);
            b.DeleteTodo(todo.Id); b.DeleteDateEvent(date.Id); await Sync(b); await Sync(a);
            Assert.Equal("另一端补记", Assert.Single(a.GetDailyReflections()).Feeling);
            Assert.Equal("private-photo.png", Assert.Single(Assert.Single(a.GetDailyReflections()).Photos!));
            Assert.Empty(a.GetTodos()); Assert.Empty(a.GetDateEvents()); Assert.Empty(a.GetHabitCheckIns());
            Assert.Equal(todo.Id, Assert.Single(a.GetRecords()).TodoId);
            Assert.Equal(0, a.PendingSyncCount); Assert.Equal(0, b.PendingSyncCount);
        }
        finally
        {
            bridge.StandardInput.Close();
            await bridge.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(data))
            {
                foreach (var file in Directory.GetFiles(data)) File.Delete(file);
                Directory.Delete(data);
            }
        }

        async Task Sync(SqliteFocusStore store, string user = "one")
        {
            foreach (var workspace in new[] { false, true })
            {
                for (var page = 0; ; page++)
                {
                    Assert.True(page < 20, "Sync did not drain its queue.");
                    var request = store.PrepareSync(workspace);
                    await bridge.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { user, workspace, request.Cursor, request.Changes }, requestJson));
                    await bridge.StandardInput.FlushAsync();
                    var line = await bridge.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                    Assert.NotNull(line);
                    var response = JsonSerializer.Deserialize<SyncResponse>(line, SyncJson.Options)!;
                    store.ApplySync(request, response, workspace);
                    if (!response.HasMore && response.Accepted.Count < 50) break;
                }
            }
        }
    }
}
