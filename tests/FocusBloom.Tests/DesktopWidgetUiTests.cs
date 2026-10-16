using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class DesktopWidgetUiTests
{
    private sealed class Clock : TimeProvider { private DateTimeOffset _now = new(DateTime.Today.AddHours(14), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(14))); public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan value) => _now += value; }
    [Fact]
    public void AllWidgetsSwitchViewsEditSharedDataPersistAndSurviveTrayWithoutLeavingWindowsOnExit()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomWidgetsUi", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var today = DateOnly.FromDateTime(DateTime.Today); var project = store.GetProjects()[0].Id;
                var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); var other = new TodoFolder(Guid.NewGuid(), "生活", "未分类"); store.SaveTodoFolder(folder); store.SaveTodoFolder(other);
                var task = new TodoItem(Guid.NewGuid(), other.Id, "测试桌面组件", DueDate: today.AddDays(2), Important: true, Urgent: true); store.SaveTodo(task);
                foreach (var (important, urgent) in new[] { (false, true), (false, false), (true, false) }) store.SaveTodo(new(Guid.NewGuid(), folder.Id, $"计划 {important}-{urgent}", Important: important, Urgent: urgent));
                var start = new DateTimeOffset(DateTime.Today.AddHours(9), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(9))); store.SaveRecord(new(Guid.NewGuid(), project, TimerMode.CountUp, start, start.AddMinutes(20), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, start.AddMinutes(20)) }));
                var exam = new DateEvent(Guid.NewGuid(), "考试", today.AddDays(10)); var anniversary = new DateEvent(Guid.NewGuid(), "开始学习", today.AddDays(-20), Mode: DateCountMode.CountUp); store.SaveDateEvent(exam); store.SaveDateEvent(anniversary);
                var habit = new Habit(Guid.NewGuid(), "每天阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-7)); store.SaveHabit(habit); store.SaveDailyReflection(new(today.AddDays(-1), 4, new[] { "学习" }));
                var clock = new Clock(); main = new(store, new FocusTimer(clock)); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light); window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Flush();
                Assert.Empty(window.DesktopWidgets.Windows); main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); ((Expander)window.FindName("FloatSettingsSection")).IsExpanded = true; Flush();
                var settings = (UserControl)window.FindName("DesktopWidgetSettings"); var controls = (ItemsControl)settings.FindName("DesktopWidgetOptions"); Assert.Equal(6, Descendants<CheckBox>(controls).Count()); foreach (var toggle in Descendants<CheckBox>(controls).ToArray()) toggle.IsChecked = true;
                Flush(); Assert.Equal(6, window.DesktopWidgets.Windows.Count); Assert.All(window.DesktopWidgets.Windows.Values, item => { Assert.Null(item.Owner); Assert.True(item.IsVisible); Assert.Equal(Visibility.Visible, item.Visibility); });
                var focus = window.DesktopWidgets.Windows[DesktopWidgetKind.Focus]; main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(12)); main.Tick(); window.DesktopWidgets.Tick(); Flush();
                Assert.Contains(Descendants<TextBlock>(focus), item => item.Text == "00:20:12"); main.PauseCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(20)); main.Tick(); window.DesktopWidgets.Tick(); Assert.Contains(Descendants<TextBlock>(focus), item => item.Text == "00:20:12");
                main.ResumeCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(7)); main.EndCommand.Execute(null); Flush(); window.DesktopWidgets.RefreshNow(); Assert.Contains(Descendants<TextBlock>(focus), item => item.Text == "00:20:19"); Assert.Equal(2, store.GetRecords().Count);
                foreach (var view in main.Widgets[DesktopWidgetKind.Focus].Views) { main.Widgets[DesktopWidgetKind.Focus].View = view; Flush(); Assert.NotEmpty(((StackPanel)focus.FindName("WidgetBody")).Children); }
                var todos = window.DesktopWidgets.Windows[DesktopWidgetKind.Todos]; foreach (var view in main.Widgets[DesktopWidgetKind.Todos].Views) { main.Widgets[DesktopWidgetKind.Todos].View = view; Flush(); Assert.NotEmpty(Descendants<CheckBox>(todos)); GoalsUiTests.Save(todos, "widget-todos-" + view + ".png"); }
                main.Todos.SelectedFolder = main.Todos.Folders.Single(item => item.Id == folder.Id); var selectedFolder = main.Todos.SelectedFolder!.Id; var todoOption = main.Widgets[DesktopWidgetKind.Todos]; todoOption.FolderId = other.Id; todoOption.View = "紧凑列表"; Flush();
                var check = Descendants<CheckBox>(todos).Single(item => Equals(item.Tag, task.Id)); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Flush(); Assert.Equal(TodoStatus.Completed, store.GetTodos().Single(item => item.Id == task.Id).Status); Assert.Equal(selectedFolder, main.Todos.SelectedFolder!.Id); Assert.Empty(Descendants<CheckBox>(todos));
                todoOption.IncludeCompleted = true; Flush(); Assert.Single(Descendants<CheckBox>(todos));
                var dates = window.DesktopWidgets.Windows[DesktopWidgetKind.Dates]; var dateOption = main.Widgets[DesktopWidgetKind.Dates]; foreach (var view in dateOption.Views) { dateOption.View = view; Flush(); GoalsUiTests.Save(dates, "widget-dates-" + view + ".png"); }
                dateOption.AllItems = false; dateOption.SelectItem(exam.Id, true); dateOption.View = "列表"; Flush(); Assert.Contains(Descendants<Button>(dates), item => Equals(item.Tag, exam.Id)); Assert.DoesNotContain(Descendants<Button>(dates), item => Equals(item.Tag, anniversary.Id));
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<DateEventEditorWindow>().Single(); ((ComboBox)editor.FindName("ProjectSelector")).SelectedIndex = 1; Descendants<Button>(editor).Single(item => Equals(item.Content, "进入关联计时项目")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
                Descendants<Button>(dates).Single(item => Equals(item.Tag, exam.Id)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Same(window.FindName("FocusTab"), ((TabControl)window.FindName("MainTabs")).SelectedItem);
                var habits = window.DesktopWidgets.Windows[DesktopWidgetKind.Habits]; var habitCheck = Descendants<Button>(habits).Single(item => Equals(item.Tag, habit.Id) && Equals(item.Content, "今天打卡")); habitCheck.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Contains(store.GetHabitCheckIns(), item => item.HabitId == habit.Id && item.Date == today);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ((Button)Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))); Descendants<Button>(habits).Single(item => Equals(item.Tag, habit.Id) && Equals(item.Content, "已打卡 ✓")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Empty(store.GetHabitCheckIns());
                foreach (var view in main.Widgets[DesktopWidgetKind.Habits].Views) { main.Widgets[DesktopWidgetKind.Habits].View = view; Flush(); GoalsUiTests.Save(habits, "widget-habits-" + view + ".png"); }
                var mood = window.DesktopWidgets.Windows[DesktopWidgetKind.Mood]; var day = Descendants<Button>(mood).Single(item => item.Tag is DateOnly date && date == today); Assert.False(Descendants<Button>(mood).Single(item => item.Tag is DateOnly date && date == today.AddDays(1)).IsEnabled);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); ((Button)editor.FindName("MoodChoice4")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); })); day.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Contains(store.GetDailyReflections(), item => item.Date == today);
                foreach (var view in main.Widgets[DesktopWidgetKind.Mood].Views) { main.Widgets[DesktopWidgetKind.Mood].View = view; Flush(); GoalsUiTests.Save(mood, "widget-mood-" + view + ".png"); }
                var options = new DesktopWidgetSettingsWindow(main, dateOption) { Owner = dates }; options.Show(); Flush(); ((Slider)options.FindName("WidgetTransparencySlider")).Value = 25; ((CheckBox)options.FindName("WidgetTopmostInput")).IsChecked = true; Flush(); Assert.Equal(.75, dates.Opacity); Assert.True(dates.Topmost); options.Close();
                dates.Left = 40; dates.Top = 50; dates.Width = 480; dates.Height = 420; dates.SaveLayout(); Flush(); Assert.Equal(480, dateOption.Width);
                window.Hide(); Flush(); Assert.All(window.DesktopWidgets.Windows.Values, item => Assert.True(item.IsVisible)); window.Show();
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); foreach (var item in window.DesktopWidgets.Windows.Values) GoalsUiTests.Save(item, "widget-" + item.Option.Kind + "-dark.png");
                ((Button)focus.FindName("OpenModuleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.True(window.IsVisible); Assert.Equal("统计", main.RecordPage);
                todoOption.Enabled = false; Flush(); Assert.Equal(5, window.DesktopWidgets.Windows.Count); Assert.DoesNotContain(Application.Current.Windows.OfType<DesktopWidgetWindow>(), item => item.Option.Kind == DesktopWidgetKind.Todos); todoOption.Enabled = true; Flush();
                window.DisposeBrowser(); window.Close(); main.Media.Dispose(); Assert.Empty(Application.Current.Windows.OfType<DesktopWidgetWindow>()); Assert.All(new DesktopWidgetsViewModel(store).Items, item => Assert.True(item.Enabled));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser-reopened")); window.Show(); Flush(); Assert.Equal(6, window.DesktopWidgets.Windows.Count); var restored = window.DesktopWidgets.Windows[DesktopWidgetKind.Dates]; Assert.Equal(480, restored.Width); Assert.Equal(.75, restored.Opacity); Assert.True(restored.Topmost);
                window.CloseDesktopWidgetsForExit(); Assert.Empty(Application.Current.Windows.OfType<DesktopWidgetWindow>()); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<DesktopWidgetSettingsWindow>().ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
