using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class NavigationSettingsTests
{
    [Fact]
    public void EnglishUsesTranslatedNavigationAndTimerWithoutRenamingProjects()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomLanguage", Guid.NewGuid().ToString("N"));
            MainWindow? window = null;
            MainViewModel? model = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db"));
                var project = new FocusBloom.Core.Projects.FocusProject(Guid.NewGuid(), "阅读", "学习", "#718578");
                store.SaveProject(project);
                store.SaveSetting(UiText.SettingKey, "en");
                UiText.Configure(store.GetSetting(UiText.SettingKey));
                var clock = new ManualClock();
                model = new MainViewModel(store, new FocusTimer(clock));
                window = new MainWindow(model, store, Path.Combine(root, "browser"));
                window.Show(); Flush();
                Assert.Equal("Focus Timer", model.Navigation.Items.Single(item => item.Key == "focus").Name);
                Assert.Equal("Ready", model.StateText);
                Assert.Equal("阅读", model.Projects.Single(item => item.Id == project.Id).Name);
                Assert.Equal("A moment for yourself", ((TextBlock)window.FindName("FocusPageTitle")).Text);
                Assert.Equal("Save focus screenshot", ((Button)window.FindName("FocusScreenshotButton")).ToolTip);
                model.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush();
                Assert.Equal("About", ((Expander)window.FindName("AboutSettingsSection")).Header);
                Assert.Equal("en", ((ComboBox)window.FindName("LanguageSelector")).SelectedValue);
                Assert.Equal("语言/Language", ((Expander)window.FindName("LanguageSettingsSection")).Header);
                Assert.Contains(model.Media.BackgroundChoices, item => item.Name == "Lakeside Dusk");
                Assert.Contains(model.Media.SoundChoices, item => item.Name == "Light Rain · Field Recording");
                Assert.DoesNotContain(model.CardSizing.Options, option => System.Text.RegularExpressions.Regex.IsMatch(
                    option.Name + option.PreviewTitle + option.PreviewDetail + option.PreviewDescription, @"[\p{IsCJKUnifiedIdeographs}]"));
                Assert.Equal("All projects", model.Statistics.SelectedProject!.Name);
                Assert.Equal("All tasks", model.Statistics.SelectedTodo!.Name);
                var goal = new FocusBloom.Core.Goals.FocusGoal(Guid.NewGuid(), "阅读", FocusBloom.Core.Goals.GoalMetric.Sessions,
                    5, FocusBloom.Core.Goals.GoalCycle.Weekly, new DateOnly(2026, 11, 2));
                var row = new GoalRow(FocusBloom.Core.Goals.GoalRules.Progress(goal, [], [], new DateOnly(2026, 11, 5)), "阅读");
                Assert.StartsWith("Weekly", row.Period);
                Assert.Equal("Remaining: 5 sessions", row.State);
                Assert.Equal("阅读", row.Name);
                var audit = new List<string>();
                foreach (var page in model.Navigation.VisibleItems.ToArray())
                {
                    page.OpenCommand.Execute(null); Flush();
                    audit.AddRange(Descendants<TextBlock>(window).Where(text => text.IsVisible &&
                        System.Text.RegularExpressions.Regex.IsMatch(text.Text, @"[\p{IsCJKUnifiedIdeographs}]"))
                        .Select(text => page.Key + " · " + text.Text));
                }
                var auditPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa/english-page-audit.txt"));
                File.WriteAllLines(auditPath, audit.Distinct());
                model.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush();
                model.SetCustomPalette("#336688");
                Assert.Equal("custom", model.SelectedPalette.Id);
                Assert.Equal("#336688", store.GetSetting("appearance.customPalette"));
                Save(window, "settings-english.png");
                var screenshotFolder = Path.Combine(root, "screenshots");
                window.Backups!.ScreenshotDirectory = screenshotFolder;
                Assert.Equal(screenshotFolder, store.GetSetting("files.screenshotDirectory"));
                model.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); Flush();
                var todos = (TodosView)window.FindName("TodoPage");
                ((Button)todos.FindName("PeriodicChecklistButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                var tab = (TabItem)window.FindName("PlannerTab");
                Assert.Same(tab, ((TabControl)window.FindName("MainTabs")).SelectedItem);
                var planner = Assert.IsType<PlannerView>(tab.Content);
                var content = (DockPanel)planner.Content;
                var draft = Descendants<TextBox>(content).Single(input => input.Name == "PlannerDraft");
                var date = Descendants<DatePicker>(content).Single();
                var add = Descendants<Button>(content).Single(button => button.Name == "PlannerAdd");
                var day = DateTime.Today.AddDays(-3);
                date.SelectedDate = day; Flush();
                draft.Text = "阅读";
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                var check = Descendants<CheckBox>(content).Single(box => box.Content is TextBlock { Text: "阅读" });
                check.IsChecked = true;
                check.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); Flush();
                date.SelectedDate = day.AddDays(1); Flush();
                Assert.DoesNotContain(Descendants<CheckBox>(content), box => box.Content is TextBlock { Text: "阅读" });
                draft.Text = "Draft for this day";
                date.SelectedDate = day; Flush();
                var completed = Descendants<CheckBox>(content).Single(box => box.Content is TextBlock { Text: "阅读" });
                Assert.True(completed.IsChecked);
                Assert.Contains(((TextBlock)completed.Content).TextDecorations, decoration => decoration.Location == TextDecorationLocation.Strikethrough);
                var calendar = Descendants<Calendar>(content).Single();
                Assert.Equal("en-us", calendar.Language.IetfLanguageTag);
                calendar.SelectedDate = day.AddDays(1); Flush();
                Assert.Equal(day.AddDays(1), date.SelectedDate);
                Assert.Equal("Draft for this day", draft.Text);
                model.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null);
                model.Navigation.Items.Single(item => item.Key == "planner").OpenCommand.Execute(null); Flush();
                Assert.Equal(day.AddDays(1), date.SelectedDate);
                date.SelectedDate = day; Flush();
                Save(window, "planner-page-english.png");
                Appearance.Apply(model.SelectedPalette, AppearanceMode.Dark); Flush();
                var calendarItem = (System.Windows.Controls.Primitives.CalendarItem)calendar.Template.FindName("PART_CalendarItem", calendar);
                var calendarSurface = (Border)calendarItem.Template.FindName("PART_Root", calendarItem);
                Assert.Equal(((SolidColorBrush)window.FindResource("CardBrush")).Color, ((SolidColorBrush)calendarSurface.Background).Color);
                model.RefreshSystemTheme(); Flush();
                var focus = Descendants<Button>(content).Single(button => Equals(button.ToolTip, "Start focus"));
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var dialog = window.OwnedWindows.OfType<FocusBloom.Desktop.Windows.TodoStartWindow>().Single();
                    ((Button)dialog.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (dialog.IsVisible) dialog.Close();
                }));
                focus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                Assert.True(model.IsFocusActive);
                Assert.Equal("阅读", model.FocusTodoText);
                Assert.Same(window.FindName("FocusTab"), ((TabControl)window.FindName("MainTabs")).SelectedItem);
                ((Button)window.FindName("FocusScreenshotButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                var screenshot = Assert.Single(Directory.GetFiles(screenshotFolder, "*.png"));
                var toast = (Border)window.FindName("ScreenshotToast");
                Assert.True(toast.IsVisible);
                Assert.Contains(screenshot, ((TextBlock)toast.Child).Text);
                var expiry = new DispatcherFrame();
                var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.5) };
                deadline.Tick += (_, _) => { deadline.Stop(); expiry.Continue = false; };
                deadline.Start();
                Dispatcher.PushFrame(expiry);
                Assert.False(toast.IsVisible);
                clock.Advance(TimeSpan.FromSeconds(12));
                model.EndCommand.Execute(null);
                var savedRecord = Assert.Single(store.GetRecords());
                Assert.Null(savedRecord.TodoId);
                Assert.NotNull(savedRecord.PlannerItemId);
                Assert.Equal("阅读", savedRecord.PlannerTitle);
                Assert.Equal("阅读", Assert.Single(model.Records).Todo);
                Assert.Equal(savedRecord.PlannerItemId, Assert.Single(new SqliteFocusStore(Path.Combine(root, "test.db")).GetRecords()).PlannerItemId);
                var scope = FocusBloom.Core.Todos.ChecklistScope.For(FocusBloom.Core.Todos.ChecklistPeriod.Day, DateOnly.FromDateTime(day));
                Assert.True(Assert.Single(model.Todos.Checklist.Read(scope)).Done);
                model.Navigation.Items.Single(item => item.Key == "planner").OpenCommand.Execute(null); Flush();
                var edit = Descendants<Button>(content).Single(button => Equals(button.ToolTip, "Edit plan item"));
                window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                {
                    var editor = window.OwnedWindows.OfType<FocusBloom.Desktop.Windows.PlannerItemEditorWindow>().Single();
                    Descendants<TextBox>(editor).Single(input => input.Name == "PlannerEditInput").Text = "阅读第四章";
                    Descendants<Button>(editor).Single(button => button.Name == "PlannerSaveEdit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (editor.IsVisible) editor.Close();
                }));
                edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                var edited = Assert.Single(model.Todos.Checklist.Read(scope));
                Assert.Equal(savedRecord.PlannerItemId, edited.Id);
                Assert.Equal("阅读第四章", edited.Text);
                Assert.True(edited.Done);
                Assert.Equal(edited.Id, Assert.Single(store.GetRecords()).PlannerItemId);
                model.Statistics.Anchor = clock.GetLocalNow().DateTime.Date;
                Assert.Contains("\"阅读\"", model.Statistics.Export(csv: true));

                var remove = Descendants<Button>(content).Single(button => Equals(button.ToolTip, "Delete"));
                void RespondToDeletion(bool confirm)
                {
                    window.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                    {
                        var dialog = window.OwnedWindows.OfType<FocusBloom.Desktop.Windows.ConfirmDeletionWindow>().Single();
                        if (confirm)
                            ((Button)dialog.FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        else
                            dialog.Close();
                    }));
                    remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush();
                }
                RespondToDeletion(false);
                Assert.Equal(edited, Assert.Single(model.Todos.Checklist.Read(scope)));
                RespondToDeletion(true);
                Assert.Empty(model.Todos.Checklist.Read(scope));
                Assert.Equal(edited.Id, Assert.Single(store.GetRecords()).PlannerItemId);
            }
            finally
            {
                window?.DisposeBrowser(); window?.Close(); model?.Media.Dispose();
                UiText.Configure(null);
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    [Fact]
    public void NavigationVisibilityDragOrderAndSettingsSearchPersistAndKeepTimerRunning()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomNavigationSettings", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var path = Path.Combine(directory, "test.db"); var store = new SqliteFocusStore(path); var clock = new ManualClock(); main = new(store, new FocusTimer(clock));
                window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Flush(); var tabs = (TabControl)window.FindName("MainTabs");
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush(); Assert.Same(window.FindName("PreferencesTab"), tabs.SelectedItem);
                var groups = new[] { "NavigationSettingsSection", "AppearanceSettingsSection", "SizeSettingsSection", "TimerSettingsSection", "FloatSettingsSection", "FontSettingsSection", "MediaSettingsSection", "ReminderSettingsSection", "RestrictionSettingsSection" }.Select(name => (Expander)window.FindName(name)).ToArray();
                Assert.All(groups, group => Assert.False(group.IsExpanded)); Save(window, "settings-folded-sections.png");
                groups[0].IsExpanded = true; Flush(); var settingsItems = (ItemsControl)window.FindName("NavigationSettingsItems");
                var record = main.Navigation.Items.Single(item => item.Key == "records");
                var recordCheck = Descendants<CheckBox>(settingsItems).Single(box => box.DataContext == record); recordCheck.IsChecked = false; Flush();
                var sidebar = (ItemsControl)tabs.Template.FindName("NavigationItems", tabs); Assert.Equal(9, sidebar.Items.Count);
                Assert.False(Descendants<CheckBox>(settingsItems).Single(box => box.DataContext is NavigationOption item && item.Key == "settings").IsEnabled);
                var handle = Descendants<ReorderButton>(settingsItems).Single(button => button.DataContext == record);
                handle.RaiseEvent(new ReorderEventArgs(OrderMove.First)); Flush(); Assert.Same(record, main.Navigation.Items[0]); recordCheck.IsChecked = true; Flush(); Assert.Same(record, sidebar.Items[0]);
                record.OpenCommand.Execute(null); Flush(); Assert.Same(window.FindName("RecordsTab"), tabs.SelectedItem); Assert.True(record.Active); Assert.Equal(5, tabs.SelectedIndex);
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null);
                main.Navigation.Items.Single(item => item.Key == "focus").Visible = false; Assert.True(main.IsFocusActive);
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); Flush();
                var search = (TextBox)window.FindName("SettingsSearchInput"); search.Text = "字体"; Flush();
                Assert.True(groups[5].IsExpanded); Assert.Equal(Visibility.Visible, groups[5].Visibility); Assert.All(groups.Where(group => group != groups[5]), group => Assert.Equal(Visibility.Collapsed, group.Visibility));
                var interfaceSection = (Expander)window.FindName("InterfaceFontSettingsSection");
                Assert.True(interfaceSection.IsExpanded);
                var interfaceSelector = (ComboBox)window.FindName("InterfaceFontSelector");
                var focusFont = main.Typography.Chinese;
                var interfaceFont = main.Typography.InterfaceFonts[1];
                interfaceSelector.SelectedItem = interfaceFont; Flush();
                Assert.Equal(interfaceFont.Family, ((TextBlock)window.FindName("SettingsPageTitle")).FontFamily.Source);
                ((Slider)window.FindName("InterfaceSizeSelector")).Value = 120;
                main.Typography.FocusSizePercent = 80; Flush();
                Assert.Equal(26.4, ((TextBlock)window.FindName("SettingsPageTitle")).FontSize, 3);
                Assert.Equal(57.6, ((TextBlock)window.FindName("TimerDisplay")).FontSize, 3);
                Assert.Equal(interfaceFont.Family, store.GetSetting("appearance.interfaceFont"));
                Assert.Same(focusFont, main.Typography.Chinese);
                Assert.Null(store.GetSetting("ui.settings.expanded.fonts")); Save(window, "settings-search-fonts.png");
                search.Text = "图标"; Flush();
                var about = (Expander)window.FindName("AboutSettingsSection");
                Assert.True(about.IsExpanded);
                search.Text = ""; Flush(); Assert.False(groups[5].IsExpanded); Assert.True(groups[0].IsExpanded);
                Assert.Same(about, ((Panel)about.Parent).Children.OfType<Expander>().Last(section => section.Visibility == Visibility.Visible));
                groups[5].IsExpanded = true; groups[2].IsExpanded = true; main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); Save(window, "settings-navigation-dark.png");
                clock.Advance(TimeSpan.FromSeconds(12)); main.EndCommand.Execute(null); Assert.Single(store.GetRecords());
                window.DisposeBrowser(); window.Close(); main.Media.Dispose(); main = new(new SqliteFocusStore(path)); window = new(main, store, Path.Combine(directory, "browser-reopened")); window.Show(); Flush();
                Assert.Equal("records", main.Navigation.Items[0].Key); Assert.False(main.Navigation.Items.Single(item => item.Key == "focus").Visible); Assert.True(main.Navigation.Items.Single(item => item.Key == "settings").Visible);
                Assert.Equal(interfaceFont.Family, main.Typography.InterfaceFont.Family);
                Assert.Equal(120, main.Typography.InterfaceSizePercent);
                Assert.Equal(80, main.Typography.FocusSizePercent);
                Assert.True(((Expander)window.FindName("FontSettingsSection")).IsExpanded); Assert.True(((Expander)window.FindName("SizeSettingsSection")).IsExpanded); Assert.False(((Expander)window.FindName("TimerSettingsSection")).IsExpanded);
                main.Navigation.Items.Single(item => item.Key == "focus").Visible = true; main.Navigation.Move(main.Navigation.Items.Single(item => item.Key == "settings"), OrderMove.First);
                var reopened = new NavigationViewModel(new SqliteFocusStore(path)); Assert.Equal("settings", reopened.Items[0].Key); Assert.Equal(10, reopened.VisibleItems.Count);
            }
            finally { window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    [Fact]
    [Trait("DesktopMode", "Interactive")]
    public void HoveringBarsAndHeatmapShowsExactTotalWithoutClicking()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStatisticsHover", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null; GetCursorPos(out var original);
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var date = DateOnly.FromDateTime(DateTime.Today); var local = date.ToDateTime(new(10, 0)); var start = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)); var end = start.AddMinutes(3).AddSeconds(22);
                store.SaveRecord(new(Guid.NewGuid(), store.GetProjects()[0].Id, TimerMode.CountUp, start, end, EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, start, end) }));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")) { Topmost = true }; window.Show(); window.Activate(); Flush();
                ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("RecordsTab"); main.RecordPage = "统计"; main.Statistics.Range = "日"; Flush(); window.UpdateLayout();
                var page = (StatisticsView)window.FindName("StatisticsPage"); page.Render(); Flush();
                var bar = Descendants<Button>(page).First(button => button.Tag is StatisticsBucket bucket && bucket.Duration > TimeSpan.Zero); Hover(bar); ValidateTip(bar);
                var heat = Descendants<Button>(page).Single(button => button.Tag is DateOnly day && day == date); heat.BringIntoView(); Flush(); Hover(heat); ValidateTip(heat);
                Save(window, "statistics-hover-heatmap.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark); Flush(); page.Render(); Flush();
                heat = Descendants<Button>(page).Single(button => button.Tag is DateOnly day && day == date); heat.BringIntoView(); Flush(); Hover(heat); ValidateTip(heat); Save(window, "statistics-hover-dark.png");
            }
            finally { SetCursorPos(original.X, original.Y); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    private static void Hover(Button target)
    {
        var tooltip = Assert.IsType<ToolTip>(target.ToolTip); tooltip.IsOpen = false;
        var point = target.PointToScreen(new Point(target.ActualWidth / 2, target.ActualHeight / 2)); SetCursorPos((int)point.X, (int)point.Y);
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) }; var polls = 0;
        timer.Tick += (_, _) => { if (tooltip.IsOpen || ++polls >= 70) { timer.Stop(); frame.Continue = false; } }; timer.Start(); Dispatcher.PushFrame(frame); Assert.True(tooltip.IsOpen, "鼠标悬停应自动显示统计提示。");
    }
    private static void ValidateTip(Button button)
    { var tip = Assert.IsType<ToolTip>(button.ToolTip); Assert.Contains(Descendants<TextBlock>(tip), label => label.Text == "总专注 00:03:22"); tip.IsOpen = false; }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
    private static void Save(Window window, string name)
    {
        var content = (FrameworkElement)window.Content; content.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen()) { var bounds = new Rect(content.RenderSize); drawing.DrawRectangle(window.Background, null, bounds); drawing.DrawRectangle(new VisualBrush(content), null, bounds); } bitmap.Render(visual);
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var file = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(file);
    }
}
