using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class GoalsUiTests
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider { private DateTimeOffset _now = now; public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan value) => _now += value; }
    [Fact]
    public void CheckedHistoryDaysUseSolidThemeFillAndContrastingText()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCheckInFill", Guid.NewGuid().ToString()); MainViewModel? main = null; HabitHistoryWindow? history = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); main = new(store); var today = main.Goals.Today;
                var habit = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-1)); store.SaveHabit(habit); store.SetHabitCheckIn(habit.Id, today, true);
                history = new(main, habit); history.Show();
                foreach (var mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    main.SelectedAppearanceMode = main.AppearanceModes.Single(choice => choice.Mode == mode); Flush();
                    var calendar = (UniformGrid)history.FindName("HistoryCalendar"); var cards = calendar.Children.OfType<Border>().ToArray();
                    var filled = cards.Single(card => card.Tag is DateOnly date && date == today); var empty = cards.Single(card => card.Tag is DateOnly date && date == today.AddDays(-1));
                    var number = (TextBlock)((Button)filled.Child).Content;
                    Assert.Same(Application.Current.FindResource("AccentBrush"), filled.Background); Assert.Same(Application.Current.FindResource("PrimaryTextBrush"), number.Foreground);
                    Assert.Same(Application.Current.FindResource("SubtleBrush"), empty.Background); Assert.Equal(today.Day.ToString(), number.Text); Assert.Equal(HorizontalAlignment.Center, number.HorizontalAlignment); Assert.NotEqual(((SolidColorBrush)filled.Background).Color, ((SolidColorBrush)empty.Background).Color);
                    Save(history, $"habit-history-{mode.ToString().ToLowerInvariant()}.png");
                }
            }
            finally { history?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    [Fact]
    public void GoalsHabitsHistoryAndStatisticsWorkOnAnIsolatedDesktop()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomGoalsUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var local = today.ToDateTime(new(10, 0)); var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                store.SaveRecord(new(Guid.NewGuid(), store.GetProjects()[0].Id, TimerMode.CountUp, start, start.AddMinutes(30), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, start.AddMinutes(30)) }));
                var clock = new Clock(start); main = new(store, new FocusTimer(clock)); main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Light); main.SoftwareRemindersEnabled = false;
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 820 }; window.Show();
                main.Navigation.Items.Single(item => item.Key == "goals").OpenCommand.Execute(null); Flush(); var page = (GoalsView)window.FindName("GoalsPage"); Assert.True(page.IsVisible);
                var habitEditor = new HabitEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)habitEditor.FindName("NameInput")).Text = "每天阅读";
                    ((DatePicker)habitEditor.FindName("StartDateInput")).SelectedDate = today.AddDays(-3).ToDateTime(TimeOnly.MinValue);
                    ((CheckBox)habitEditor.FindName("ReminderToggle")).IsChecked = true; ((TextBox)habitEditor.FindName("ReminderTimeInput")).Text = "20:00";
                    ((Button)habitEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(habitEditor.ShowDialog()); var habit = Assert.Single(store.GetHabits()); Assert.True(habit.Reminder);
                store.SetHabitCheckIn(habit.Id, today.AddDays(-1), true); main.Goals.Reload();
                var goalEditor = new GoalEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)goalEditor.FindName("NameInput")).Text = "每天专注一小时"; ((TextBox)goalEditor.FindName("TargetInput")).Text = "0";
                    ((Button)goalEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Empty(store.GetGoals());
                    ((TextBox)goalEditor.FindName("TargetInput")).Text = "1"; ((ComboBox)goalEditor.FindName("ProjectSelector")).SelectedIndex = 1;
                    ((Button)goalEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(goalEditor.ShowDialog()); Assert.Equal(30, Assert.Single(main.Goals.Goals).Progress.Value); Assert.Equal(60, Assert.Single(store.GetGoals()).Target);
                var daysEditor = new GoalEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)daysEditor.FindName("NameInput")).Text = "阅读两天"; ((ComboBox)daysEditor.FindName("MetricSelector")).SelectedIndex = 2;
                    Assert.Equal(3, ((ComboBox)daysEditor.FindName("CycleSelector")).SelectedIndex); ((TextBox)daysEditor.FindName("TargetInput")).Text = "2";
                    ((DatePicker)daysEditor.FindName("StartDateInput")).SelectedDate = today.AddDays(-3).ToDateTime(TimeOnly.MinValue); ((ComboBox)daysEditor.FindName("HabitSelector")).SelectedIndex = 1;
                    ((Button)daysEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(daysEditor.ShowDialog()); Flush(); window.UpdateLayout();
                var check = Descendants<Button>(page).Single(button => button.Name == "HabitCheckButton"); Assert.True(check.IsEnabled); check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.True(Assert.Single(main.Goals.Habits).CheckedToday); Assert.True(main.Goals.Goals.Single(item => item.Progress.Goal.Metric == GoalMetric.CheckInDays).Progress.Complete);
                var history = new HabitHistoryWindow(main, habit) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var calendar = (UniformGrid)history.FindName("HistoryCalendar"); Assert.Equal(49, calendar.Children.Count);
                    var todayCell = calendar.Children.OfType<Border>().Single(card => card.Tag is DateOnly date && date == today); Assert.Equal(today.Day.ToString(), ((TextBlock)((Button)todayCell.Child).Content).Text);
                    ((DatePicker)history.FindName("MonthInput")).SelectedDate = today.AddMonths(-1).ToDateTime(TimeOnly.MinValue); Assert.Equal(49, calendar.Children.Count); history.Close();
                })); history.ShowDialog();
                main.Goals.SaveGoal(new(Guid.NewGuid(), "每天两次专注", GoalMetric.Sessions, 2, GoalCycle.Daily, today)); Flush(); Save(window, "goals-habits-light.png");
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); Assert.True(main.IsFocusActive); clock.Advance(TimeSpan.FromMinutes(30)); main.EndCommand.Execute(null); Flush();
                Assert.True(main.Goals.Goals.Single(item => item.Name == "每天专注一小时").Progress.Complete); Assert.True(main.Goals.Goals.Single(item => item.Progress.Goal.Metric == GoalMetric.Sessions).Progress.Complete);
                ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("RecordsTab"); main.RecordPage = "统计"; main.Statistics.Range = "日"; main.Statistics.Anchor = today.ToDateTime(TimeOnly.MinValue); Flush(); Assert.Equal(3, main.Statistics.GoalProgress.Count);
                Assert.Contains("每天专注一小时", main.Statistics.Export(false));
                main.Navigation.Items.Single(item => item.Key == "goals").OpenCommand.Execute(null); main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); Save(window, "goals-habits-dark.png");
                main.Goals.MoveGoal(main.Goals.Goals[2], OrderMove.First, null); main.Goals.Reload(); Assert.Equal("每天两次专注", main.Goals.Goals[0].Name);
                var delete = new HabitEditorWindow(main, habit) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var confirm = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single(); ((Button)confirm.FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
                    ((Button)delete.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                })); Assert.True(delete.ShowDialog()); Assert.Empty(store.GetHabits()); Assert.Equal(2, store.GetHabitCheckIns().Count); Assert.Equal(2, main.Goals.Goals.Single(item => item.Name == "阅读两天").Progress.Value);
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close();
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    internal static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(content.RenderSize); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(content), null, bounds); } bitmap.Render(visual);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
}
