using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class DesktopWidgetRefinementTests
{
    [Fact]
    public void CompactInputsFitDatedTodosComeFirstAndUnpinnedWidgetsAllowOtherWindowsAboveFromFirstShow()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomWidgetRefinement", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null; Window? probe = null; DesktopWidgetSettingsWindow? settings = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var folder = new TodoFolder(Guid.NewGuid(), "事项", "未分类"); store.SaveTodoFolder(folder);
                var undated = new TodoItem(Guid.NewGuid(), folder.Id, "没有截止日期"); var dated = new TodoItem(Guid.NewGuid(), folder.Id, "明天截止", DueDate: today.AddDays(1)); store.SaveTodo(undated); store.SaveTodo(dated);
                main = new(store); main.Widgets[DesktopWidgetKind.Todos].View = "按截止日期"; main.Widgets[DesktopWidgetKind.Dates].View = "月历";
                foreach (var kind in new[] { DesktopWidgetKind.Focus, DesktopWidgetKind.Todos, DesktopWidgetKind.Dates }) main.Widgets[kind].Enabled = true;
                window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Flush();
                var todos = window.DesktopWidgets.Windows[DesktopWidgetKind.Todos]; var dates = window.DesktopWidgets.Windows[DesktopWidgetKind.Dates]; var focus = window.DesktopWidgets.Windows[DesktopWidgetKind.Focus];
                var sorted = Descendants<Button>(todos).Where(item => item.Tag is Guid).Select(item => (Guid)item.Tag).ToArray(); Assert.Equal(new[] { dated.Id, undated.Id }, sorted);
                foreach (var widget in window.DesktopWidgets.Windows.Values)
                {
                    Assert.False(widget.Topmost); var handle = new WindowInteropHelper(widget).Handle; Assert.Equal(0, GetWindowLong(handle, -20) & 8);
                    foreach (var combo in Descendants<ComboBox>(widget)) { combo.ApplyTemplate(); var presenter = (ContentPresenter)combo.Template.FindName("WidgetSelectionContent", combo); Assert.True(presenter.ActualHeight >= combo.FontSize, $"{widget.Option.Name} / {combo.SelectedItem}：内容高度 {presenter.ActualHeight}"); Assert.True(presenter.DesiredSize.Height <= presenter.ActualHeight + .5); var bounds = presenter.TransformToAncestor(combo).TransformBounds(new Rect(presenter.RenderSize)); Assert.InRange(bounds.Bottom, combo.FontSize, combo.ActualHeight - 2); }
                }
                probe = new Window { Title = "组件层级验证", Width = 220, Height = 160, ShowInTaskbar = false }; probe.Show(); Flush(); var probeHandle = new WindowInteropHelper(probe).Handle;
                Assert.True(SetWindowPos(probeHandle, IntPtr.Zero, 0, 0, 0, 0, 0x13)); Flush(); AssertAbove(probeHandle, new WindowInteropHelper(todos).Handle);
                var picker = Descendants<DatePicker>(dates).Single(); picker.ApplyTemplate(); var input = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker); input.ApplyTemplate();
                var content = (ScrollViewer)input.Template.FindName("PART_ContentHost", input); Assert.True(content.ActualHeight >= 18); Assert.True(input.TransformToAncestor(picker).TransformBounds(new Rect(input.RenderSize)).Bottom <= picker.ActualHeight + .5);
                var popup = (Popup)picker.Template.FindName("PART_Popup", picker); Assert.NotNull(popup); picker.IsDropDownOpen = true; dates.Render(); Flush(); Assert.True(popup.IsOpen); Assert.True(picker.IsLoaded); Assert.Same(picker, Descendants<DatePicker>(dates).Single()); picker.IsDropDownOpen = false; Flush();
                GoalsUiTests.Save(focus, "widget-compact-period-fixed.png"); GoalsUiTests.Save(dates, "widget-compact-date-fixed.png"); GoalsUiTests.Save(todos, "widget-deadline-order-fixed.png");
                settings = new(main, todos.Option) { Owner = todos }; settings.Show(); Flush(); ((CheckBox)settings.FindName("WidgetTopmostInput")).IsChecked = true; Flush(); Assert.True(todos.Option.Topmost); Assert.True(todos.Topmost);
                settings.Close(); Flush(); var native = new WindowInteropHelper(todos).Handle; Assert.NotEqual(0, GetWindowLong(native, -20) & 8);
                main.Widgets[DesktopWidgetKind.Calendar].Enabled = true; Flush(); Assert.NotEqual(0, GetWindowLong(native, -20) & 8);
                todos.Option.Topmost = false; Flush(); Assert.Equal(0, GetWindowLong(new WindowInteropHelper(todos).Handle, -20) & 8);
                todos.Option.BackgroundColor = "#29443E"; todos.Option.BackgroundMode = "纯色"; Flush(); Assert.Equal(Color.FromRgb(41, 68, 62), ((SolidColorBrush)((Border)todos.FindName("WidgetSurface")).Background).Color); Assert.Equal("#FFF3F5FA", ((SolidColorBrush)todos.FindResource("TextBrush")).Color.ToString());
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); Assert.Equal(Color.FromRgb(41, 68, 62), ((SolidColorBrush)((Border)todos.FindName("WidgetSurface")).Background).Color);
                GoalsUiTests.Save(todos, "widget-custom-color.png");
                todos.Option.BackgroundImage = main.Media.Images.First().Asset.File; todos.Option.BackgroundMode = "图片";
                var until = DateTime.UtcNow.AddSeconds(5); while (((Border)todos.FindName("WidgetBackground")).Background is not ImageBrush && DateTime.UtcNow < until) { Flush(); Thread.Sleep(10); } Flush(); Assert.IsType<ImageBrush>(((Border)todos.FindName("WidgetBackground")).Background); GoalsUiTests.Save(todos, "widget-custom-image.png");
                Assert.Equal("跟随主题", dates.Option.BackgroundMode); todos.Option.BackgroundMode = "跟随主题"; Flush(); Assert.False(todos.Resources.Contains("TextBrush"));
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { settings?.Close(); probe?.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    [Fact]
    public void CalendarWidgetSeparatesCoursesNavigatesDatesAndOpensCourseDetails()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarWidget", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var monday = CalendarRules.Monday(today); var folder = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); store.SaveTodoFolder(folder);
                var todo = new TodoItem(Guid.NewGuid(), folder.Id, "提交作业", DueDate: today, DueTime: new(18, 0)); store.SaveTodo(todo); var date = new DateEvent(Guid.NewGuid(), "期中考试", today); store.SaveDateEvent(date);
                store.SaveSchoolTerm(new(monday, 16, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)) }));
                var course = new SchoolCourse(Guid.NewGuid(), "示例课程", 1, 1, 2, Room: "教室101", Meetings: new[] { new CourseMeeting(Guid.NewGuid(), 1, 1, 2), new CourseMeeting(Guid.NewGuid(), 3, 1, 1) }); store.SaveCourse(course);
                main = new(store); main.Calendar.Mode = "年"; var option = main.Widgets[DesktopWidgetKind.Calendar]; option.Enabled = true; window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Flush(); var calendar = window.DesktopWidgets.Windows[DesktopWidgetKind.Calendar];
                foreach (var view in option.Views)
                {
                    option.View = view; Flush(); Assert.Equal("年", main.Calendar.Mode);
                    var entries = Descendants<Button>(calendar).Where(item => item.Tag is CalendarEntry).Select(item => (CalendarEntry)item.Tag).ToArray();
                    if (view == "课程表") { Assert.Equal(2, entries.Length); Assert.All(entries, entry => Assert.Equal("课程", entry.Kind)); }
                    else { Assert.DoesNotContain(entries, entry => entry.Kind == "课程"); if (view != "年") { Assert.Contains(entries, entry => entry.Id == todo.Id); Assert.Contains(entries, entry => entry.Id == date.Id); } }
                    GoalsUiTests.Save(calendar, "widget-calendar-" + view + ".png");
                }
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var editor = Application.Current.Windows.OfType<CourseEditorWindow>().Single(); editor.Close(); })); Descendants<Button>(calendar).First(item => item.Tag is CalendarEntry { Kind: "课程" }).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Equal("课程表", main.Calendar.Mode);
                option.View = "月"; Flush(); var picker = Descendants<DatePicker>(calendar).Single(); picker.SelectedDate = today.AddYears(-10).ToDateTime(TimeOnly.MinValue); Flush(); Assert.Equal(today.AddYears(-10).ToDateTime(TimeOnly.MinValue), Descendants<DatePicker>(calendar).Single().SelectedDate);
                ((Button)calendar.FindName("OpenModuleButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Equal(today.AddYears(-10), main.Calendar.Date); Assert.Equal("月", main.Calendar.Mode);
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(calendar, "widget-calendar-dark.png");
                window.CloseDesktopWidgetsForExit(); Assert.Empty(Application.Current.Windows.OfType<DesktopWidgetWindow>());
            }
            finally { foreach (var editor in Application.Current.Windows.OfType<CourseEditorWindow>().ToArray()) editor.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void AssertAbove(IntPtr above, IntPtr below)
    { for (var handle = GetWindow(below, 3); handle != IntPtr.Zero; handle = GetWindow(handle, 3)) if (handle == above) return; Assert.Fail("普通窗口无法覆盖未置顶组件。"); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
