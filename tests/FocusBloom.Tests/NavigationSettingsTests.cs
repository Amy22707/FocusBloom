using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class NavigationSettingsTests
{
    [Fact]
    public void NavigationVisibilityDragOrderAndSettingsSearchPersistAndKeepTimerRunning()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomNavigationSettings", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var clock = new ManualClock(); main = new(store, new FocusTimer(clock));
                window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Flush(); var tabs = (TabControl)window.FindName("MainTabs");
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush(); Assert.Same(window.FindName("PreferencesTab"), tabs.SelectedItem);
                var groups = new[] { "NavigationSettingsSection", "AppearanceSettingsSection", "SizeSettingsSection", "TimerSettingsSection", "FloatSettingsSection", "FontSettingsSection", "MediaSettingsSection", "ReminderSettingsSection", "RestrictionSettingsSection" }.Select(name => (Expander)window.FindName(name)).ToArray();
                Assert.All(groups, group => Assert.False(group.IsExpanded)); Save(window, "settings-folded-sections.png");
                groups[0].IsExpanded = true; Flush(); var settingsItems = (ItemsControl)window.FindName("NavigationSettingsItems");
                var record = main.Navigation.Items.Single(item => item.Key == "records");
                var recordCheck = Descendants<CheckBox>(settingsItems).Single(box => box.DataContext == record); recordCheck.IsChecked = false; Flush();
                var sidebar = (ItemsControl)tabs.Template.FindName("NavigationItems", tabs); Assert.Equal(7, sidebar.Items.Count);
                Assert.False(Descendants<CheckBox>(settingsItems).Single(box => box.DataContext is NavigationOption item && item.Key == "settings").IsEnabled);
                var handle = Descendants<ReorderButton>(settingsItems).Single(button => button.DataContext == record);
                handle.RaiseEvent(new ReorderEventArgs(OrderMove.First)); Flush(); Assert.Same(record, main.Navigation.Items[0]); recordCheck.IsChecked = true; Flush(); Assert.Same(record, sidebar.Items[0]);
                record.OpenCommand.Execute(null); Flush(); Assert.Same(window.FindName("RecordsTab"), tabs.SelectedItem); Assert.True(record.Active); Assert.Equal(5, tabs.SelectedIndex);
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null);
                main.Navigation.Items.Single(item => item.Key == "focus").Visible = false; Assert.True(main.IsFocusActive);
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush();
                var search = (TextBox)window.FindName("SettingsSearchInput"); search.Text = "字体"; Flush();
                Assert.True(groups[5].IsExpanded); Assert.Equal(Visibility.Visible, groups[5].Visibility); Assert.All(groups.Where(group => group != groups[5]), group => Assert.Equal(Visibility.Collapsed, group.Visibility));
                Assert.Null(store.GetSetting("ui.settings.expanded.fonts")); Save(window, "settings-search-fonts.png");
                search.Text = ""; Flush(); Assert.False(groups[5].IsExpanded); Assert.True(groups[0].IsExpanded);
                groups[5].IsExpanded = true; groups[2].IsExpanded = true; main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); Save(window, "settings-navigation-dark.png");
                clock.Advance(TimeSpan.FromSeconds(12)); main.EndCommand.Execute(null); Assert.Single(store.GetRecords());
                window.DisposeBrowser(); window.Close(); main.Media.Dispose(); main = new(new SqliteFocusStore(path)); window = new(main, store, Path.Combine(directory, "browser-reopened")); window.Show(); Flush();
                Assert.Equal("records", main.Navigation.Items[0].Key); Assert.False(main.Navigation.Items.Single(item => item.Key == "focus").Visible); Assert.True(main.Navigation.Items.Single(item => item.Key == "settings").Visible);
                Assert.True(((Expander)window.FindName("FontSettingsSection")).IsExpanded); Assert.True(((Expander)window.FindName("SizeSettingsSection")).IsExpanded); Assert.False(((Expander)window.FindName("TimerSettingsSection")).IsExpanded);
                main.Navigation.Items.Single(item => item.Key == "focus").Visible = true; main.Navigation.Move(main.Navigation.Items.Single(item => item.Key == "settings"), OrderMove.First);
                var reopened = new NavigationViewModel(new SqliteFocusStore(path)); Assert.Equal("settings", reopened.Items[0].Key); Assert.Equal(8, reopened.VisibleItems.Count);
            }
            finally { window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    [Fact]
    [Trait("DesktopMode", "Interactive")]
    public void HoveringBarsAndHeatmapShowsExactTotalWithoutClicking()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatisticsHover", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null; GetCursorPos(out var original);
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var date = DateOnly.FromDateTime(DateTime.Today); var local = date.ToDateTime(new(10, 0)); var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); var end = start.AddMinutes(3).AddSeconds(22);
                store.SaveRecord(new(Guid.NewGuid(), store.GetProjects()[0].Id, TimerMode.CountUp, start, end, EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, end) }));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")) { Topmost = true }; window.Show(); window.Activate(); Flush();
                ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("RecordsTab"); main.RecordPage = "统计"; main.Statistics.Range = "日"; Flush(); window.UpdateLayout();
                var page = (StatisticsView)window.FindName("StatisticsPage"); page.Render(); Flush();
                var bar = Descendants<Button>(page).First(button => button.Tag is StatisticsBucket bucket && bucket.Duration > TimeSpan.Zero); Hover(bar); ValidateTip(bar);
                var heat = Descendants<Button>(page).Single(button => button.Tag is DateOnly day && day == date); heat.BringIntoView(); Flush(); Hover(heat); ValidateTip(heat);
                Save(window, "statistics-hover-heatmap.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); page.Render(); Flush();
                heat = Descendants<Button>(page).Single(button => button.Tag is DateOnly day && day == date); heat.BringIntoView(); Flush(); Hover(heat); ValidateTip(heat); Save(window, "statistics-hover-dark.png");
            }
            finally { SetCursorPos(original.X, original.Y); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static void Hover(Button target)
    {
        var tooltip = Assert.IsType<ToolTip>(target.ToolTip); tooltip.IsOpen = false;
        var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2)); SetCursorPos((int)point.X, (int)point.Y);
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var polls = 0;
        timer.Tick += (_, _) => { if (tooltip.IsOpen || ++polls >= 70) { timer.Stop(); frame.Continue = false; } }; timer.Start(); Dispatcher.PushFrame(frame); Assert.True(tooltip.IsOpen, "鼠标悬停应自动显示统计提示。");
    }
    private static void ValidateTip(Button button)
    { var tip = Assert.IsType<ToolTip>(button.ToolTip); Assert.Contains(Descendants<TextBlock>(tip), label => label.Text == "总专注 00:03:22"); tip.IsOpen = false; }
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
