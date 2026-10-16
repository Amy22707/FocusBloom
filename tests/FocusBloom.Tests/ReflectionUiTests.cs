using System.IO;
using System.Windows;
using System.Windows.Controls;
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
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class ReflectionUiTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(DateTime.Today.AddHours(14), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today.AddHours(14)));
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
    [Fact]
    public void MoodCalendarBackfillEditingDeletionSummaryRemindersAndLayoutWorkWithoutInterruptingFocus()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomReflectionUi", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var project = store.GetProjects()[0].Id;
                var local = today.ToDateTime(new(9, 0)); var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
                var record = new FocusRecord(Guid.NewGuid(), project, TimerMode.CountUp, start, start.AddHours(1), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, start.AddMinutes(20)), new FocusInterval(IntervalKind.Pause, start.AddMinutes(20), start.AddMinutes(40)), new FocusInterval(IntervalKind.Focus, start.AddMinutes(40), start.AddHours(1)) }); store.SaveRecord(record); store.SaveOutcome(new(record.Id, "读完资料", 3, "页", "整理笔记"));
                var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); store.SaveTodoFolder(folder); store.SaveTodo(new(Guid.NewGuid(), folder.Id, "完成实验", TodoStatus.Completed, CompletedAt: start.AddHours(2)));
                var habit = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-7)); store.SaveHabit(habit);
                store.SaveDailyReflection(new(today.AddDays(-3), 1, new[] { "休息" }, "有些累")); store.SaveDailyReflection(new(today.AddDays(-1), 4, new[] { "学习" }, "整理好了资料"));
                var clock = new Clock(); main = new(store, new FocusTimer(clock)); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light); main.SoftwareRemindersEnabled = false;
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 860 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "reflection").OpenCommand.Execute(null); Flush();
                var tabs = (TabControl)window.FindName("MainTabs"); Assert.Same(window.FindName("ReflectionTab"), tabs.SelectedItem); Assert.Equal("settings", main.Navigation.Items[^1].Key); Assert.Equal(8, tabs.Items.IndexOf(window.FindName("BrowserTab")));
                var page = (ReflectionView)window.FindName("ReflectionPage"); Assert.True(page.IsVisible); Assert.Equal(TimeSpan.FromMinutes(40), main.Reflection.Summary.Duration); Assert.Single(main.Reflection.Summary.CompletedTodos); Assert.Single(main.Reflection.Summary.Outcomes);
                var calendar = (StackPanel)page.FindName("MoodCalendar"); Button Day(DateOnly date) => Descendants<Button>(calendar).Single(item => item.Tag is DateOnly value && value == date);
                Assert.False(Day(today.AddDays(1)).IsEnabled); Assert.True(Day(today).IsEnabled);
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); Assert.True(main.IsFocusActive);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(2, store.GetDailyReflections().Count);
                    ((Button)editor.FindName("MoodChoice4")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ((TextBox)editor.FindName("TagsInput")).Text = "学习，运动，学习"; ((TextBox)editor.FindName("FeelingInput")).Text = "今天状态不错"; ((TextBox)editor.FindName("HighlightsInput")).Text = "完成实验报告"; ((TextBox)editor.FindName("NextStepInput")).Text = "明天检查结果";
                    Flush(); GoalsUiTests.Save(editor, "reflection-editor-light.png");
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                })); ((Button)page.FindName("EditReflectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.Equal(3, store.GetDailyReflections().Count); Assert.Equal(2, main.Reflection.SelectedEntry!.Tags.Length); Assert.Equal("完成实验报告", main.Reflection.Highlights); Assert.Equal(today, main.Reflection.Day); Assert.True(main.IsFocusActive);
                main.Goals.CheckIn(habit.Id, today); Assert.Equal(1, main.Reflection.Summary.CheckedHabits); ((Expander)page.FindName("ReflectionSummaryExpander")).IsExpanded = true; Flush();
                Assert.Contains(Descendants<TextBlock>(page), item => item.Text.Contains("读完资料")); GoalsUiTests.Save(window, "reflection-month-light.png");
                ((ScrollViewer)page.FindName("ReflectionViewport")).ScrollToBottom(); Flush(); GoalsUiTests.Save(window, "reflection-trend-light.png"); ((ScrollViewer)page.FindName("ReflectionViewport")).ScrollToTop();
                Day(today.AddDays(-1)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); var viewport = (ScrollViewer)page.FindName("ReflectionViewport"); viewport.ScrollToVerticalOffset(60); Flush(); var offset = viewport.VerticalOffset;
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); ((Button)editor.FindName("MoodChoice5")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ((TextBox)editor.FindName("FeelingInput")).Text = "补充了昨天的记录"; ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); })); ((Button)page.FindName("EditReflectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.Equal(today.AddDays(-1), main.Reflection.Day); Assert.Equal(3, store.GetDailyReflections().Count); Assert.Equal(5, main.Reflection.SelectedEntry!.Mood); Assert.InRange(viewport.VerticalOffset, Math.Max(0, offset - 1), offset + 1);
                var picker = (DatePicker)page.FindName("ReflectionDatePicker"); picker.SelectedDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue); Assert.False(((Button)page.FindName("EditReflectionButton")).IsEnabled); Assert.False(main.Reflection.CanEdit);
                picker.SelectedDate = today.AddYears(-10).ToDateTime(TimeOnly.MinValue); Assert.True(DatePickerNavigation.GetEnable(picker));
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); ((TextBox)editor.FindName("HighlightsInput")).Text = "十年前的今天"; ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); })); ((Button)page.FindName("EditReflectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.Null(main.Reflection.SelectedEntry!.Mood); Assert.Equal(today.AddYears(-10), main.Reflection.Day); Assert.Equal(4, store.GetDailyReflections().Count);
                var changes = 0; main.CalendarDataChanged += (_, _) => changes++;
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var editor = Application.Current.Windows.OfType<ReflectionReminderWindow>().Single(); Assert.Contains(Descendants<TextBlock>(editor), item => item.Text.Contains("总开关已关闭")); ((CheckBox)editor.FindName("ReminderEnabledInput")).IsChecked = true; ((TextBox)editor.FindName("ReminderTimeInput")).Text = "21:15"; ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                })); ((Button)page.FindName("ReflectionReminderButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(main.Reflection.Reminder.Enabled); Assert.Equal(new TimeOnly(21, 15), main.Reflection.Reminder.Time); Assert.True(changes > 0); Assert.False(main.SoftwareRemindersEnabled);
                main.Reflection.TodayCommand.Execute(null); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); viewport.ScrollToTop(); Flush(); GoalsUiTests.Save(window, "reflection-month-dark.png");
                var scale = main.CardSizing.Options.Single(item => item.Name == "心情月历"); var before = Day(today).Height; scale.Percentage = 140; Flush(); Assert.True(Day(today).Height > before); scale.Percentage = 100;
                window.MinWidth = 620; window.Width = 760; Flush(); Assert.Equal(1, Grid.GetRow((Border)page.FindName("DayPanel"))); GoalsUiTests.Save(window, "reflection-narrow.png"); window.Width = 1240; Flush();
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); Assert.Equal(today.AddDays(-1), main.Reflection.Day); editor.Close(); })); window.OpenReminder(new("心情", ReflectionReminders.Id, "心情", today.AddDays(-1))); Assert.True(main.IsFocusActive);
                main.Reflection.TodayCommand.Execute(null);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single();
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().Close())); ((Button)editor.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(4, store.GetDailyReflections().Count);
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ((Button)Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))); ((Button)editor.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                })); ((Button)page.FindName("EditReflectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.Equal(3, store.GetDailyReflections().Count); Assert.Single(store.GetRecords()); Assert.Single(store.GetTodos()); Assert.Single(store.GetHabitCheckIns()); Assert.True(main.IsFocusActive);
                clock.Advance(TimeSpan.FromSeconds(12)); main.EndCommand.Execute(null); Flush(); Assert.Equal(2, store.GetRecords().Count); Assert.Equal(TimeSpan.FromMinutes(40) + TimeSpan.FromSeconds(12), main.Reflection.Summary.Duration);
                var reopened = new ReflectionViewModel(store); Assert.True(reopened.SummaryExpanded); Assert.True(reopened.Reminder.Enabled); Assert.Equal(3, reopened.Entries.Count); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(item => item != window).ToArray()) dialog.Close(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
