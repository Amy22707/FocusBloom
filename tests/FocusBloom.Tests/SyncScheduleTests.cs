using System.IO;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class SyncScheduleTests
{
    [Fact]
    public void PendingChangesAreBatchedAndFailedAttemptSurvivesRestart()
    {
        var path = Path.Combine(Path.GetTempPath(), $"focusbloom-schedule-{Guid.NewGuid():N}.db");
        try
        {
            var store = new SqliteFocusStore(path);
            var first = store.ObserveSyncSchedule();
            var started = DateTimeOffset.UtcNow;
            Assert.True(first.IsDue(started));
            store.RecordSyncAttempt(first.Revision, started); // Simulate an unsuccessful network request.
            store = new SqliteFocusStore(path);
            var unchanged = store.ObserveSyncSchedule();
            Assert.True(unchanged.HasPending);
            Assert.False(unchanged.IsDue(started.AddDays(6)));
            Assert.True(unchanged.IsDue(started.AddDays(7)));

            store.SaveSetting("pauseMinutes", "15");
            var changed = store.ObserveSyncSchedule();
            var detected = changed.ChangedAt!.Value;
            Assert.False(changed.IsDue(detected.AddSeconds(119)));
            Assert.True(changed.IsDue(detected.AddMinutes(2)));
            store.RecordSyncAttempt(changed.Revision, detected.AddMinutes(2));
            Assert.False(store.ObserveSyncSchedule().IsDue(detected.AddHours(1)));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
    }
}
