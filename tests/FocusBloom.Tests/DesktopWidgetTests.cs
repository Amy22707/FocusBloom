using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class DesktopWidgetTests
{
    private sealed class Clock(DateTimeOffset at) : TimeProvider { private DateTimeOffset _now = at; public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan value) => _now += value; }
    [Fact]
    public void LiveFocusSnapshotExcludesPausesSplitsMidnightAndDoesNotDoubleCountSavedSessions()
    {
        var day = new DateOnly(2026, 10, 16); var local = day.ToDateTime(new(23, 50)); var clock = new Clock(new(local, TimeZoneInfo.Local.GetUtcOffset(local))); var timer = new FocusTimer(clock);
        timer.Start(new(Guid.NewGuid(), TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero)); clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(5), timer.CaptureCurrentRecord()!.FocusDuration); timer.Pause(); clock.Advance(TimeSpan.FromMinutes(3)); Assert.Equal(TimeSpan.FromMinutes(5), timer.CaptureCurrentRecord()!.FocusDuration);
        timer.Resume(); clock.Advance(TimeSpan.FromMinutes(5)); var current = timer.CaptureCurrentRecord()!;
        var first = FocusStatistics.Build(Array.Empty<FocusRecord>(), new(day, day)); var next = FocusStatistics.Build(Array.Empty<FocusRecord>(), new(day.AddDays(1), day.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(7), LiveFocusStatistics.Build(first, current).Duration); Assert.Equal(TimeSpan.FromMinutes(3), LiveFocusStatistics.Build(next, current).Duration);
        var other = FocusStatistics.Build(Array.Empty<FocusRecord>(), new(day, day.AddDays(1), new HashSet<Guid> { Guid.NewGuid() })); Assert.Equal(TimeSpan.Zero, LiveFocusStatistics.Build(other, current).Duration);
        timer.End(); Assert.Null(timer.CaptureCurrentRecord()); var pending = LiveFocusStatistics.Build(next, timer.LastRecord); var saved = FocusStatistics.Build(new[] { timer.LastRecord! }, next.Filter); var combined = LiveFocusStatistics.Build(saved, timer.LastRecord);
        Assert.Equal(pending.Duration, combined.Duration); Assert.False(combined.IncludesCurrent); Assert.Equal(TimeSpan.FromMinutes(3), combined.Duration);
    }
    [Fact]
    public void RestDoesNotContributeLiveTimeAndSnapshotsDoNotChangeTheTimer()
    {
        var now = DateTimeOffset.Now; var clock = new Clock(now); var timer = new FocusTimer(clock); timer.Start(new(Guid.NewGuid(), TimerMode.Countdown, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(20)));
        clock.Advance(TimeSpan.FromSeconds(6)); var first = timer.CaptureCurrentRecord(); var second = timer.CaptureCurrentRecord(); Assert.Equal(first!.Id, second!.Id); Assert.Equal(TimeSpan.FromSeconds(6), first.FocusDuration); Assert.Equal(FocusState.Focusing, timer.State);
        clock.Advance(TimeSpan.FromSeconds(4)); timer.Update(); Assert.Equal(FocusState.Resting, timer.State); Assert.Null(timer.CaptureCurrentRecord()); Assert.Equal(TimeSpan.FromSeconds(10), timer.LastRecord!.FocusDuration);
        clock.Advance(TimeSpan.FromSeconds(8)); Assert.Null(timer.CaptureCurrentRecord()); Assert.Equal(TimeSpan.FromSeconds(10), timer.LastRecord.FocusDuration);
    }
    [Fact]
    public void WidgetPreferencesPersistIndependentViewsScopesSelectionAndGeometry()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomWidgetPreferences", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var model = new DesktopWidgetsViewModel(store); Assert.Equal(6, model.Items.Count); Assert.All(model.Items, item => Assert.False(item.Enabled));
            var todos = model[DesktopWidgetKind.Todos]; var dates = model[DesktopWidgetKind.Dates]; var id = Guid.NewGuid();
            todos.View = "四象限"; todos.FolderId = id; todos.IncludeCompleted = true; todos.Enabled = true; todos.Transparency = 35; todos.Topmost = true; todos.SaveBounds(40, 50, 480, 420, "test-monitor");
            dates.AllItems = false; Assert.False(dates.Includes(id)); dates.SelectItem(id, true); Assert.True(dates.Includes(id)); dates.View = "大字";
            todos.BackgroundMode = "纯色"; todos.BackgroundColor = "#29443E"; todos.BackgroundImage = "D:/Pictures/forest.png";
            var reopened = new DesktopWidgetsViewModel(new SqliteFocusStore(Path.Combine(directory, "test.db"))); var restored = reopened[DesktopWidgetKind.Todos];
            Assert.Equal(todos.Preferences, restored.Preferences); Assert.Equal("大字", reopened[DesktopWidgetKind.Dates].View); Assert.True(reopened[DesktopWidgetKind.Dates].Includes(id)); Assert.False(reopened[DesktopWidgetKind.Habits].Enabled);
            Assert.Equal("跟随主题", reopened[DesktopWidgetKind.Calendar].BackgroundMode); Assert.Contains("课程表", reopened[DesktopWidgetKind.Calendar].Views);
            restored.Enabled = false; Assert.True(restored.IncludeCompleted); Assert.Equal(id, restored.FolderId); Assert.Equal(480, restored.Width); Assert.Equal(35, restored.Transparency);
            dates.SelectItem(id, false); Assert.False(dates.Includes(id)); dates.AllItems = true; Assert.True(dates.Includes(id));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
