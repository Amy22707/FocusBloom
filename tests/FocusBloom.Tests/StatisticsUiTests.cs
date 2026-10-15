using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class StatisticsUiTests
{
    [Fact]
    public void StatisticsChartsDrillDownReviewExportAndFiltersWorkWithoutChangingTimer()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatisticsUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 15);
                var projects = new[] { new FocusProject(Guid.NewGuid(), "示例项目", "学习", "#7B97A6"), new FocusProject(Guid.NewGuid(), "英语阅读", "学习", "#879F8C"), new FocusProject(Guid.NewGuid(), "整理资料", "工作", "#AB87A0") };
                foreach (var project in projects) store.SaveProject(project);
                var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); store.SaveTodoFolder(folder);
                var task = new TodoItem(Guid.NewGuid(), folder.Id, "ICS实验", TodoStatus.InProgress, EstimatedMinutes: 120, PreferredProjectId: projects[0].Id, ResumeNote: "整理实验结论"); store.SaveTodo(task);
                Guid outcomeId = Guid.Empty;
                for (var index = 0; index < 18; index++)
                {
                    var date = day.AddDays(-index); var local = date.ToDateTime(new(9 + index % 5, 0)); var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); var end = start.AddMinutes(20 + index * 3);
                    var record = new FocusRecord(Guid.NewGuid(), projects[index % 3].Id, TimerMode.CountUp, start, end, EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, end) }, Strict: index % 2 == 0, TodoId: index % 3 == 0 ? task.Id : null, InterceptionCount: index % 4);
                    store.SaveRecord(record); if (index == 0) outcomeId = record.Id;
                    if (index % 3 == 0) store.SaveOutcome(new(record.Id, "完成一部分实验", 2, "题", "整理实验结论"));
                }
                var clock = new ManualClock(); main = new(store, new FocusTimer(clock)); main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Light);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 820 }; window.Show(); Flush();
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("RecordsTab");
                ((Button)window.FindName("RecordStatisticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var page = (StatisticsView)window.FindName("StatisticsPage"); main.Statistics.Anchor = day.ToDateTime(TimeOnly.MinValue); Flush(); window.UpdateLayout(); page.Render(); Flush();
                Assert.True(page.IsVisible); Assert.False(((CalendarView)window.FindName("FocusCalendarPage")).IsVisible);
                foreach (var range in new[] { "日", "周", "月", "年" })
                {
                    ((ComboBox)page.FindName("StatisticsRangeSelector")).SelectedItem = range; Flush(); page.Render(); Flush();
                    Assert.Equal(range, main.Statistics.Range); Assert.Contains(Descendants<Button>(page), button => button.Tag is StatisticsBucket);
                    Save(window, "statistics-" + range + "-light.png");
                }
                var viewport = (ScrollViewer)page.FindName("StatisticsViewport"); viewport.ScrollToBottom(); Flush(); Save(window, "statistics-annual-heatmap.png");
                Assert.Equal(365, Descendants<Button>(page).Count(button => button.Tag is DateOnly));
                main.Statistics.Range = "月"; viewport.ScrollToTop(); Flush(); page.Render();
                var bar = Descendants<Button>(page).First(button => button.Tag is StatisticsBucket bucket && bucket.Duration > TimeSpan.Zero);
                CloseDrillOnIdle(); bar.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.DoesNotContain(Application.Current.Windows.OfType<Window>(), item => item is StatisticsDetailsWindow);
                var share = Descendants<Button>(page).First(button => button.Tag is StatisticsShare); CloseDrillOnIdle(); share.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var heat = Descendants<Button>(page).Single(button => button.Tag is DateOnly date && date == day); CloseDrillOnIdle(); heat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                ((ComboBox)page.FindName("StatisticsFolderSelector")).SelectedItem = main.Statistics.FolderChoices.Single(choice => choice.Folder == "学习"); Flush();
                Assert.DoesNotContain(main.Statistics.Snapshot.Projects, item => item.ProjectId == projects[2].Id);
                ((ComboBox)page.FindName("StatisticsProjectSelector")).SelectedItem = main.Statistics.ProjectChoices.Single(choice => choice.Id == projects[0].Id); Flush();
                ((ComboBox)page.FindName("StatisticsTodoSelector")).SelectedItem = main.Statistics.TodoChoices.Single(choice => choice.Id == task.Id); Flush(); Assert.All(main.Statistics.Snapshot.Sessions, session => Assert.Equal(task.Id, session.Record.TodoId));
                ((Button)window.FindName("RecordReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); page.Render(); Save(window, "statistics-review-light.png");
                Assert.True(main.Statistics.ReviewMode); Assert.Contains(Descendants<TextBlock>(page), text => text.Text.Contains("预估")); Assert.Single(main.Statistics.Tasks); Assert.NotEmpty(main.Statistics.Outcomes);
                var saved = store.GetRecords().Count; main.SaveOutcome(new(outcomeId, "补记实验结果", 5, "题", "下一次检查边界")); Flush();
                Assert.Contains(main.Statistics.Outcomes, item => item.Text.Contains("补记实验结果")); Assert.Equal(saved, store.GetRecords().Count);
                var summaryPath = Path.Combine(directory, "summary.md"); File.WriteAllText(summaryPath, main.Statistics.Export(false)); Assert.Contains("补记实验结果", File.ReadAllText(summaryPath)); Assert.Contains("ICS实验", File.ReadAllText(summaryPath));
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); Save(window, "statistics-review-dark.png");
                ((Button)window.FindName("RecordStatisticsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); page.Render(); viewport.ScrollToTop(); Flush(); Save(window, "statistics-month-dark.png");
                var heatBackground = (SolidColorBrush)Descendants<Button>(page).First(button => button.Tag is DateOnly date && date == new DateOnly(2026, 1, 1)).Background;
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("SubtleBrush")).Color, heatBackground.Color);
                main.Statistics.Range = "自定义"; main.Statistics.CustomFrom = day.AddDays(1).ToDateTime(TimeOnly.MinValue); main.Statistics.CustomTo = day.ToDateTime(TimeOnly.MinValue); Flush(); Assert.False(((Button)page.FindName("ExportStatisticsButton")).IsEnabled);
                main.Statistics.CustomFrom = new DateTime(2016, 1, 1); main.Statistics.CustomTo = new DateTime(2016, 12, 31); Flush(); page.Render(); Assert.Empty(main.Statistics.Snapshot.Sessions); Save(window, "statistics-empty-custom.png");
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); Assert.True(main.IsFocusActive);
                var projectId = main.SelectedProject!.Id; clock.Advance(TimeSpan.FromSeconds(12)); main.Tick();
                main.Statistics.Range = "周"; main.Statistics.Move(-1); ((Button)window.FindName("RecordReviewButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.True(main.IsFocusActive); Assert.Equal(projectId, main.SelectedProject.Id);
                main.EndCommand.Execute(null); Flush(); Assert.Equal(saved + 1, store.GetRecords().Count);
                ((Button)window.FindName("RecordTimelineButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.True(((CalendarView)window.FindName("FocusCalendarPage")).IsVisible); Assert.False(page.IsVisible);
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close();
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }
    private static void CloseDrillOnIdle() => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var dialog = Application.Current.Windows.OfType<StatisticsDetailsWindow>().Single(); Assert.Contains(Descendants<Button>(dialog), button => button.Tag is Guid); dialog.Close(); }), DispatcherPriority.ApplicationIdle);
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(content.RenderSize); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(content), null, bounds); } bitmap.Render(visual);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
}
