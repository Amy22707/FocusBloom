using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Projects;
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
public sealed class WorkspaceVisualRefreshTests
{
    [Fact]
    public void ContentStaysReadableAcrossPagesSizesAndThemesWithCompactChromeAndCalendarOverflow()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomVisualRefresh", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
                var project = new FocusProject(Guid.NewGuid(), "示例项目", "学习", "#718578"); store.SaveProject(project); store.SaveProject(new(Guid.NewGuid(), "阅读与写作", "学习", "#927DAF"));
                var folder = new TodoFolder(Guid.NewGuid(), "课程作业", "未分类"); store.SaveTodoFolder(folder);
                for (var index = 0; index < 12; index++) store.SaveTodo(new(Guid.NewGuid(), folder.Id, new[] { "完成系统实验报告", "整理本周课程笔记", "阅读数据库章节", "复习示例课程习题" }[index % 4] + (index > 3 ? " " + (index + 1) : ""), Status: index == 11 ? TodoStatus.Completed : index == 0 ? TodoStatus.InProgress : TodoStatus.Pending, DueDate: today.AddDays(index < 6 ? 0 : index), DueTime: new(10 + index % 6, 0), Important: index % 2 == 0, Urgent: index % 3 == 0));
                store.SaveDateEvent(new(Guid.NewGuid(), "期中考试", today.AddDays(10))); store.SaveDateEvent(new(Guid.NewGuid(), "阅读计划开始", today.AddDays(-20), Mode: DateCountMode.CountUp));
                store.SaveSchoolTerm(new(CalendarRules.Monday(today), 16, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)) })); store.SaveCourse(new(Guid.NewGuid(), "示例课程", 1, 1, 2, Room: "教室101", Teacher: "示例教师"));
                for (var index = 0; index < 3; index++) { var at = DateTimeOffset.Now.AddDays(-index).AddHours(-2); store.SaveRecord(new(Guid.NewGuid(), project.Id, TimerMode.CountUp, at, at.AddMinutes(45), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, at, at.AddMinutes(45)) })); }
                store.SaveGoal(new(Guid.NewGuid(), "每天专注一小时", GoalMetric.Minutes, 60, GoalCycle.Daily, today.AddDays(-5))); store.SaveHabit(new(Guid.NewGuid(), "睡前阅读", Enumerable.Range(1, 7).ToArray(), today.AddDays(-5))); store.SaveHabit(new(Guid.NewGuid(), "整理学习笔记", new[] { 1, 3, 5 }, today.AddDays(-5)));
                store.SaveDailyReflection(new(today, 4, new[] { "学习" }, "慢慢找到自己的节奏", "完成课程阅读", "整理实验结果"));
                main = new(store); main.SelectedPalette = main.Palettes.Single(item => item.Id == "sage"); window = new(main, store, Path.Combine(root, "browser")); window.Show(); Flush(); var todos = (TodosView)window.FindName("TodoPage"); main.Todos.SelectedFolder = main.Todos.Folders.Single(item => item.Id == folder.Id);
                foreach (var layout in new (double Width, double Height, bool Collapsed, bool Dark)[] { (1120, 720, false, false), (1440, 900, true, false), (900, 560, true, true), (1120, 720, false, true) })
                {
                    window.Width = layout.Width; window.Height = layout.Height; window.NavigationCollapsed = layout.Collapsed; main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == (layout.Dark ? AppearanceMode.Dark : AppearanceMode.Light)); Flush();
                    var header = (Border)window.FindName("WorkspaceHeader"); Assert.InRange(header.ActualHeight, 50, 54); Assert.Equal(((FrameworkElement)window.Content).ActualWidth, header.ActualWidth); var tabs = (TabControl)window.FindName("MainTabs"); Assert.Equal(layout.Collapsed ? 64 : 160, ((ColumnDefinition)tabs.Template.FindName("NavigationColumn", tabs)).ActualWidth);
                    var suffix = $"{layout.Width:0}-{(layout.Dark ? "dark" : "light")}";
                    foreach (var key in new[] { "focus", "projects", "todos", "calendar", "goals", "dates", "reflection", "records", "settings" })
                    {
                        main.Navigation.Items.Single(item => item.Key == key).OpenCommand.Execute(null); if (key == "todos") main.Todos.ViewMode = "列表"; if (key == "calendar") main.Calendar.Mode = "月"; if (key == "records") main.RecordPage = "统计"; Flush();
                        if (key == "todos")
                        {
                            var list = (ListBox)todos.FindName("TodoList"); Assert.True(list.ActualHeight >= layout.Height * .48); var cards = Descendants<Border>(list).Where(item => item.Name == "TodoCardSurface").ToArray(); Assert.Equal(12, cards.Length); Assert.All(cards, card => Assert.Equal(104, card.ActualHeight));
                            var title = Descendants<Button>(cards[0]).Single(item => item.Name == "OpenListTodoDetailsButton"); Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<TodoDetailsWindow>().Single().Close())); title.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                            if (!layout.Dark && layout.Width == 1120)
                            {
                                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<TodoDetailsWindow>().Single().Close())); cards[0].RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent }); Flush();
                                var item = (TodoRow)cards[1].DataContext; var check = Descendants<CheckBox>(list).Single(box => box.DataContext is TodoRow row && row.Id == item.Id); check.IsChecked = true; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Flush(); Assert.True(main.Todos.VisibleItems.Last().Completed);
                                check = Descendants<CheckBox>(list).Single(box => box.DataContext is TodoRow row && row.Id == item.Id); check.IsChecked = false; check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Flush(); Assert.False(main.Todos.Items.Single(row => row.Id == item.Id).Completed);
                            }
                        }
                        if (key == "calendar")
                        {
                            var calendar = (CalendarView)window.FindName("CalendarPage"); Assert.InRange(((FrameworkElement)calendar.FindName("CalendarToolbar")).ActualHeight, 30, 40); Assert.True(((ScrollViewer)calendar.FindName("CalendarViewport")).ActualHeight > calendar.ActualHeight * .75);
                            Assert.Contains(Descendants<Button>(calendar), item => item.Tag is CalendarEntry); Assert.Contains(Descendants<Button>(calendar), item => item.Content is string text && text.StartsWith("＋"));
                            ((Button)calendar.FindName("CalendarDateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.True(((Popup)calendar.FindName("CalendarDatePopup")).IsOpen); var year = (ComboBox)calendar.FindName("CalendarYearSelector"); year.SelectedItem = today.Year - 10; Flush(); Assert.Equal(today.Year - 10, main.Calendar.Date.Year); year.SelectedItem = today.Year; ((Popup)calendar.FindName("CalendarDatePopup")).IsOpen = false; Flush();
                            Descendants<Button>(calendar).First(item => item.Content is string text && text.StartsWith("＋")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Equal("日", main.Calendar.Mode); Assert.Equal(today, main.Calendar.Date); main.Calendar.Mode = "月"; Flush();
                        }
                        GoalsUiTests.Save(window, $"refresh-{key}-{suffix}.png");
                    }
                    main.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); main.Todos.ViewMode = "四象限"; Flush(); Assert.NotNull(((System.Windows.Shapes.Path)todos.FindName("QuadrantAxes")).Data); GoalsUiTests.Save(window, $"refresh-quadrants-{suffix}.png");
                    main.Navigation.Items.Single(item => item.Key == "calendar").OpenCommand.Execute(null); foreach (var mode in new[] { "周", "课程表" }) { main.Calendar.Mode = mode; Flush(); Assert.NotNull(((ContentControl)((CalendarView)window.FindName("CalendarPage")).FindName("CalendarDayHeader")).Content); GoalsUiTests.Save(window, $"refresh-calendar-{mode}-{suffix}.png"); }
                }
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<TodoDetailsWindow>().ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
