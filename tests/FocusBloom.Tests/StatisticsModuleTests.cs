using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class StatisticsModuleTests
{
    private static FocusRecord Record(Guid project, DateOnly date, int? count, bool strict = true, Guid? todo = null)
    {
        var local = date.ToDateTime(new(9, 0)); var from = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        return new(Guid.NewGuid(), project, TimerMode.CountUp, from, from.AddMinutes(30), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, from, from.AddMinutes(30)) }, strict, todo, count);
    }
    [Fact]
    public void InterceptionsUseEndedStrictSessionsAndExcludeUncountedRecordsFromAverages()
    {
        var date = new DateOnly(2026, 10, 15); var project = Guid.NewGuid(); var other = Guid.NewGuid(); var todo = Guid.NewGuid();
        var first = Record(project, date, 4, todo: todo); var crossing = Record(project, date, 8, todo: todo);
        crossing = crossing with { EndedAt = crossing.EndedAt.AddDays(1) };
        var records = new[] { first, Record(project, date, 0, todo: todo), Record(project, date, null, todo: todo), Record(project, date, 99, false, todo), Record(other, date, 12), Record(project, date, 20), crossing };
        var data = FocusStatistics.Build(records, new(date, date, new HashSet<Guid> { project }, todo)); var summary = InterceptionStatistics.Build(data);
        Assert.Equal(3, summary.StrictSessions); Assert.Equal(2, summary.CountedSessions); Assert.Equal(1, summary.UnknownSessions); Assert.Equal(4, summary.Total); Assert.Equal(1, summary.AffectedSessions); Assert.Equal(2, summary.Average);
        var next = InterceptionStatistics.Build(FocusStatistics.Build(records, new(date.AddDays(1), date.AddDays(1)))); Assert.Equal(8, next.Total); Assert.Equal(1, next.CountedSessions);
        var unknown = InterceptionStatistics.Build(FocusStatistics.Build(new[] { Record(project, date, null) }, new(date, date))); Assert.Equal(0, unknown.CountedSessions); Assert.Equal(1, unknown.UnknownSessions); Assert.Equal(0, unknown.Average);
        Assert.Equal(new InterceptionStatistics(0, 0, 0, 0), InterceptionStatistics.Build(FocusStatistics.Build(Array.Empty<FocusRecord>(), new(date, date))));
    }
    [Fact]
    public void ZeroInterceptionRatioUsesKnownStrictSessionsAndHasNoValueWithoutData()
    {
        var day = new DateOnly(2026, 10, 15); var project = Guid.NewGuid(); var records = new[] { Record(project, day, 3), Record(project, day, 0), Record(project, day, null), Record(project, day, 0, false) };
        InterceptionStatistics Build(params FocusRecord[] items) => InterceptionStatistics.Build(FocusStatistics.Build(items, new(day, day)));
        var summary = Build(records); Assert.Equal(1, summary.ZeroSessions); Assert.Equal(.5, summary.ZeroRatio); Assert.Equal(1, summary.UnknownSessions);
        Assert.Equal(1, Build(records[1]).ZeroRatio); Assert.Equal(0, Build(records[0]).ZeroRatio); Assert.Null(Build(records[2]).ZeroRatio); Assert.Null(Build(records[3]).ZeroRatio); Assert.Null(Build().ZeroRatio);
    }
    [Fact]
    public void InterceptionFrequencyUsesMatchingFullFocusDurationAndExcludesUnknownSessions()
    {
        var day = new DateOnly(2026, 10, 15); var project = Guid.NewGuid(); var half = Record(project, day, 3);
        var longSession = half with { Id = Guid.NewGuid(), EndedAt = half.StartedAt.AddHours(2), Intervals = new[] { new FocusInterval(IntervalKind.Focus, half.StartedAt, half.StartedAt.AddHours(2)) } };
        InterceptionStatistics Build(params FocusRecord[] records) => InterceptionStatistics.Build(FocusStatistics.Build(records, new(day, day)));
        Assert.Equal(6, Build(half).PerHour); Assert.Equal(1.5, Build(longSession).PerHour);
        var paused = half with { Intervals = new[] { new FocusInterval(IntervalKind.Focus, half.StartedAt, half.StartedAt.AddMinutes(15)), new FocusInterval(IntervalKind.Pause, half.StartedAt.AddMinutes(15), half.EndedAt) } };
        var unknown = longSession with { Id = Guid.NewGuid(), InterceptionCount = null }; var normal = longSession with { Id = Guid.NewGuid(), Strict = false };
        var data = Build(paused, unknown, normal); Assert.Equal(TimeSpan.FromMinutes(15), data.CountedDuration); Assert.Equal(12, data.PerHour); Assert.Equal(1, data.CountedSessions);
        Assert.Null(Build(unknown, normal).PerHour); Assert.Equal(0, Build(half with { InterceptionCount = 0 }).PerHour);
        var crossing = longSession with { EndedAt = longSession.EndedAt.AddDays(1) };
        Assert.Null(Build(crossing).PerHour); Assert.Equal(1.5, InterceptionStatistics.Build(FocusStatistics.Build(new[] { crossing }, new(day.AddDays(1), day.AddDays(1)))).PerHour);
    }
    [Fact]
    public void InterceptionFrequencyComparisonUsesPreviousPeriodAndTheSameProjectAndTodo()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomInterceptionComparison", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var project = store.GetProjects()[0].Id; var todo = Guid.NewGuid(); var day = new DateOnly(2026, 10, 15);
            store.SaveTodoFolder(new(Guid.NewGuid(), "事项", "未分类")); var folder = store.GetTodoFolders()[0]; store.SaveTodo(new(todo, folder.Id, "阅读", PreferredProjectId: project));
            var otherTodo = Guid.NewGuid(); store.SaveTodo(new(otherTodo, folder.Id, "整理资料", PreferredProjectId: project));
            var otherProject = Guid.NewGuid(); store.SaveProject(new(otherProject, "其他项目", "工作", "#879F8C"));
            foreach (var record in new[] { Record(project, day, 3, todo: todo), Record(project, day, 0, todo: todo), Record(project, day, null, todo: todo), Record(project, day, 99, false, todo), Record(project, day.AddDays(-1), 6, todo: todo), Record(project, day.AddDays(-1), 0, todo: todo), Record(project, day, 50, todo: otherTodo), Record(otherProject, day, 60, todo: todo), Record(otherProject, day.AddDays(-1), 200, todo: todo) }) store.SaveRecord(record);
            var model = new StatisticsViewModel(store) { Range = "日", Anchor = day.ToDateTime(TimeOnly.MinValue) }; model.SelectedProject = model.ProjectChoices.Single(item => item.Id == project); model.SelectedTodo = model.TodoChoices.Single(item => item.Id == todo);
            Assert.Equal(3, model.Interceptions.PerHour); Assert.Equal(6, model.PreviousInterceptions.PerHour); Assert.Equal(-.5, model.InterceptionRateRelativeChange); Assert.Contains("降低", model.InterceptionComparisonText); Assert.Equal(day.AddDays(-1), model.PreviousInterceptionFrom); Assert.Equal(day.AddDays(-1), model.PreviousInterceptionTo);
            model.Range = "月"; Assert.Equal(new DateOnly(2026, 9, 1), model.PreviousInterceptionFrom); Assert.Equal(new DateOnly(2026, 9, 30), model.PreviousInterceptionTo);
            model.Range = "年"; Assert.Equal(new DateOnly(2025, 1, 1), model.PreviousInterceptionFrom); Assert.Equal(new DateOnly(2025, 12, 31), model.PreviousInterceptionTo);
            model.Range = "周"; Assert.Equal(new DateOnly(2026, 10, 5), model.PreviousInterceptionFrom); Assert.Equal(new DateOnly(2026, 10, 11), model.PreviousInterceptionTo);
            model.CustomFrom = day.ToDateTime(TimeOnly.MinValue); model.CustomTo = day.AddDays(2).ToDateTime(TimeOnly.MinValue); model.Range = "自定义"; Assert.Equal(day.AddDays(-3), model.PreviousInterceptionFrom); Assert.Equal(day.AddDays(-1), model.PreviousInterceptionTo);
            store.SaveRecord(Record(project, day.AddDays(-3), 0, todo: todo)); model.Reload(); model.Range = "日"; model.Anchor = day.AddDays(-2).ToDateTime(TimeOnly.MinValue); Assert.Contains("暂无", model.InterceptionComparisonText);
            model.Anchor = day.AddDays(-1).ToDateTime(TimeOnly.MinValue); Assert.Contains("上一周期暂无", model.InterceptionComparisonText);
            store.SaveRecord(Record(project, day.AddDays(-2), 0, todo: todo)); model.Reload(); Assert.Contains("增加", model.InterceptionComparisonText); Assert.Null(model.InterceptionRateRelativeChange);
            model.Anchor = day.AddDays(-2).ToDateTime(TimeOnly.MinValue); Assert.Equal("与上一周期持平", model.InterceptionComparisonText);
            model.Anchor = day.ToDateTime(TimeOnly.MinValue); Assert.Contains("降低", model.InterceptionComparisonText); Assert.Contains("2026-10-14", model.InterceptionComparisonTooltip);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ModuleVisibilityOrderAndFilteredInterceptionsSurviveReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatsModules", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var project = store.GetProjects()[0].Id; var day = DateOnly.FromDateTime(DateTime.Today);
            store.SaveRecord(Record(project, day, 5)); store.SaveRecord(Record(project, day.AddDays(-1), 9));
            var model = new StatisticsViewModel(store) { Range = "日", Anchor = day.ToDateTime(TimeOnly.MinValue) }; Assert.Equal(5, model.Interceptions.Total);
            var focus = model.Modules.Single(item => item.Key == "concentration"); model.MoveModule(focus, OrderMove.First); model.Modules.Single(item => item.Key == "overview").Visible = false;
            var expected = model.Modules.Select(item => (item.Key, item.Visible)).ToArray(); var reopened = new StatisticsViewModel(store); Assert.Equal(expected, reopened.Modules.Select(item => (item.Key, item.Visible)).ToArray());
            reopened.Anchor = day.AddDays(-1).ToDateTime(TimeOnly.MinValue); Assert.Equal(9, reopened.Interceptions.Total); Assert.Equal(2, store.GetRecords().Count);
            foreach (var item in reopened.Modules) item.Visible = false; Assert.All(new StatisticsViewModel(store).Modules, item => Assert.False(item.Visible));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    [Fact]
    public void ModuleDialogReordersTogglesAndRendersConcentrationWithoutAffectingReview()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatsModulesUi", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var project = store.GetProjects()[0].Id; var today = DateOnly.FromDateTime(DateTime.Today);
                store.SaveRecord(Record(project, today, 3)); store.SaveRecord(Record(project, today, 0)); store.SaveRecord(Record(project, today, null)); store.SaveRecord(Record(project, today, 99, false));
                main = new(store); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 850 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "records").OpenCommand.Execute(null);
                ((Button)window.FindName("RecordStatisticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); main.Statistics.Range = "日"; Flush();
                var page = (StatisticsView)window.FindName("StatisticsPage"); var content = (StackPanel)page.FindName("StatisticsContent"); var configure = (Button)page.FindName("ConfigureStatisticsModulesButton");
                string[] Rendered() => Descendants<FrameworkElement>(content).Where(item => item.Tag is StatisticsModuleOption).Select(item => ((StatisticsModuleOption)item.Tag).Key).ToArray();
                Assert.Equal(main.Statistics.Modules.Select(item => item.Key).ToArray(), Rendered());
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<StatisticsModulesWindow>().Single(); Flush();
                    var list = (ItemsControl)dialog.FindName("StatisticsModuleList"); var toggle = Descendants<CheckBox>(list).Single(item => ((StatisticsModuleOption)item.DataContext).Key == "activity"); toggle.IsChecked = false;
                    var handle = Descendants<ReorderButton>(list).Single(item => ((StatisticsModuleOption)item.DataContext).Key == "concentration"); handle.RaiseEvent(new ReorderEventArgs(OrderMove.First, main.Statistics.Modules[0])); dialog.Close();
                })); configure.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.Equal("concentration", Rendered()[0]); Assert.DoesNotContain("activity", Rendered()); Assert.Equal(3, main.Statistics.Interceptions.Total); Assert.Equal(1.5, main.Statistics.Interceptions.Average);
                Assert.Contains(Descendants<TextBlock>(content), item => item.Text.Contains("1 次旧记录未统计"));
                Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "3 次/小时"); Assert.Contains(Descendants<TextBlock>(content), item => item.Text.Contains("样本 2 次") && item.Text.Contains("1小时0分钟")); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "上一周期暂无数据");
                var ring = Descendants<Button>(content).Single(item => Equals(item.Tag, "interception-ratio")); Assert.Contains(Descendants<TextBlock>(ring), item => item.Text == "50%"); Assert.Contains("零拦截 1", (string)ring.ToolTip);
                var compact = Descendants<FrameworkElement>(content).Single(item => item.Tag is StatisticsModuleOption option && option.Key == "concentration"); Assert.True(compact.ActualWidth > 200); Assert.True(compact.ActualWidth < content.ActualWidth * .6);
                foreach (var (tag, count) in new[] { ("interception-ratio", 3), ("zero-interceptions", 1), ("with-interceptions", 1) })
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var detail = Application.Current.Windows.OfType<StatisticsDetailsWindow>().Single(); Assert.Equal(count, Descendants<Button>(detail).Count(button => button.Tag is Guid)); detail.Close(); })); Descendants<Button>(content).Single(item => Equals(item.Tag, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                ((ScrollViewer)page.FindName("StatisticsViewport")).ScrollToTop(); Flush(); GoalsUiTests.Save(window, "statistics-modules-light.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(window, "statistics-modules-dark.png");
                window.MinWidth = 620; window.Width = 760; Flush();
                compact = Descendants<FrameworkElement>(content).Single(item => item.Tag is StatisticsModuleOption option && option.Key == "concentration"); Assert.True(compact.ActualWidth >= content.ActualWidth * .9); GoalsUiTests.Save(window, "statistics-concentration-narrow.png"); window.Width = 1240; Flush();
                foreach (var item in main.Statistics.Modules) item.Visible = false; Flush(); Assert.Empty(Rendered()); Assert.Contains(Descendants<TextBlock>(content), item => item.Text.Contains("尚未选择统计模块")); Assert.True(configure.IsVisible);
                main.Statistics.Modules.Single(item => item.Key == "concentration").Visible = true; Flush(); Assert.Equal(new[] { "concentration" }, Rendered());
                var shareModule = main.Statistics.Modules.Single(item => item.Key == "shares"); shareModule.Visible = true; main.Statistics.MoveModule(shareModule, OrderMove.Next, main.Statistics.Modules[1]); Flush();
                var concentration = Descendants<FrameworkElement>(content).Single(item => item.Tag is StatisticsModuleOption option && option.Key == "concentration"); var shares = Descendants<FrameworkElement>(content).Single(item => item.Tag is StatisticsModuleOption option && option.Key == "shares"); Assert.Same(concentration.Parent, shares.Parent);
                shareModule.Visible = false; main.Statistics.CustomFrom = today.AddYears(-10).ToDateTime(TimeOnly.MinValue); main.Statistics.CustomTo = main.Statistics.CustomFrom; main.Statistics.Range = "自定义"; Flush(); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "暂无数据"); main.Statistics.Range = "日"; Flush();
                store.SaveRecord(Record(project, today.AddDays(-3), 0)); store.SaveRecord(Record(project, today.AddDays(-4), 2)); main.Statistics.Reload();
                main.Statistics.Anchor = today.AddDays(-3).ToDateTime(TimeOnly.MinValue); Flush(); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "100%"); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "0 次/小时"); Assert.Contains(Descendants<TextBlock>(content), item => item.Text.Contains("降低"));
                main.Statistics.Anchor = today.AddDays(-4).ToDateTime(TimeOnly.MinValue); Flush(); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "0%"); main.Statistics.Anchor = today.ToDateTime(TimeOnly.MinValue);
                ((Button)window.FindName("RecordReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.False(configure.IsVisible); Assert.Contains(Descendants<TextBlock>(content), item => item.Text == "待办与累计投入");
                ((Button)window.FindName("RecordStatisticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Equal(new[] { "concentration" }, Rendered());
                Assert.Equal(main.Statistics.Modules.Select(item => (item.Key, item.Visible)), new StatisticsViewModel(store).Modules.Select(item => (item.Key, item.Visible))); Assert.Equal(6, store.GetRecords().Count); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
