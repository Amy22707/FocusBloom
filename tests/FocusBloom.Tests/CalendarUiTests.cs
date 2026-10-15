using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
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
public sealed class CalendarUiTests
{
    [Fact]
    public void ChoosingCourseStartSelectsNextEndAndAllowsManualAdjustment()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCoursePeriods", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db"));
                store.SaveSchoolTerm(new(new(2026, 10, 12), 16, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)), new ClassPeriod(4, new(11, 0), new(11, 50)) }));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")); window.Show();
                var editor = new CourseEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)editor.FindName("NameInput")).Text = "课程时间联动";
                    var start = (ComboBox)editor.FindName("StartPeriodSelector"); var end = (ComboBox)editor.FindName("EndPeriodSelector");
                    Assert.Equal(0, start.SelectedIndex); Assert.Equal(1, end.SelectedIndex);
                    start.SelectedIndex = 1; Assert.Equal(2, end.SelectedIndex); Assert.Contains("11:50", end.SelectedItem.ToString());
                    start.SelectedIndex = 2; Assert.Equal(2, end.SelectedIndex);
                    start.SelectedIndex = 0; Assert.Equal(1, end.SelectedIndex); end.SelectedIndex = 0; Assert.Equal(0, end.SelectedIndex);
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(editor.ShowDialog()); var course = Assert.Single(store.GetCourses()); Assert.Equal(1, course.EndPeriod);
                var saved = new CourseEditorWindow(main, course) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    Assert.Equal(0, ((ComboBox)saved.FindName("EndPeriodSelector")).SelectedIndex);
                    ((ComboBox)saved.FindName("StartPeriodSelector")).SelectedIndex = 1; Assert.Equal(2, ((ComboBox)saved.FindName("EndPeriodSelector")).SelectedIndex);
                    ((Button)saved.FindName("AddMeetingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                    var added = Descendants<Expander>(saved).Where(expander => expander.GetType().Name == "CourseMeetingEditor").Last();
                    var selectors = Descendants<ComboBox>((DependencyObject)added.Content).ToArray(); selectors[1].SelectedIndex = 2; Assert.Equal(2, selectors[2].SelectedIndex);
                    ((Button)saved.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(saved.ShowDialog()); course = Assert.Single(store.GetCourses()); Assert.Equal(2, course.Meetings!.Count);
                Assert.Equal(2, course.Meetings[0].StartPeriod); Assert.Equal(4, course.Meetings[0].EndPeriod); Assert.Equal(4, course.Meetings[1].StartPeriod); Assert.Equal(4, course.Meetings[1].EndPeriod);
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close();
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void CourseCardTextUsesCompleteLinesAtDifferentWidthsAndScales()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCourseText", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 12);
                store.SaveSchoolTerm(new(day, 16, new[] { new ClassPeriod(1, new(13, 0), new(14, 50)) }));
                store.SaveCourse(new(Guid.NewGuid(), "示例课程", 1, 1, 1, Room: "教室101", Teacher: "示例教师"));
                store.SaveCourse(new(Guid.NewGuid(), "示例课程二", 2, 1, 1, Room: "教室102", Teacher: "教师甲、教师乙"));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")) { Width = 1120, Height = 720 }; window.Show();
                ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("CalendarTab"); main.Calendar.Mode = "课程表"; main.Calendar.Date = day.AddDays(35);
                var calendar = (CalendarView)window.FindName("CalendarPage"); var scale = main.CardSizing.Options.Single(option => option.Name == "课程表");
                foreach (var width in new[] { 900d, 1120d })
                foreach (var percentage in new[] { 70d, 100d, 140d })
                {
                    window.Width = width; scale.Percentage = percentage; Flush(); window.UpdateLayout(); calendar.Render(); Flush(); window.UpdateLayout();
                    var cards = Descendants<Button>(calendar).Where(button => button.Tag is CalendarEntry entry && entry.Kind == "课程").ToArray(); Assert.Equal(2, cards.Length);
                    foreach (var card in cards)
                    {
                        var text = Descendants<TextBlock>(card).ToArray(); Assert.Equal(2, text.Length);
                        foreach (var label in text)
                        {
                            Assert.Equal(LineStackingStrategy.BlockLineHeight, label.LineStackingStrategy);
                            Assert.Equal(Math.Round(label.MaxHeight / label.LineHeight), label.MaxHeight / label.LineHeight, 5);
                            Assert.Equal(Math.Round(label.ActualHeight / label.LineHeight), label.ActualHeight / label.LineHeight, 5);
                            var bounds = label.TransformToAncestor(card).TransformBounds(new Rect(label.RenderSize));
                            Assert.True(bounds.Bottom <= card.ActualHeight - card.Padding.Bottom + 0.5, $"课程文字超出卡片：{bounds.Bottom} / {card.ActualHeight}");
                        }
                        Assert.Equal(3, text[1].MaxHeight / text[1].LineHeight, 5); Assert.Equal(TextTrimming.CharacterEllipsis, text[1].TextTrimming);
                        Assert.Contains(((CalendarEntry)card.Tag).Detail, card.ToolTip as string ?? "");
                    }
                    if (width == 1120 && percentage == 100) Save(window, "course-card-complete-lines-light.png");
                }
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); scale.Percentage = 100; Flush(); calendar.Render(); Flush(); Save(window, "course-card-complete-lines-dark.png");
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void CalendarModesEditorsDateEventsAndWeeklyCoursesWorkInBothThemes()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = new DateOnly(2026, 10, 14);
                var folder = new TodoFolder(Guid.NewGuid(), "学习", "未分类"); store.SaveTodoFolder(folder);
                var todo = new TodoItem(Guid.NewGuid(), folder.Id, "ICS作业", DueDate: day, DueTime: new(18, 0)); store.SaveTodo(todo);
                store.SaveSchoolTerm(new(new(2026, 9, 7), 20, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)), new ClassPeriod(3, new(10, 10), new(11, 0)), new ClassPeriod(4, new(11, 10), new(12, 0)) }));
                var course = new SchoolCourse(Guid.NewGuid(), "示例项目", 3, 1, 2, Room: "教室101"); store.SaveCourse(course);
                store.SaveCalendarPlan(new(Guid.NewGuid(), "复习示例课程", day, new(14, 0), 90, todo.Id));
                store.SaveDateEvent(new(Guid.NewGuid(), "期末考试", day.AddDays(30), Pinned: true, Notes: "每天进步一点"));
                store.SaveDateEvent(new(Guid.NewGuid(), "开始学习", day.AddDays(-100), Mode: DateCountMode.CountUp));
                var start = day.ToDateTime(new(14, 10)); var end = day.ToDateTime(new(14, 40));
                var offset = TimeZoneInfo.Local.GetUtcOffset(start);
                store.SaveRecord(new(Guid.NewGuid(), store.GetProjects()[0].Id, TimerMode.CountUp, new(start, offset), new(end, offset), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, new(start, offset), new(end, offset)) }, TodoId: todo.Id));
                main = new(store); main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Light);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1240, Height = 820 }; window.Show();
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("CalendarTab"); Flush(); window.UpdateLayout();
                var calendar = (CalendarView)window.FindName("CalendarPage"); main.Calendar.Date = day;
                foreach (var mode in main.Calendar.Modes)
                {
                    main.Calendar.Mode = mode; Flush(); window.UpdateLayout(); calendar.Render(); Flush();
                    Assert.True(((StackPanel)calendar.FindName("CalendarContent")).Children.Count > 0);
                    Save(window, "calendar-" + mode + "-light.png");
                }
                main.Calendar.Mode = "月"; Flush(); window.UpdateLayout(); calendar.Render(); Flush();
                Assert.InRange(((ScrollViewer)calendar.FindName("CalendarViewport")).ScrollableHeight, 0, 1);
                main.Calendar.Mode = "日"; main.Calendar.Move(1); Assert.Equal(day.AddDays(1), main.Calendar.Date); main.Calendar.Date = day;
                var view = (ScrollViewer)calendar.FindName("CalendarViewport"); view.ScrollToVerticalOffset(13 * 48); Flush(); Save(window, "calendar-day-timeline.png");
                var entries = Descendants<Button>(calendar).Where(button => button.Tag is CalendarEntry).ToArray(); Assert.DoesNotContain(entries, button => ((CalendarEntry)button.Tag).Kind == "专注");
                tabs.SelectedItem = window.FindName("RecordsTab"); main.FocusCalendar.Mode = "日"; main.FocusCalendar.Date = day; Flush(); window.UpdateLayout();
                var focusCalendar = (CalendarView)window.FindName("FocusCalendarPage"); focusCalendar.Render(); Flush();
                Assert.Contains(Descendants<Button>(focusCalendar), button => button.Tag is CalendarEntry entry && entry.Kind == "专注");
                Assert.DoesNotContain(Descendants<Button>(focusCalendar), button => button.Tag is CalendarEntry entry && entry.Kind == "待办");
                Save(window, "focus-calendar-day.png"); tabs.SelectedItem = window.FindName("CalendarTab"); Flush();
                var dateEditor = new DateEventEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)dateEditor.FindName("TitleInput")).Text = "每月检查"; ((DatePicker)dateEditor.FindName("DateInput")).SelectedDate = new DateTime(2026, 10, 31);
                    ((ComboBox)dateEditor.FindName("RepeatSelector")).SelectedIndex = 2; ((Button)dateEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(dateEditor.ShowDialog()); Assert.Contains(store.GetDateEvents(), item => item.Title == "每月检查" && item.Repeat == DateRepeat.Monthly);
                var courseEditor = new CourseEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)courseEditor.FindName("NameInput")).Text = "离散数学"; ((TextBox)courseEditor.FindName("WeeksInput")).Text = "1-16";
                    ((ComboBox)courseEditor.FindName("PatternSelector")).SelectedIndex = 1; ((Button)courseEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(courseEditor.ShowDialog()); Assert.Contains(store.GetCourses(), item => item.Name == "离散数学" && item.Pattern == WeekPattern.Odd);
                var change = new CourseChangeWindow(main, course, day) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { ((DatePicker)change.FindName("NewDateInput")).SelectedDate = day.AddDays(1).ToDateTime(TimeOnly.MinValue); ((Button)change.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
                Assert.True(change.ShowDialog()); Assert.Equal(day.AddDays(1), Assert.Single(store.GetCourseChanges()).NewDate);
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark);
                main.Calendar.Mode = "课程表"; Flush(); window.UpdateLayout(); calendar.Render(); Save(window, "calendar-timetable-dark.png");
                Assert.Equal(0, ((ScrollViewer)calendar.FindName("CalendarViewport")).VerticalOffset);
                tabs.SelectedItem = window.FindName("DateEventsTab"); Flush(); window.UpdateLayout(); Save(window, "future-dates-dark.png");
                Assert.Equal(3, main.DateEvents.Items.Count);
                var row = new DateEventRow(new(Guid.NewGuid(), "周年", day.AddDays(-100), Mode: DateCountMode.CountUp, Repeat: DateRepeat.Yearly), day); Assert.Equal("100", row.Number);
                tabs.SelectedItem = window.FindName("CalendarTab"); main.Calendar.Mode = "月"; Flush(); window.UpdateLayout(); calendar.Render(); Save(window, "calendar-month-dark.png");
                Assert.All(Descendants<TextBlock>(calendar).Where(text => text.IsVisible), text => Assert.NotNull(text.Foreground));
            }
            finally
            {
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close();
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var descendant in Descendants<T>(child)) yield return descendant; } }
    private static void Save(MainWindow window, string name)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(content.RenderSize); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(content), null, bounds); } image.Render(visual);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(file);
    }
}
