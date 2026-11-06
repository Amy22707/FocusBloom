using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;
using FocusBloom.Core.Restrictions;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class OfflineShowcaseTests
{
    private const string FocusQuote = "执着于理想，纯粹于当下。";

    public sealed class ShowcaseFactAttribute : FactAttribute
    {
        public ShowcaseFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("FOCUSBLOOM_CAPTURE_SHOWCASE") != "1")
                Skip = "Showcase capture requires the offline build and the showcase background asset.";
        }
    }
    private sealed class ShowcaseClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [ShowcaseFact]
    public void CaptureOfflineEditionWithFictionalWorkspace()
    {
        WpfTestHost.Run(() =>
        {
            var temp = Path.GetFullPath(Path.GetTempPath());
            var root = Path.GetFullPath(Path.Combine(temp, $"FocusBloomShowcase-{Guid.NewGuid():N}"));
            Assert.Equal(Path.TrimEndingDirectorySeparator(temp), Path.GetDirectoryName(root));
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/showcase"));
            var art = Path.Combine(output, "backgrounds", "湖畔暮色.png");
            var store = new SqliteFocusStore(Path.Combine(root, "demo.db"));
            var (project, folder, todo) = Seed(store, art);
            var clock = new ShowcaseClock();
            var main = new MainViewModel(store, new FocusTimer(clock));
            var window = new MainWindow(main, store, Path.Combine(root, "browser")) { Width = 1440, Height = 900 };
            try
            {
                main.SelectedPalette = main.Palettes.Single(item => item.Id == "sage");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light);
                main.Typography.Timer = main.Typography.TimerFonts.Single(font => font.Family == "Consolas");
                var candidates = main.Media.Images.Where(item => item.Asset.ManualOnly).ToArray();
                Assert.Equal(new[] { "湖畔暮色", "雨窗书桌" }, candidates.Select(item => item.Name));
                Assert.All(candidates, item => { Assert.True(item.Excluded); item.Excluded = false; Assert.True(item.Excluded); });
                main.Media.SelectedBackground = main.Media.BackgroundChoices.Single(asset => asset.Id == "lake-blue-hour");
                var quote = Path.Combine(root, "showcase-quote.txt");
                File.WriteAllText(quote, FocusQuote);
                main.Media.ImportQuotes(quote);
                main.Media.SelectedQuote = main.Media.QuoteChoices.Single(item => item.Text == FocusQuote);
                window.Show(); Flush();
                window.Width = 1280; window.Height = 820; Flush();
                Assert.Null(window.Accounts); Assert.Equal("一刻专注 · FocusBloom", window.Title);
                if (main.Typography.ChineseFonts.Any(item => item.Family == "KaiTi"))
                {
                    Assert.Equal("KaiTi", main.Typography.Chinese.Family);
                    Assert.Contains("KaiTi", ((TextBlock)window.FindName("FocusQuoteDisplay")).FontFamily.Source);
                }
                Assert.Equal(main.Typography.Timer.Family, ((TextBlock)window.FindName("TimerDisplay")).FontFamily.Source);
                Assert.False(((FrameworkElement)window.FindName("AccountSettingsSection")).IsVisible);
                main.SelectedFolder = main.Folders.Single(item => item.Name == project.Category);
                var focusProject = main.FolderProjects.Single(item => item.Name == "阅读");
                main.SelectedProject = focusProject;
                main.SelectedTimerMode = main.TimerModes.Single(item => item.Mode == TimerMode.Countdown);
                main.DurationMinutes = "25";
                Open("focus");
                Assert.True(main.Media.HasBackground);
                Shot(window, "01-focus");
                main.StartCommand.Execute(null); Assert.Equal(FocusState.Focusing, main.State);
                clock.Now += TimeSpan.FromMinutes(3); main.Tick();
                Click(window, "EnterFullscreenButton"); Shot(window, "02-immersive");
                var fullscreen = (FocusBloom.Desktop.Views.FullscreenFocusView)window.FindName("FullscreenView");
                var capture = (BitmapSource)fullscreen.GetType().GetMethod("CaptureScene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(fullscreen, null)!;
                Assert.True(capture.PixelWidth >= 900);
                Assert.Equal(1d, ((FrameworkElement)fullscreen.FindName("FocusActions")).Opacity);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(capture));
                using (var file = File.Create(Path.Combine(output, "screenshots", "28-focus-export.png"))) png.Save(file);
                Click(window, "ExitContentFullscreenButton");
                Assert.True(((TabControl)window.FindName("MainTabs")).ActualHeight > 400);
                main.PrepareExit();

                Open("projects"); main.ProjectLibraryScope = "全部文件夹"; Shot(window, "03-projects");
                var editor = new ProjectEditorWindow(main, project) { Owner = window };
                editor.Show(); Shot(editor, "04-project-plan"); editor.Close();
                var newProject = new ProjectEditorWindow(main, false) { Owner = window };
                ((ComboBox)newProject.FindName("PresetSelector")).SelectedIndex = 1;
                newProject.Show(); Shot(newProject, "24-project-preset"); newProject.Close();

                Open("todos"); main.Todos.SelectedFolder = main.Todos.Folders.Single(item => item.Id == folder.Id);
                main.Todos.ViewMode = "列表"; Shot(window, "05-todos");
                main.Todos.ViewMode = "四象限"; Shot(window, "06-quadrants");
                var details = new TodoDetailsWindow(main, main.Todos.Items.Single(item => item.Id == todo.Id)) { Owner = window, Height = 610 };
                details.Show(); Shot(details, "07-todo-detail");
                var step = ((StackPanel)details.FindName("DetailSteps")).Children.OfType<CheckBox>().Last();
                step.IsChecked = true; step.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Assert.True(store.GetTodos().Single(item => item.Id == todo.Id).Steps!.Last().Completed);
                Assert.Equal(TodoStatus.InProgress, store.GetTodos().Single(item => item.Id == todo.Id).Status);
                details.Close();

                Open("planner");
                Shot(window, "29-planner");

                Open("calendar"); main.Calendar.Mode = "周"; Shot(window, "08-calendar");
                main.Calendar.Mode = "课程表"; Shot(window, "09-timetable");
                var semesters = new SchoolSemestersWindow(main) { Owner = window, Height = 710 };
                semesters.Show(); Shot(semesters, "25-semesters"); semesters.Close();
                Open("dates"); Shot(window, "10-date-events");
                Open("records"); main.RecordPage = "时间轴"; Shot(window, "11-records");
                main.RecordPage = "统计"; Shot(window, "12-statistics");
                main.RecordPage = "复盘"; Shot(window, "13-review");
                Open("goals"); Shot(window, "14-habits");
                Open("reflection"); main.Reflection.ViewMode = "月视图"; Shot(window, "15-mood-calendar");
                main.Reflection.ViewMode = "每日记录"; Shot(window, "16-diary");
                Open("settings");
                Setting("BackupSettingsSection", "17-backup");
                Setting("MediaSettingsSection", "18-backgrounds");
                Setting("RestrictionSettingsSection", "19-restrictions");
                Setting("ReminderSettingsSection", "20-reminders");
                Setting("AboutSettingsSection", "27-about");
                main.TimerFloatVisible = true; Flush();
                Shot(Application.Current.Windows.OfType<TimerFloatWindow>().Single(), "21-floating-timer");
                main.TimerFloatVisible = false;
                var inbox = new ReminderInboxWindow(main, _ => { }) { Owner = window };
                inbox.Show(); Shot(inbox, "22-reminder-inbox"); inbox.Close();
                Open("focus");
                Click(window, "OpenStudyBrowserButton"); Shot(window, "23-study-browser");
                Open("focus");
                foreach (var option in main.Widgets.Items) option.Enabled = true;
                Flush();
                Assert.Equal(6, window.DesktopWidgets.Windows.Count);
                foreach (var widget in window.DesktopWidgets.Windows.Values) Shot(widget, "widget-" + widget.Option.Kind.ToString().ToLowerInvariant());
                window.CloseDesktopWidgetsForExit();
                Open("focus");
                main.SelectedProject = focusProject;
                main.SelectedTimerMode = main.TimerModes.Single(item => item.Mode == TimerMode.Countdown);
                main.DurationMinutes = "25";
                main.Media.SelectedBackground = main.Media.BackgroundChoices.Single(asset => asset.Id == "rain-desk");
                main.StartCommand.Execute(null); Assert.Equal(FocusState.Focusing, main.State);
                Assert.Equal("rain-desk", main.Media.CurrentBackgroundId);
                clock.Now += TimeSpan.FromMinutes(1); main.Tick();
                Click(window, "EnterFullscreenButton"); Shot(window, "26-rain-desk");
                Click(window, "ExitContentFullscreenButton"); main.PrepareExit();
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally
            {
                window.CloseDesktopWidgetsForExit(); window.DisposeBrowser(); window.Close(); main.Media.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                // These files belong to this newly created, isolated demo profile only.
                try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
                catch (IOException) { /* WebView2 may still be releasing its private cache after disposal. */ }
            }
            void Open(string key) { main.Navigation.Items.Single(item => item.Key == key).OpenCommand.Execute(null); Flush(); }
            void Setting(string name, string file)
            {
                var section = (Expander)window.FindName(name);
                section.IsExpanded = true; Flush();
                if (name == "MediaSettingsSection" && window.FindName("FocusMediaSettingsCard") is Border { Child: FrameworkElement media }
                    && media.FindName("BackgroundLibrary") is Expander backgrounds)
                    backgrounds.IsExpanded = true;
                Flush(); section.BringIntoView(); Shot(window, file);
                section.IsExpanded = false; Flush();
            }
        });
    }

    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Shot(Window window, string name) { Flush(); GoalsUiTests.Save(window, "../showcase/screenshots/" + name + ".png"); }

    private static (FocusProject Project, TodoFolder Folder, TodoItem Todo) Seed(SqliteFocusStore store, string art)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        store.SaveSetting("restrictions.applications", JsonSerializer.Serialize(new[]
        {
            new AllowedApplication("Microsoft Edge", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
            new AllowedApplication("计算器", @"C:\Windows\System32\calc.exe")
        }));
        store.SaveSetting("restrictions.websites", JsonSerializer.Serialize(new[]
        {
            new WebsiteRule("docs.python.org", true), new WebsiteRule("wikipedia.org", true)
        }));
        var monday = CalendarRules.Monday(today);
        var project = new FocusProject(Guid.NewGuid(), "高等数学", "学习", "#789989",
            new("完成本周作业并复习积分", today.AddDays(13), [new("复习课堂例题", true), new("完成课后习题"), new("整理错题")]));
        store.SaveProject(project);
        foreach (var (name, category, color) in new[] { ("阅读", "学习", "#AE91B8"), ("Python 练习", "学习", "#879DB1"), ("政治课展示", "学习", "#D7AB7B"), ("线代复习", "学习", "#B79AAF"), ("小组作业", "课程", "#9EA884") })
            store.SaveProject(new(Guid.NewGuid(), name, category, color));
        var folder = new TodoFolder(Guid.NewGuid(), "本周学习", "日常"); store.SaveTodoCategory("日常"); store.SaveTodoFolder(folder);
        var todo = new TodoItem(Guid.NewGuid(), folder.Id, "高数作业", TodoStatus.InProgress, TodoPriority.High, monday.AddDays(2), new(20, 0),
            "完成第三章课后题。", 80, "检查最后两道积分题。", PreferredProjectId: project.Id, Important: true, Urgent: true,
            Steps: [new("复习课上例题", true), new("完成课后习题"), new("检查答案")]);
        store.SaveTodo(todo);
        store.SaveCalendarPlan(new(Guid.NewGuid(), todo.Title, monday.AddDays(2), new(15, 0), 80, TodoId: todo.Id));
        foreach (var (title, day, hour, minutes) in new[]
        {
            ("小组开会", 0, 15, 60), ("和导师讨论", 1, 10, 60), ("Python 习题", 3, 14, 90),
            ("政治课展示", 4, 10, 60), ("线代考试", 4, 16, 120), ("整理课程笔记", 5, 14, 60)
        })
        {
            var task = new TodoItem(Guid.NewGuid(), folder.Id, title, EstimatedMinutes: minutes);
            store.SaveTodo(task);
            store.SaveCalendarPlan(new(Guid.NewGuid(), title, monday.AddDays(day), new(hour, 0), minutes, TodoId: task.Id));
        }
        store.SaveTodo(new(Guid.NewGuid(), folder.Id, "提交英语作业", TodoStatus.Completed, DueDate: monday));
        new PeriodicChecklist(store).Save(ChecklistScope.For(ChecklistPeriod.Day, today),
        [
            new(Guid.NewGuid(), "完成高数第三章作业", true),
            new(Guid.NewGuid(), "做两道 Python 习题", false),
            new(Guid.NewGuid(), "和导师讨论论文选题", false),
            new(Guid.NewGuid(), "准备政治课展示提纲", false),
            new(Guid.NewGuid(), "睡前阅读二十分钟", false)
        ]);
        store.SaveDateEvent(new(Guid.NewGuid(), "小组报告截止", today.AddDays(13), Group: "课程", CoverPath: art));
        store.SaveDateEvent(new(Guid.NewGuid(), "英语口语考试", today.AddDays(8), Group: "课程"));
        store.SaveDateEvent(new(Guid.NewGuid(), "开学", today.AddDays(-32), Mode: DateCountMode.CountUp, Group: "生活"));
        store.SaveDateEvent(new(Guid.NewGuid(), "周末图书馆", today.AddDays(3), Group: "生活"));
        store.SaveCalendarPlan(new(Guid.NewGuid(), "复习积分例题", today, new(16, 0), 75, TodoId: todo.Id, ProjectId: project.Id));
        store.SaveSchoolTerm(SchoolTerm.Default(today.AddDays(-28)));
        foreach (var (name, day, start, end, room, color) in new[] { ("高等数学", 1, 2, 3, "教学楼 204", "#A6BBD4"), ("大学英语", 2, 4, 5, "教学楼 301", "#D8B397"), ("Python 程序设计", 3, 7, 8, "机房 B", "#AAC7AA"), ("思想政治理论课", 4, 2, 3, "教学楼 102", "#BAA9CD"), ("线性代数", 5, 6, 8, "教学楼 206", "#D4C49D") })
            store.SaveCourse(new(Guid.NewGuid(), name, day, start, end, Room: room, Color: color));
        store.RenameSchoolSemester($"{today.Year} 秋季");
        var activeSemester = store.GetSchoolSemesters().Single(item => item.Active);
        store.CreateSchoolSemester($"{today.Year} 春季", new(today.Year, 3, 1));
        store.SaveCourse(new(Guid.NewGuid(), "大学物理", 2, 2, 3));
        store.SelectSchoolSemester(activeSemester.Id);
        var projects = store.GetProjects().Where(item => item.Id != SqliteFocusStore.DefaultProjectId).ToArray();
        for (var i = 0; i < 26; i++)
        {
            var at = new DateTimeOffset(DateTime.Today.AddDays(-(i / 3)).AddHours(9 + i % 3 * 2));
            var end = at.AddMinutes(25 + (i * 7) % 39);
            var id = Guid.NewGuid();
            store.SaveRecord(new(id, i % 3 == 0 ? project.Id : projects[i % projects.Length].Id, TimerMode.Countdown, at, end, EndReason.CountdownElapsed, [new(IntervalKind.Focus, at, end)], TodoId: i % 3 == 0 ? todo.Id : null));
            if (i == 0) store.SaveOutcome(new(id, "做完第三章的积分习题", 8, "题", "检查两道做错的题"));
        }
        for (var i = 0; i < 4; i++)
        {
            var habit = new Habit(Guid.NewGuid(), new[] { "睡前读十页", "散步二十分钟", "整理摘录", "写三行日记" }[i], [1, 2, 3, 4, 5, 6, 7], today.AddDays(-28));
            store.SaveHabit(habit);
            for (var j = 0; j < 24; j++) if ((i + j) % 4 != 0) store.SetHabitCheckIn(habit.Id, today.AddDays(-j), true);
        }
        store.SaveGoal(new(Guid.NewGuid(), "每周留给阅读 3 小时", GoalMetric.Minutes, 180, GoalCycle.Weekly, today.AddDays(-14)));
        for (var i = 0; i < 18; i++)
            store.SaveDailyReflection(new(today.AddDays(-i), new[] { 4, 3, 5, 2, 4, 3, 1, 4 }[i % 8], ["阅读", "散步"],
                "下午和导师聊了选题，思路清楚多了。", "写完高数作业，和组员对了一遍展示提纲。", "先做两道线代真题。", i == 0 ? [art] : null));
        return (project, folder, todo);
    }
}
