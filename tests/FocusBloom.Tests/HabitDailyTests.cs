using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
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
public sealed class HabitDailyTests
{
    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.Now;
        public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
        public void NextDay() => _now = _now.AddDays(1);
    }
    [Fact]
    public void DailyBackfillUsesSelectedDateUpdatesStreaksAndGoalsAndRejectsFutureAndRestDays()
    {
        var root = Temporary();
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new Clock(); var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
            var habit = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-10)); store.SaveHabit(habit);
            var rest = new Habit(Guid.NewGuid(), "指定执行日", new[] { ((int)today.DayOfWeek + 6) % 7 + 1 }, today.AddDays(-10)); store.SaveHabit(rest);
            store.SetHabitCheckIn(habit.Id, today.AddDays(-3), true); store.SetHabitCheckIn(habit.Id, today.AddDays(-2), true);
            var goal = new FocusGoal(Guid.NewGuid(), "累计阅读三天", GoalMetric.CheckInDays, 3, GoalCycle.Total, habit.Start, HabitId: habit.Id); store.SaveGoal(goal);
            var model = new GoalsViewModel(store, _ => { }, clock); Assert.True(model.ShowDaily); model.PreviousCommand.Execute(null); Assert.Equal(today.AddDays(-1), model.Day); Assert.Equal("昨天", model.DayTitle);
            Assert.True(model.DayHabits.Single(row => row.Id == habit.Id).CanCheck); Assert.False(model.DayHabits.Single(row => row.Id == rest.Id).CanCheck);
            Assert.True(model.CheckIn(habit.Id, model.Day)); Assert.Equal(3, model.DayHabits.Single(row => row.Id == habit.Id).TotalDays); Assert.Equal(3, model.DayHabits.Single(row => row.Id == habit.Id).Streak); Assert.True(Assert.Single(model.DayGoals).Progress.Complete);
            Assert.False(model.CheckIn(rest.Id, model.Day)); model.NextCommand.Execute(null); Assert.Equal(today, model.Day); Assert.False(model.DayHabits.Single(row => row.Id == habit.Id).Checked);
            model.NextCommand.Execute(null); Assert.All(model.DayHabits, row => Assert.False(row.CanCheck)); Assert.False(model.CheckIn(habit.Id, model.Day)); Assert.Equal(3, store.GetHabitCheckIns().Count);
            var first = model.Habits[0]; var color = model.DayHabits.Single(row => row.Id == first.Id).Color; model.MoveHabit(first, OrderMove.Last, null); Assert.Equal(first.Id, model.DayHabits.Last().Id); Assert.Equal(color, model.DayHabits.Last().Color);
            model.TodayCommand.Execute(null); clock.NextDay(); model.Reload(); Assert.Equal(today.AddDays(1), model.Day); model.PreviousCommand.Execute(null); clock.NextDay(); model.Reload(); Assert.Equal(today, model.Day);
            model.View = "月"; Assert.True(model.ShowCompletion); Assert.NotEmpty(model.CompletionDays); model.View = "日"; Assert.True(new GoalsViewModel(store, _ => { }, clock).ShowDaily);
        }
        finally { Clean(root); }
    }
    [Fact]
    public void DayCardsBackfillWithOneClickConfirmUndoAndRemainUsableAcrossLayoutsAndThemes()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
                var names = new[] { "阅读十页", "整理笔记", "每天运动", "练习写作", "英语阅读", "复习课程" };
                for (var index = 0; index < names.Length; index++)
                {
                    var habit = new Habit(new Guid(index + 1, 0, 0, new byte[8]), names[index], Enumerable.Range(1, 7).ToArray(), today.AddDays(-15)); store.SaveHabit(habit);
                    store.SetHabitCheckIn(habit.Id, today.AddDays(-3), true); store.SetHabitCheckIn(habit.Id, today.AddDays(-2), true); if (index < 2) store.SetHabitCheckIn(habit.Id, today, true);
                }
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1240, Height = 850 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "goals").OpenCommand.Execute(null); Flush();
                var page = Children<GoalsView>(window).Single(); var list = (ItemsControl)page.FindName("DayHabitCards"); Assert.Equal("日", ((ComboBox)page.FindName("CompletionViewSelector")).SelectedItem); Assert.Equal(6, list.Items.Count); var dateText = Children<System.Windows.Controls.Primitives.DatePickerTextBox>((DatePicker)page.FindName("DailyHabitDate")).Single(); Assert.Equal(30, dateText.ActualHeight); Assert.Equal(2, dateText.Padding.Top);
                var id = main.Goals.DayHabits[0].Id; Button Check() => Children<Button>(list).Single(button => button.Name == "DayHabitCheckButton" && ((HabitDayRow)button.DataContext).Id == id);
                Assert.Equal(2, Children<System.Windows.Controls.Primitives.UniformGrid>(list).First().Columns); GoalsUiTests.Save(window, "habits-day-today-light.png");
                ((Button)page.FindName("DailyPreviousButton")).Command.Execute(null); Flush(); Assert.Equal(today.AddDays(-1), main.Goals.Day); Assert.False(((HabitDayRow)Check().DataContext).Checked);
                Check().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Contains(store.GetHabitCheckIns(), row => row.HabitId == id && row.Date == today.AddDays(-1)); Assert.Equal(3, ((HabitDayRow)Check().DataContext).Streak);
                var surface = (Border)Check().Template.FindName("Surface", Check()); var tint = (Border)Check().Template.FindName("Tint", Check()); Assert.Equal(30, surface.CornerRadius.TopLeft); Assert.Equal(.4, tint.Opacity);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().Close())); Check().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(((HabitDayRow)Check().DataContext).Checked);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ((Button)Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))); Check().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.False(((HabitDayRow)Check().DataContext).Checked);
                Check().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); GoalsUiTests.Save(window, "habits-day-backfill-light.png");
                foreach (var row in main.Goals.DayHabits.Where(row => !row.Checked).ToArray()) main.Goals.CheckIn(row.Id, row.Date); Flush(); Assert.Equal(1, main.Goals.DayRatio); Assert.Contains("都认真完成", main.Goals.DayEncouragement);
                var handle = Children<ReorderButton>(list).First(); var moved = ((HabitDayRow)handle.DataContext).Id; handle.RaiseEvent(new ReorderEventArgs(OrderMove.Last)); Flush(); Assert.Equal(moved, main.Goals.DayHabits.Last().Id);
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(window, "habits-day-complete-dark.png");
                window.Width = 900; window.Height = 560; Flush(); Assert.Equal(1, Children<System.Windows.Controls.Primitives.UniformGrid>(list).First().Columns); var viewport = (ScrollViewer)page.FindName("DailyViewport"); viewport.ScrollToBottom(); Flush(); Assert.Equal(viewport.ScrollableHeight, viewport.VerticalOffset); GoalsUiTests.Save(window, "habits-day-compact-dark.png");
                ((DatePicker)page.FindName("DailyHabitDate")).SelectedDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue); Flush(); Assert.All(Children<Button>(list).Where(button => button.Name == "DayHabitCheckButton"), button => Assert.False(button.IsEnabled)); Assert.True(new GoalsViewModel(store, _ => { }).ShowDaily);
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<ConfirmDeletionWindow>().ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "FocusBloomHabitDay", Guid.NewGuid().ToString());
    private static void Clean(string path) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
