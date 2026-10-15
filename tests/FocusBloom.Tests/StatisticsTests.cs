using System.IO;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class StatisticsTests
{
    private static DateTimeOffset At(DateOnly day, int hour, int minute = 0) { var local = day.ToDateTime(new(hour, minute)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
    private static FocusRecord Session(Guid project, DateOnly day, int hour, int minutes, Guid? todo = null) => new(Guid.NewGuid(), project, TimerMode.CountUp, At(day, hour), At(day, hour).AddMinutes(minutes), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, At(day, hour), At(day, hour).AddMinutes(minutes)) }, TodoId: todo);

    [Fact]
    public void MidnightSplitsTimeExcludesPauseAndCountsOnlyOnEndDate()
    {
        var day = new DateOnly(2026, 10, 15); var project = Guid.NewGuid();
        var crossing = new FocusRecord(Guid.NewGuid(), project, TimerMode.Pomodoro, At(day, 23, 40), At(day.AddDays(1), 0, 20), EndReason.CountdownElapsed,
            new[] { new FocusInterval(IntervalKind.Focus, At(day, 23, 40), At(day, 23, 50)), new FocusInterval(IntervalKind.Pause, At(day, 23, 50), At(day.AddDays(1), 0, 10)), new FocusInterval(IntervalKind.Focus, At(day.AddDays(1), 0, 10), At(day.AddDays(1), 0, 20)) });
        var regular = Session(project, day.AddDays(1), 9, 30); var records = new[] { crossing, regular };
        var first = FocusStatistics.Build(records, new(day, day)); Assert.Equal(TimeSpan.FromMinutes(10), first.Duration); Assert.Equal(0, first.Count); Assert.Equal(TimeSpan.Zero, first.Average); Assert.Single(first.Sessions);
        var second = FocusStatistics.Build(records, new(day.AddDays(1), day.AddDays(1))); Assert.Equal(TimeSpan.FromMinutes(40), second.Duration); Assert.Equal(2, second.Count); Assert.Equal(TimeSpan.FromMinutes(25), second.Average);
        var all = FocusStatistics.Build(records, new(day, day.AddDays(1))); Assert.Equal(TimeSpan.FromMinutes(50), all.Duration); Assert.Equal(2, all.ActiveDays); Assert.Equal(2, all.Count);
        Assert.Equal(all.Duration, TimeSpan.FromTicks(all.Days.Sum(item => item.Duration.Ticks))); Assert.Equal(all.Duration, TimeSpan.FromTicks(FocusStatistics.Hours(all).Sum(item => item.Duration.Ticks)));
        var late = FocusStatistics.Build(records, new(day, day, Hour: 23)); Assert.Equal(TimeSpan.FromMinutes(10), late.Duration); Assert.Equal(0, late.Count); Assert.Single(late.Sessions);
        var midnightEnd = Session(project, day, 23, 60); var boundary = FocusStatistics.Build(new[] { midnightEnd }, new(day.AddDays(1), day.AddDays(1))); Assert.Equal(1, boundary.Count); Assert.Equal(TimeSpan.Zero, boundary.Duration); Assert.Equal(TimeSpan.FromHours(1), boundary.Average);
    }

    [Fact]
    public void ProjectAndTodoFiltersAndTrendBucketsPreserveExactTotals()
    {
        var day = new DateOnly(2026, 2, 28); var first = Guid.NewGuid(); var second = Guid.NewGuid(); var todo = Guid.NewGuid();
        var shortSession = Session(first, day, 14, 1, todo) with { Intervals = new[] { new FocusInterval(IntervalKind.Focus, At(day, 14), At(day, 14).AddSeconds(12).AddTicks(345)) }, EndedAt = At(day, 14).AddSeconds(12).AddTicks(345) };
        var records = new[] { shortSession, Session(second, day, 15, 30, todo), Session(first, day.AddDays(1), 16, 20, Guid.NewGuid()) };
        var filter = new StatisticsFilter(day, day.AddDays(1), new HashSet<Guid> { first }, todo); var data = FocusStatistics.Build(records, filter);
        Assert.Single(data.Projects); Assert.Single(data.Sessions); Assert.Equal(shortSession.FocusDuration, data.Duration); Assert.Equal(1, data.Count);
        var complete = FocusStatistics.Build(records, new(new(2026, 1, 1), new(2026, 12, 31))); var months = FocusStatistics.Trend(complete, false, true); Assert.Equal(12, months.Count); Assert.Equal(2, months[1].Count); Assert.Equal(1, months[2].Count);
        Assert.Equal(complete.Duration, TimeSpan.FromTicks(months.Sum(item => item.Duration.Ticks))); Assert.Equal(3, months.Sum(item => item.Count));
        Assert.Equal(24, FocusStatistics.Trend(data, true, false).Count); Assert.Equal(2, FocusStatistics.Trend(data, false, false).Count);
        var empty = FocusStatistics.Build(records, new(day, day.AddDays(-1))); Assert.Empty(empty.Sessions); Assert.Equal(TimeSpan.Zero, empty.Duration);
    }

    [Fact]
    public void ReviewSeparatesUnitsUsesCumulativeInvestmentAndExportsReadableSummaries()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatistics", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 15);
            var project = new FocusProject(Guid.NewGuid(), "实验", "学习", "#7B97A6"); var other = project with { Id = Guid.NewGuid(), Category = "工作" }; store.SaveProject(project); store.SaveProject(other);
            var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); store.SaveTodoFolder(folder);
            var task = new TodoItem(Guid.NewGuid(), folder.Id, "阅读,\"资料\"", TodoStatus.Completed, EstimatedMinutes: 60, PreferredProjectId: project.Id, ResumeNote: "整理笔记"); store.SaveTodo(task);
            var records = new[] { Session(project.Id, day.AddDays(-30), 9, 40, task.Id), Session(project.Id, day, 10, 20, task.Id), Session(project.Id, day, 14, 10, task.Id), Session(other.Id, day, 15, 15) };
            foreach (var item in records) store.SaveRecord(item);
            store.SaveOutcome(new(records[0].Id, "上月成果", 100, "页")); store.SaveOutcome(new(records[1].Id, "本次阅读", 5, "页", "整理笔记")); store.SaveOutcome(new(records[2].Id, "完成习题", 3, "题"));
            var model = new StatisticsViewModel(store) { Range = "日", Anchor = day.ToDateTime(TimeOnly.MinValue) };
            Assert.Equal(TimeSpan.FromMinutes(45), model.Snapshot.Duration); Assert.Equal(3, model.Snapshot.Count); Assert.Equal(2, model.Shares.Count);
            Assert.Contains("5 页", model.QuantitySummary); Assert.Contains("3 题", model.QuantitySummary); Assert.DoesNotContain("105", model.QuantitySummary);
            Assert.Equal(2, model.Outcomes.Count); Assert.Equal("1小时10分钟", Assert.Single(model.Tasks).Invested); Assert.Equal("比预估多 10分钟", model.Tasks[0].Difference); Assert.Equal("已完成", model.Tasks[0].Status);
            model.SelectedFolder = model.FolderChoices.Single(choice => choice.Folder == "学习"); Assert.Equal(TimeSpan.FromMinutes(30), model.Snapshot.Duration); Assert.DoesNotContain(model.ProjectChoices, choice => choice.Id == other.Id);
            model.SelectedProject = model.ProjectChoices.Single(choice => choice.Id == project.Id); model.SelectedTodo = model.TodoChoices.Single(choice => choice.Id == task.Id); Assert.Equal(2, model.Snapshot.Count);
            var summary = model.Export(false); Assert.Contains("本次阅读", summary); Assert.Contains("整理笔记", summary); Assert.DoesNotContain("上月成果", summary); Assert.Contains("1小时10分钟", summary);
            var csv = model.Export(true); Assert.Contains("阅读,\"\"资料\"\"", csv); Assert.Contains("1200", csv);
            model.ShareMode = "文件夹"; Assert.Equal("学习", Assert.Single(model.Shares).Name); model.ChartMetric = "次数";
            model.Range = "自定义"; model.CustomFrom = day.AddDays(1).ToDateTime(TimeOnly.MinValue); model.CustomTo = day.ToDateTime(TimeOnly.MinValue); Assert.False(model.IsValid); Assert.Empty(model.Snapshot.Sessions);
            model.TodayCommand.Execute(null); Assert.True(model.IsValid); Assert.Equal(DateTime.Today, model.CustomFrom); Assert.Equal(DateTime.Today, model.CustomTo); Assert.False(model.ShowAnchor);
            var reopened = new StatisticsViewModel(new SqliteFocusStore(Path.Combine(directory, "test.db"))); Assert.Equal("自定义", reopened.Range); Assert.Equal("次数", reopened.ChartMetric); Assert.Equal("文件夹", reopened.ShareMode);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
