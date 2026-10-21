using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class WidgetViewSyncTests
{
    [Fact]
    public void HabitWidgetBackfillsSelectedDayAndSharesProgressAcrossDailyWeeklyMonthlyAndYearlyViews()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var yesterday = today.AddDays(-1);
                var habits = new[] { new Habit(Guid.Parse("00000001-0000-0000-0000-000000000000"), "阅读十页", Enumerable.Range(1, 7).ToArray(), today.AddDays(-30)), new Habit(Guid.Parse("00000002-0000-0000-0000-000000000000"), "整理笔记", Enumerable.Range(1, 7).ToArray(), today.AddDays(-30)) };
                foreach (var habit in habits) store.SaveHabit(habit); store.SetHabitCheckIn(habits[0].Id, today.AddDays(-2), true);
                main = new(store); window = new(main, store, Path.Combine(root, "browser")); window.Show(); Flush(); var option = main.Widgets[DesktopWidgetKind.Habits]; option.Width = 480; option.Height = 610; option.Enabled = true; Flush();
                var widget = window.DesktopWidgets.Windows[DesktopWidgetKind.Habits]; Assert.Equal("日", option.View); Assert.Contains(Children<Button>(widget), button => button.Name == "WidgetHabitCheckButton");
                var mainDay = main.Goals.Day; var picker = Children<DatePicker>(widget).Single(); picker.SelectedDate = yesterday.ToDateTime(TimeOnly.MinValue); Flush();
                Button Check() => Children<Button>(widget).Single(button => button.Name == "WidgetHabitCheckButton" && button.DataContext is HabitDayRow row && row.Id == habits[0].Id);
                Assert.Equal(yesterday, ((HabitDayRow)Check().DataContext).Date); Click(Check()); Flush(); Assert.Contains(store.GetHabitCheckIns(), check => check.HabitId == habits[0].Id && check.Date == yesterday); Assert.Equal(2, ((HabitDayRow)Check().DataContext).Streak); Assert.Equal(mainDay, main.Goals.Day);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().Close())); Click(Check()); Flush(); Assert.True(((HabitDayRow)Check().DataContext).Checked);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Click((Button)Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().FindName("ConfirmDeletionButton")))); Click(Check()); Flush(); Assert.False(((HabitDayRow)Check().DataContext).Checked);
                Click(Check()); Flush(); GoalsUiTests.Save(widget, "widget-habits-day-synced.png");
                option.View = "周"; Flush(); Assert.Equal(14, Children<Button>(widget).Count(button => button.Tag is ValueTuple<Guid, DateOnly>)); Assert.All(Children<Button>(widget).Where(button => button.Tag is ValueTuple<Guid, DateOnly>), button => { Assert.Equal(TextAlignment.Center, ((TextBlock)button.Content).TextAlignment); Assert.Equal(VerticalAlignment.Center, ((TextBlock)button.Content).VerticalAlignment); }); var second = Children<Button>(widget).Single(button => button.Tag is ValueTuple<Guid, DateOnly> value && value.Item1 == habits[1].Id && value.Item2 == yesterday); Click(second); Flush(); Assert.Contains(store.GetHabitCheckIns(), check => check.HabitId == habits[1].Id && check.Date == yesterday); GoalsUiTests.Save(widget, "widget-habits-week-synced.png");
                option.View = "月"; Flush(); Assert.Equal(DateTime.DaysInMonth(today.Year, today.Month), Children<Button>(widget).Count(button => button.Tag is CompletionDay)); var day = Children<Button>(widget).Single(button => button.Tag is CompletionDay item && item.Date == yesterday); Assert.Equal(1, ((CompletionDay)day.Tag).Ratio); GoalsUiTests.Save(widget, "widget-habits-month-synced.png"); Click(day); Flush(); Assert.Equal("日", option.View); Assert.All(Children<Button>(widget).Where(button => button.Name == "WidgetHabitCheckButton"), button => Assert.Equal(yesterday, ((HabitDayRow)button.DataContext).Date));
                option.View = "年"; Flush(); Assert.Equal(DateTime.IsLeapYear(today.Year) ? 366 : 365, Children<Button>(widget).Count(button => button.Tag is CompletionDay)); GoalsUiTests.Save(widget, "widget-habits-year-synced.png");
                option.AllItems = false; option.SelectItem(habits[0].Id, true); option.View = "月"; Flush(); Assert.All(Children<Button>(widget).Where(button => button.Tag is CompletionDay), button => Assert.All(((CompletionDay)button.Tag).Entries, entry => Assert.Equal(habits[0].Id, entry.Id)));
                day = Children<Button>(widget).Single(button => button.Tag is CompletionDay item && item.Date == today); Click(day); Flush(); Assert.Contains(store.GetHabitCheckIns(), check => check.HabitId == habits[0].Id && check.Date == today);
                main.Goals.CheckIn(habits[1].Id, today); Flush(); option.AllItems = true; option.View = "日"; Flush(); Children<DatePicker>(widget).Single().SelectedDate = today.ToDateTime(TimeOnly.MinValue); Flush(); Assert.All(Children<Button>(widget).Where(button => button.Name == "WidgetHabitCheckButton"), button => Assert.True(((HabitDayRow)button.DataContext).Checked));
                foreach (var legacy in new[] { "列表", "平铺" }) { option.View = legacy; Flush(); Assert.Equal(2, Children<Button>(widget).Count(button => Equals(button.Content, "已打卡 ✓"))); }
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); option.View = "日"; option.BackgroundMode = "纯色"; option.BackgroundColor = "#E2EDDF"; Flush(); GoalsUiTests.Save(widget, "widget-habits-day-custom-background.png");
                option.BackgroundMode = "跟随主题"; Children<DatePicker>(widget).Single().SelectedDate = today.AddDays(1).ToDateTime(TimeOnly.MinValue); Flush(); Assert.All(Children<Button>(widget).Where(button => button.Name == "WidgetHabitCheckButton"), button => Assert.False(button.IsEnabled));
                Children<DatePicker>(widget).Single().SelectedDate = yesterday.ToDateTime(TimeOnly.MinValue); Flush(); Click((Button)widget.FindName("OpenModuleButton")); Flush(); Assert.Equal(yesterday, main.Goals.Day); Assert.Equal("日", main.Goals.View); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<ConfirmDeletionWindow>().ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }

    [Fact]
    public void DateWidgetFiltersGroupsKeepsOrderAndReflectsRenameMoveAndDelete()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
                var first = new DateEvent(Guid.NewGuid(), "课程考试", today.AddDays(10), Group: "考试"); var second = new DateEvent(Guid.NewGuid(), "阶段检查", today.AddDays(3), Group: "工作"); store.SaveDateEvent(first); store.SaveDateEvent(second); store.SaveDateGroup("展示");
                main = new(store); window = new(main, store, Path.Combine(root, "browser")); window.Show(); Flush(); var option = main.Widgets[DesktopWidgetKind.Dates]; option.Width = 620; option.Height = 550; option.Enabled = true; Flush(); var widget = window.DesktopWidgets.Windows[DesktopWidgetKind.Dates];
                Assert.Equal(2, DateButtons(widget).Length); main.DateEvents.SelectedGroup = main.DateEvents.GroupChoices.Single(group => group.Name == "考试"); Flush(); Assert.Equal(2, DateButtons(widget).Length);
                foreach (var view in new[] { "列表", "大字", "月历", "卡片" }) { option.View = view; Flush(); Assert.NotEmpty(((StackPanel)widget.FindName("WidgetBody")).Children); }
                var settings = new DesktopWidgetSettingsWindow(main, option) { Owner = widget }; settings.Show(); Flush(); ((CheckBox)settings.FindName("WidgetAllDateGroups")).IsChecked = false;
                var groupOptions = (StackPanel)settings.FindName("WidgetDateGroups"); foreach (var group in groupOptions.Children.OfType<CheckBox>()) group.IsChecked = Equals(group.Content, "考试") || Equals(group.Content, "工作"); settings.Close(); Flush(); Assert.False(option.AllDateGroups); Assert.Equal(2, DateButtons(widget).Length);
                option.SelectDateGroup("工作", false); Flush(); Assert.Equal(first.Id, Assert.Single(DateButtons(widget)).Tag); Assert.Single(new DesktopWidgetsViewModel(store)[DesktopWidgetKind.Dates].Preferences.DateGroups!);
                main.DateEvents.SaveGroup("考试", "课程"); Flush(); Assert.True(option.IncludesGroup("课程")); Assert.False(option.IncludesGroup("考试")); Assert.Equal(first.Id, Assert.Single(DateButtons(widget)).Tag);
                option.SelectDateGroup("工作", true); option.View = "分组"; main.DateEvents.MoveGroup(main.DateEvents.Groups.Single(group => group.Name == "工作"), OrderMove.First); Flush(); var headings = Children<TextBlock>(widget).Where(label => label.FontSize == 13 && label.FontWeight == FontWeights.SemiBold).Select(label => label.Text).ToArray(); Assert.True(Array.IndexOf(headings, "工作") < Array.IndexOf(headings, "课程")); GoalsUiTests.Save(widget, "widget-dates-groups-synced.png");
                var add = Children<Button>(widget).Single(button => Equals(button.ToolTip, "在此分组添加日期事项") && Children<TextBlock>((DependencyObject)button.Parent).Any(label => label.Text == "课程"));
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<DateEventEditorWindow>().Single(); Assert.Equal("课程", ((ComboBox)editor.FindName("DateGroupSelector")).SelectedItem); editor.Close(); })); Click(add);
                main.DateEvents.MoveToGroup(main.DateEvents.Items.Single(row => row.Item.Id == first.Id), "工作"); Flush(); Assert.Equal(2, DateButtons(widget).Length);
                store.SaveDateEvent(second with { Title = "阶段检查已更新" }); main.ReloadCalendarData(); Flush(); Assert.Contains(Children<TextBlock>(widget), label => label.Text == "阶段检查已更新");
                main.DateEvents.DeleteGroup("工作"); Flush(); Assert.True(option.IncludesGroup("未分组")); Assert.Equal(2, DateButtons(widget).Length);
                option.View = "卡片"; main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(widget, "widget-dates-mixed-dark.png");
                option.AllItems = false; option.SelectItem(first.Id, true); Flush(); Assert.Equal(first.Id, Assert.Single(DateButtons(widget)).Tag); option.AllDateGroups = true; Flush(); Assert.Single(DateButtons(widget)); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(item => item != window && item is not DesktopWidgetWindow).ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static Button[] DateButtons(DependencyObject parent) => Children<Button>(parent).Where(button => button.Tag is Guid).ToArray();
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "FocusBloomWidgetSync", Guid.NewGuid().ToString());
    private static void Clean(string root) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
