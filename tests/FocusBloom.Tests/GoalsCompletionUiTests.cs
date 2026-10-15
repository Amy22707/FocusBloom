using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class GoalsCompletionUiTests
{
    [Fact]
    public void BackfillConfirmationCalendarScopesAndWeekMonthYearViewsWork()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCompletionUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null; HabitHistoryWindow? history = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var start = today.AddDays(-45);
                var reading = new Habit(Guid.NewGuid(), "阅读", Enumerable.Range(1, 7).ToArray(), start); var exercise = reading with { Id = Guid.NewGuid(), Name = "运动" }; store.SaveHabit(reading); store.SaveHabit(exercise);
                store.SetHabitCheckIn(reading.Id, today, true); store.SetHabitCheckIn(reading.Id, today.AddDays(-1), true);
                var goal = new FocusGoal(Guid.NewGuid(), "每天两次专注", GoalMetric.Sessions, 2, GoalCycle.Daily, start); store.SaveGoal(goal);
                var yesterday = today.AddDays(-1).ToDateTime(new(9, 0)); var from = new DateTimeOffset(yesterday, TimeZoneInfo.Local.GetUtcOffset(yesterday));
                store.SaveRecord(new(Guid.NewGuid(), store.GetProjects()[0].Id, TimerMode.CountUp, from, from.AddMinutes(20), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, from, from.AddMinutes(20)) }));
                main = new(store); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 850 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "goals").OpenCommand.Execute(null); Flush();
                var past = today.AddMonths(-1); history = new(main, reading, past) { Owner = window }; history.Show(); Flush();
                Button HistoryDay(DateOnly date) => (Button)((UniformGrid)history.FindName("HistoryCalendar")).Children.OfType<Border>().Single(card => card.Tag is DateOnly day && day == date).Child;
                Assert.True(HistoryDay(past).IsEnabled); HistoryDay(past).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(store.GetHabitCheckIns(), item => item.HabitId == reading.Id && item.Date == past); Assert.Equal(past.ToDateTime(TimeOnly.MinValue), ((DatePicker)history.FindName("MonthInput")).SelectedDate);
                var number = (TextBlock)HistoryDay(past).Content; Assert.Equal(past.Day.ToString(), number.Text); Assert.Equal(HorizontalAlignment.Center, number.HorizontalAlignment);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().Close())); HistoryDay(past).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(store.GetHabitCheckIns(), item => item.Date == past);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ((Button)Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)))); HistoryDay(past).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.DoesNotContain(store.GetHabitCheckIns(), item => item.Date == past);
                ((DatePicker)history.FindName("MonthInput")).SelectedDate = today.ToDateTime(TimeOnly.MinValue); Assert.False(HistoryDay(today.AddDays(1)).IsEnabled); history.Close(); history = null;
                main.Goals.View = "月"; Flush(); window.UpdateLayout(); var completion = Descendants<GoalCompletionView>(window).Single(); var calendar = (StackPanel)completion.FindName("CompletionCalendar");
                var days = Descendants<Button>(calendar).Where(button => button.Tag is CompletionDay).ToArray(); Assert.Equal(DateTime.DaysInMonth(today.Year, today.Month), days.Length);
                Assert.All(days, button => Assert.True(((FrameworkElement)button.Content).DesiredSize.Height <= button.Height - button.BorderThickness.Top - button.BorderThickness.Bottom));
                var dateField = Descendants<DatePickerTextBox>((DatePicker)completion.FindName("CompletionAnchorDate")).Single(); Assert.Equal(30, dateField.ActualHeight); Assert.Equal(2, dateField.Padding.Top);
                var partial = days.Single(button => ((CompletionDay)button.Tag).Date == today.AddDays(-1)); Assert.Equal(.5, ((CompletionDay)partial.Tag).Ratio); Assert.Contains("50%", (string)partial.ToolTip);
                Assert.NotEqual(((SolidColorBrush)partial.Background).Color, ((SolidColorBrush)days.Single(button => ((CompletionDay)button.Tag).Date == today).Background).Color);
                GoalsUiTests.Save(window, "goal-completion-month-light.png");
                main.Goals.SelectedCompletion = main.Goals.CompletionChoices.Single(item => item.HabitId == reading.Id); Flush(); Assert.All(main.Goals.CompletionDays, day => Assert.All(day.Entries, item => Assert.Equal(reading.Id, item.Id)));
                main.Goals.SelectedCompletion = main.Goals.CompletionChoices.Single(item => item.GoalId == goal.Id); Flush(); Assert.Equal(.5, main.Goals.CompletionDays.Single(item => item.Date == today.AddDays(-1)).Ratio);
                main.Goals.View = "周"; Flush(); Assert.Equal(7, Descendants<Button>(calendar).Count(button => button.Tag is CompletionDay)); var anchor = main.Goals.Anchor;
                main.Goals.PreviousCommand.Execute(null); Assert.Equal(anchor.AddDays(-7), main.Goals.Anchor); main.Goals.NextCommand.Execute(null); Assert.Equal(anchor, main.Goals.Anchor);
                GoalsUiTests.Save(window, "goal-completion-week.png");
                main.Goals.SelectedCompletion = main.Goals.CompletionChoices[0]; main.Goals.View = "年"; Flush(); window.UpdateLayout();
                Assert.Equal(DateTime.IsLeapYear(today.Year) ? 366 : 365, Descendants<Button>(calendar).Count(button => button.Tag is CompletionDay)); Assert.IsType<Canvas>(calendar.Children[0]); Assert.True(((Canvas)calendar.Children[0]).ActualWidth <= completion.ActualWidth); GoalsUiTests.Save(window, "goal-completion-year-light.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(window, "goal-completion-year-dark.png");
                main.Goals.NextCommand.Execute(null); Flush(); Assert.All(main.Goals.CompletionDays, day => Assert.True(day.Future)); Assert.Contains("暂无", main.Goals.CompletionSummary);
                main.Goals.TodayCommand.Execute(null); main.Goals.View = "月"; main.Goals.SelectedCompletion = main.Goals.CompletionChoices.Single(item => item.HabitId == reading.Id); main.Goals.Anchor = today.AddMonths(-1).ToDateTime(TimeOnly.MinValue); Flush();
                var pastButton = Descendants<Button>(calendar).Single(button => button.Tag is CompletionDay day && day.Date == past);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var popup = Application.Current.Windows.OfType<HabitHistoryWindow>().Single(); Assert.Equal(past.ToDateTime(TimeOnly.MinValue), ((DatePicker)popup.FindName("MonthInput")).SelectedDate); popup.Close();
                })); pastButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("月", new GoalsViewModel(store, _ => { }).View); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { history?.Close(); foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
