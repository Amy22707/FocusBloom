using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
using Microsoft.Toolkit.Uwp.Notifications;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class CalendarPreferencesUiTests
{
    [Fact]
    public void NavigationFooterStaysNearBottomWithoutOverlappingNavigationAtSmallWindowHeights()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomNavigationFooter", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); main = new(store); window = new(main, store, Path.Combine(directory, "browser")); window.Show();
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("PreferencesTab"); window.NavigationCollapsed = false; Flush();
                var footer = (FrameworkElement)tabs.Template.FindName("NavigationFooter", tabs); var viewport = (ScrollViewer)tabs.Template.FindName("NavigationViewport", tabs);
                foreach (var height in new[] { 560d, 720d, 820d })
                {
                    window.Height = height; Flush(); window.UpdateLayout(); viewport.ScrollToBottom(); Flush(); window.UpdateLayout();
                    var footerBounds = footer.TransformToAncestor(tabs).TransformBounds(new Rect(footer.RenderSize));
                    var viewportBounds = viewport.TransformToAncestor(tabs).TransformBounds(new Rect(viewport.RenderSize));
                    var preferences = Descendants<Button>(tabs).Single(button => button.DataContext is NavigationOption option && option.Key == "settings"); var itemBounds = preferences.TransformToAncestor(tabs).TransformBounds(new Rect(preferences.RenderSize));
                    Assert.InRange(tabs.ActualHeight - footerBounds.Bottom, 23, 25);
                    Assert.True(viewportBounds.Bottom + 15 <= footerBounds.Top);
                    Assert.True(itemBounds.Bottom <= viewportBounds.Bottom + 1);
                    Assert.True(itemBounds.Bottom + 15 <= footerBounds.Top);
                    if (height == 560) Assert.True(viewport.ScrollableHeight > 0);
                    if (height == 720) Save(window, "navigation-footer-spacing-light.png");
                }
                window.Height = 560; main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); viewport.ScrollToBottom(); Flush(); Save(window, "navigation-footer-spacing-dark.png");
                window.NavigationCollapsed = true; Flush(); Assert.Equal(Visibility.Collapsed, footer.Visibility);
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void NavigationAndSectionStatesSurviveReopenAndEveryWorkspacePageCanShowFullscreen()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomWorkspacePresentation", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); main = new(store);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1200, Height = 820 }; window.Show(); Flush();
                var tabs = (TabControl)window.FindName("MainTabs");
                var navigationToggle = (ToggleButton)tabs.Template.FindName("NavigationCollapseToggle", tabs); navigationToggle.IsChecked = true; Flush(); window.UpdateLayout();
                Assert.True(window.NavigationCollapsed); Assert.Equal(64, ((ColumnDefinition)tabs.Template.FindName("NavigationColumn", tabs)).ActualWidth);
                foreach (var button in Descendants<Button>(tabs).Where(button => button.DataContext is NavigationOption && button.Content is StackPanel))
                {
                    var header = (StackPanel)button.Content; Assert.Equal(Visibility.Collapsed, ((TextBlock)header.Children[1]).Visibility); Assert.NotNull(button.ToolTip);
                }
                tabs.SelectedIndex = 1; Flush(); Save(window, "navigation-icons-only.png");
                ((ToggleButton)window.FindName("ProjectSidebarToggle")).IsChecked = true;
                var todoView = (TodosView)window.FindName("TodoPage"); ((ToggleButton)todoView.FindName("TodoSidebarToggle")).IsChecked = true;
                var media = (FocusMediaSettingsView)((Border)window.FindName("FocusMediaSettingsCard")).Child;
                ((Expander)media.FindName("BackgroundLibrary")).IsExpanded = true; ((Expander)media.FindName("QuoteLibrary")).IsExpanded = false; ((Expander)media.FindName("SoundLibrary")).IsExpanded = true;
                var originalSize = new Size(window.Width, window.Height);
                foreach (var page in new[] { tabs.Items[1], window.FindName("TodoTab"), window.FindName("CalendarTab"), window.FindName("DateEventsTab"), window.FindName("RecordsTab"), window.FindName("PreferencesTab") })
                {
                    tabs.SelectedItem = page; Flush(); ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); window.UpdateLayout();
                    Assert.True(window.ContentFullscreen); Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("WorkspaceHeader")).Visibility);
                    Assert.Equal(0, ((ColumnDefinition)tabs.Template.FindName("NavigationColumn", tabs)).ActualWidth);
                    Assert.Equal(Visibility.Visible, ((Button)window.FindName("ExitContentFullscreenButton")).Visibility); Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("FullscreenView")).Visibility);
                    if (page == window.FindName("CalendarTab")) Save(window, "calendar-content-fullscreen.png");
                    ((Button)window.FindName("ExitContentFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                    Assert.False(window.ContentFullscreen); Assert.True(window.NavigationCollapsed); Assert.Equal(originalSize, new Size(window.Width, window.Height));
                }
                main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); Assert.True(main.IsFocusActive);
                tabs.SelectedItem = window.FindName("RecordsTab"); Flush(); ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.True(window.ContentFullscreen); Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("FullscreenView")).Visibility);
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent }); Flush(); Assert.False(window.ContentFullscreen);
                tabs.SelectedIndex = 0; Flush(); ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.False(window.ContentFullscreen); Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("FullscreenView")).Visibility);
                window.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent }); Flush();
                window.DisposeBrowser(); window.Close(); main.Media.Dispose(); main = new(new SqliteFocusStore(path)); window = new(main, store, Path.Combine(directory, "browser-reopened")); window.Show(); Flush();
                Assert.True(window.NavigationCollapsed); Assert.True(((ToggleButton)window.FindName("ProjectSidebarToggle")).IsChecked);
                Assert.True(((ToggleButton)((TodosView)window.FindName("TodoPage")).FindName("TodoSidebarToggle")).IsChecked);
                media = (FocusMediaSettingsView)((Border)window.FindName("FocusMediaSettingsCard")).Child;
                Assert.True(((Expander)media.FindName("BackgroundLibrary")).IsExpanded); Assert.False(((Expander)media.FindName("QuoteLibrary")).IsExpanded); Assert.True(((Expander)media.FindName("SoundLibrary")).IsExpanded);
                window.NavigationCollapsed = false; ((ToggleButton)window.FindName("ProjectSidebarToggle")).IsChecked = false; ((ToggleButton)((TodosView)window.FindName("TodoPage")).FindName("TodoSidebarToggle")).IsChecked = false;
                ((Expander)media.FindName("BackgroundLibrary")).IsExpanded = false; ((Expander)media.FindName("QuoteLibrary")).IsExpanded = true; ((Expander)media.FindName("SoundLibrary")).IsExpanded = false;
                window.DisposeBrowser(); window.Close(); main.Media.Dispose(); main = new(new SqliteFocusStore(path)); window = new(main, store, Path.Combine(directory, "browser-expanded")); window.Show(); Flush();
                Assert.False(window.NavigationCollapsed); Assert.False(((ToggleButton)window.FindName("ProjectSidebarToggle")).IsChecked);
                Assert.False(((ToggleButton)((TodosView)window.FindName("TodoPage")).FindName("TodoSidebarToggle")).IsChecked);
                media = (FocusMediaSettingsView)((Border)window.FindName("FocusMediaSettingsCard")).Child;
                Assert.False(((Expander)media.FindName("BackgroundLibrary")).IsExpanded); Assert.True(((Expander)media.FindName("QuoteLibrary")).IsExpanded); Assert.False(((Expander)media.FindName("SoundLibrary")).IsExpanded);
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void PreferencesKeepScrollPositionAndSizingPreviewTracksTheSelectedModule()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomPreferencesPreview", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); main = new(store);
                window = new(main, store, Path.Combine(directory, "browser")) { Width = 1200, Height = 820 }; window.Show();
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("PreferencesTab"); Flush(); window.UpdateLayout();
                ((Expander)window.FindName("SizeSettingsSection")).IsExpanded = true;
                var scroll = (ScrollViewer)window.FindName("PreferencesScrollViewer"); Flush(); window.UpdateLayout(); scroll.ScrollToVerticalOffset(450); Flush(); window.UpdateLayout();
                var position = scroll.VerticalOffset; Assert.True(position > 100);
                tabs.SelectedItem = window.FindName("TodoTab"); Flush(); tabs.SelectedItem = window.FindName("PreferencesTab"); Flush(); window.UpdateLayout();
                Assert.InRange(scroll.VerticalOffset, position - 1, position + 1);
                scroll.ScrollToBottom(); Flush(); position = scroll.VerticalOffset;
                tabs.SelectedIndex = 1; Flush(); tabs.SelectedItem = window.FindName("PreferencesTab"); Flush(); Assert.InRange(scroll.VerticalOffset, position - 1, position + 1);
                var selector = (ComboBox)window.FindName("CardSizeSelector"); selector.BringIntoView(); Flush();
                selector.SelectedIndex = 0; Flush(); var slider = (Slider)window.FindName("CardSizeSlider"); var preview = (Border)window.FindName("CardSizePreview");
                foreach (var value in new[] { 70d, 140d })
                {
                    slider.Value = value; Flush(); window.UpdateLayout(); Assert.Equal(value, main.CardSizing.Options[0].Percentage);
                    var bounds = preview.TransformToAncestor(scroll).TransformBounds(new Rect(preview.RenderSize));
                    Assert.InRange(bounds.Width, 220 * value / 100 - 1, 220 * value / 100 + 1); Assert.InRange(bounds.Height, 120 * value / 100 - 1, 120 * value / 100 + 1);
                    Save(window, value == 70 ? "preferences-preview-small.png" : "preferences-preview-large.png");
                }
                selector.SelectedIndex = 7; Flush(); slider.Value = 140; Flush(); window.UpdateLayout();
                Assert.Same(main.CardSizing.Options[7], preview.DataContext); Assert.Equal(180, preview.Height);
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush();
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("FieldBrush")).Color, ((SolidColorBrush)preview.Background).Color);
                Save(window, "preferences-preview-dark.png");
                selector.SelectedIndex = 2; Flush(); Assert.False(main.CardSizing.PreviewOption.ShowPreviewDetail); Assert.Equal(32, preview.Height);
                Assert.NotNull(Descendants<TextBlock>(preview).FirstOrDefault(text => text.Text == "完成课程实验"));
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    [Trait("DesktopMode", "Interactive")]
    public void CourseMeetingsSizingDateNavigationAndNativeReminderSwitchWorkAndPersist()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCalendarPreferences", Guid.NewGuid().ToString());
            MainWindow? window = null; MainViewModel? main = null; WindowsReminderService? service = null;
            try
            {
                var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var day = DateOnly.FromDateTime(DateTime.Today);
                var nextMonday = CalendarRules.Monday(day).AddDays(7);
                store.SaveSchoolTerm(new(nextMonday, 2, new[] { new ClassPeriod(1, new(8, 0), new(8, 50)), new ClassPeriod(2, new(9, 0), new(9, 50)) }));
                var folder = new TodoFolder(Guid.NewGuid(), "课程作业", "未分类"); store.SaveTodoFolder(folder);
                store.SaveTodo(new(Guid.NewGuid(), folder.Id, "完成ICS实验", DueDate: day.AddDays(30)));
                store.SaveDateEvent(new(Guid.NewGuid(), "考试", day.AddDays(40)));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")) { Width = 1200, Height = 820 }; window.Show(); Flush();
                var editor = new CourseEditorWindow(main) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    ((TextBox)editor.FindName("NameInput")).Text = "示例项目";
                    ((Button)editor.FindName("AddMeetingButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                    var meetings = Descendants<Expander>(editor).Where(item => item.GetType().Name == "CourseMeetingEditor").ToArray(); Assert.Equal(2, meetings.Length);
                    var second = Descendants<ComboBox>((DependencyObject)meetings[1].Content).ToArray(); second[0].SelectedIndex = 0;
                    ((TextBox)editor.FindName("CustomColorInput")).Text = "#3467ab";
                    Assert.Equal(24, ((ComboBox)editor.FindName("ColorSelector")).Items.Count);
                    Save(editor, "course-multiple-meetings.png");
                    ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Assert.True(editor.ShowDialog()); var course = Assert.Single(store.GetCourses()); Assert.Equal(2, course.Meetings!.Count); Assert.Equal("#3467AB", course.Color);
                var secondId = course.Meetings[1].Id; var change = new CourseChangeWindow(main, course, nextMonday, secondId) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { ((DatePicker)change.FindName("NewDateInput")).SelectedDate = nextMonday.AddDays(1).ToDateTime(TimeOnly.MinValue); ((Button)change.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }));
                Assert.True(change.ShowDialog()); Assert.Equal(secondId, Assert.Single(store.GetCourseChanges()).MeetingId);
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("PreferencesTab"); Flush();
                ((Expander)window.FindName("SizeSettingsSection")).IsExpanded = true; Flush();
                var selector = (ComboBox)window.FindName("CardSizeSelector"); Assert.Equal(9, selector.Items.Count);
                var slider = (Slider)window.FindName("CardSizeSlider"); slider.Value = 70; Flush(); Assert.Equal(70, main.CardSizing.Options[0].Percentage);
                tabs.SelectedIndex = 1; Flush(); window.UpdateLayout();
                var projects = (ListBox)window.FindName("ProjectList"); Assert.True(projects.Items.Count > 0);
                var projectCard = (ListBoxItem)projects.ItemContainerGenerator.ContainerFromIndex(0); Assert.InRange(projectCard.ActualHeight, 83, 85); Save(window, "cards-small.png");
                main.CardSizing.Options[0].Percentage = 140; Flush(); window.UpdateLayout(); Assert.InRange(projectCard.ActualHeight, 167, 169); Save(window, "cards-large.png");
                main.CardSizing.Options[0].ResetCommand.Execute(null); Assert.Equal(100, main.CardSizing.Options[0].Percentage);
                main.CardSizing.Options[1].Percentage = 80;
                tabs.SelectedItem = window.FindName("CalendarTab"); main.Calendar.Mode = "月"; main.Calendar.Date = day; Flush(); window.UpdateLayout();
                ((ComboBox)((CalendarView)window.FindName("CalendarPage")).FindName("CalendarYearSelector")).SelectedItem = day.Year - 10; Flush(); Assert.Equal(day.Year - 10, main.Calendar.Date.Year);
                main.Calendar.SelectedMonth = 2; Assert.Equal(2, main.Calendar.Date.Month);
                var picker = (DatePicker)((CalendarView)window.FindName("CalendarPage")).FindName("JumpDate"); picker.IsDropDownOpen = true; Flush();
                var popup = (Popup)picker.Template.FindName("PART_Popup", picker); Assert.IsType<DockPanel>(popup.Child);
                var navigation = Descendants<ComboBox>(popup.Child).ToArray(); Assert.Equal(2, navigation.Length); navigation[0].SelectedItem = day.Year - 10; navigation[1].SelectedItem = 10; Flush();
                var calendar = Descendants<System.Windows.Controls.Calendar>(popup.Child).Single(); Assert.Equal(day.Year - 10, calendar.DisplayDate.Year); Assert.Equal(10, calendar.DisplayDate.Month);
                calendar.SelectedDate = new DateTime(day.Year - 10, 10, 15); Flush(); Assert.Equal(calendar.SelectedDate, picker.SelectedDate); picker.IsDropDownOpen = false;
                main.SoftwareRemindersEnabled = false; main.OutlookSyncRequested = true;
                using (var reopened = new MediaScope(new MainViewModel(new SqliteFocusStore(path))))
                { Assert.False(reopened.Main.SoftwareRemindersEnabled); Assert.True(reopened.Main.OutlookSyncRequested); Assert.Equal(80, reopened.Main.CardSizing.Options[1].Percentage); Assert.Contains("尚未接入", reopened.Main.OutlookSyncStatus); }
                main.SoftwareRemindersEnabled = true;
                service = new(store, main, _ => { }); var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
                Assert.True(notifier.GetScheduledToastNotifications().Count(item => item.Group == "FocusBloom") >= 4, main.NotificationStatus);
                main.SoftwareRemindersEnabled = false; service.Reconcile(); Assert.DoesNotContain(notifier.GetScheduledToastNotifications(), item => item.Group == "FocusBloom"); Assert.Equal("[]", store.GetSetting("notifications.pending"));
                main.SoftwareRemindersEnabled = true; service.Reconcile(); Assert.True(notifier.GetScheduledToastNotifications().Count(item => item.Group == "FocusBloom") >= 4, main.NotificationStatus);
                main.SoftwareRemindersEnabled = false; service.Reconcile(); Assert.Single(store.GetCourses()); Assert.True(course.Reminder?.Enabled ?? true);
            }
            finally
            {
                service?.Dispose(); if (service is not null) ToastNotificationManagerCompat.Uninstall();
                foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(dialog => dialog != window).ToArray()) dialog.Close();
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    [Fact]
    public void FocusTimelineGroupsPausesShowsOutcomesAndDoubleDaySplitsAtNoon()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomTimelineUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var day = DateOnly.FromDateTime(DateTime.Today); var project = store.GetProjects()[0];
                DateTimeOffset At(int hour, int minute) { var local = day.ToDateTime(new(hour, minute)); return new(local, TimeZoneInfo.Local.GetUtcOffset(local)); }
                var record = new FocusRecord(Guid.NewGuid(), project.Id, TimerMode.CountUp, At(11, 30), At(13, 30), EndReason.Manual,
                    new[] { new FocusInterval(IntervalKind.Focus, At(11, 30), At(12, 0)), new FocusInterval(IntervalKind.Pause, At(12, 0), At(12, 10)), new FocusInterval(IntervalKind.Focus, At(12, 10), At(13, 30)) }, Strict: true, InterceptionCount: 3);
                store.SaveRecord(record); store.SaveOutcome(new(record.Id, "完成实验", NextStep: "整理笔记"));
                store.SaveCalendarPlan(new(Guid.NewGuid(), "跨中午安排", day, new(11, 30), 120, ProjectId: project.Id));
                store.SaveCourse(new(Guid.NewGuid(), "不应出现的课程", (int)day.DayOfWeek == 0 ? 7 : (int)day.DayOfWeek, 1, 1));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")) { Width = 1200, Height = 820 }; window.Show();
                var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedItem = window.FindName("RecordsTab"); main.FocusCalendar.Date = day; main.FocusCalendar.Mode = "时间轴"; Flush(); window.UpdateLayout();
                var focus = (CalendarView)window.FindName("FocusCalendarPage"); focus.Render(); Flush();
                Assert.InRange(((FrameworkElement)focus.FindName("CalendarToolbar")).ActualHeight, 60, 84);
                Assert.Single(Descendants<Button>(focus), button => button.Tag is CalendarEntry entry && entry.Kind == "专注");
                var texts = Descendants<TextBlock>(focus).Select(text => text.Text).ToArray(); Assert.Contains("1 次专注 · 累计 1小时50分钟", texts); Assert.Contains("成果 · 完成实验", texts); Assert.Contains("严格专注 · 拦截 3 次", texts);
                Save(window, "focus-record-timeline-light.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); focus.Render(); Save(window, "focus-record-timeline-dark.png");
                main.FocusCalendar.Mode = "日"; main.FocusCalendar.DayLayout = "双列"; Flush(); focus.Render(); Flush();
                Assert.Equal(2, Descendants<Canvas>(focus).Count());
                var planParts = Descendants<Button>(focus).Where(button => button.Tag is CalendarEntry entry && entry.Kind == "安排").ToArray(); Assert.Equal(2, planParts.Length); Assert.Equal(planParts[0].Tag is CalendarEntry first ? first.Id : Guid.Empty, ((CalendarEntry)planParts[1].Tag).Id);
                Assert.All(planParts, button => { Assert.InRange(Canvas.GetTop(button), 0, 576); Assert.InRange(button.Width, 20, 550); }); Save(window, "focus-day-double-dark.png");
                main.FocusCalendar.DayLayout = "单列"; Flush(); focus.Render(); Assert.Single(Descendants<Canvas>(focus));
                tabs.SelectedItem = window.FindName("CalendarTab");
                foreach (var mode in new[] { "日", "周", "月", "年" }) { main.Calendar.Mode = mode; Assert.DoesNotContain(main.Calendar.Entries(day, day), entry => entry.Kind is "课程" or "专注" or "安排"); }
                main.Calendar.Mode = "日"; Flush(); window.UpdateLayout();
                var taskCalendar = (CalendarView)window.FindName("CalendarPage");
                Assert.InRange(((FrameworkElement)taskCalendar.FindName("CalendarToolbar")).ActualHeight, 60, 84);
                Save(window, "calendar-compact-toolbar.png");
                Assert.NotNull(main.GetRecord(record.Id));
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }
    private sealed class MediaScope(MainViewModel main) : IDisposable { public MainViewModel Main => main; public void Dispose() => main.Media.Dispose(); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(content.RenderSize); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(content), null, bounds); } bitmap.Render(visual);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
}
