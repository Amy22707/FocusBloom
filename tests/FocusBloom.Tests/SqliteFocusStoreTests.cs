using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class SqliteFocusStoreTests
{
    [Fact]
    public void ExistingOrdinaryRecordsRemainReadableAfterAddingStrictMode()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomOldStore", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.db");
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE sessions (id TEXT PRIMARY KEY, project_id TEXT NOT NULL, mode INTEGER NOT NULL,
                        started_at TEXT NOT NULL, ended_at TEXT NOT NULL, end_reason INTEGER NOT NULL);
                    INSERT INTO sessions VALUES ('f39aab46-f0a4-4cc2-a4b6-674bd5fd4185',
                        '6c02cad7-dd19-4f50-9c75-567b3eac38d9', 0, '2026-10-09T10:00:00+00:00', '2026-10-09T10:01:00+00:00', 0);
                    """;
                command.ExecuteNonQuery();
            }
            var store = new SqliteFocusStore(path);
            Assert.False(Assert.Single(store.GetRecords()).Strict);
            Assert.False(Assert.Single(new SqliteFocusStore(path).GetRecords()).Strict);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ReopeningDatabaseRetainsProjectsIntervalsAndSettingsWithoutDuplicateRecords()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomTests", Guid.NewGuid().ToString());
        var path = Path.Combine(directory, "test.db");
        try
        {
            var store = new SqliteFocusStore(path);
            var project = new FocusProject(Guid.NewGuid(), "数学", "学习", "#7B97A6");
            store.SaveFolder("工作");
            store.SaveProject(project);
            store.SaveSetting("pauseMinutes", "3");
            var clock = new ManualClock();
            var timer = new FocusTimer(clock);
            timer.Start(new FocusOptions(project.Id, TimerMode.CountUp, TimeSpan.Zero,
                TimeSpan.FromMinutes(5), TimeSpan.Zero));
            clock.Advance(TimeSpan.FromMinutes(7));
            timer.Pause();
            clock.Advance(TimeSpan.FromMinutes(2));
            timer.End();
            store.SaveRecord(timer.LastRecord!);
            store.SaveRecord(timer.LastRecord!);

            var reopened = new SqliteFocusStore(path);
            Assert.Contains(project, reopened.GetProjects());
            Assert.Contains("学习", reopened.GetFolders());
            Assert.Contains("工作", reopened.GetFolders());
            Assert.Equal("3", reopened.GetSetting("pauseMinutes"));
            var record = Assert.Single(reopened.GetRecords());
            Assert.Equal(timer.LastRecord!.Id, record.Id);
            Assert.Equal(timer.LastRecord.Intervals, record.Intervals);
            Assert.Equal(TimeSpan.FromMinutes(7), record.FocusDuration);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
