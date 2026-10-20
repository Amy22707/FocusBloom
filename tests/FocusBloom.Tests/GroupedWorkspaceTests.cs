using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class GroupedWorkspaceTests
{
    [Fact]
    public async Task DateGroupsPreserveExistingEventsLinksAndEmptyGroupsThroughRenameDeleteAndBackup()
    {
        var root = Temporary();
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "source.db"));
            var old = new DateEvent(Guid.NewGuid(), "原有日期", new(2026, 10, 20));
            store.SaveDateEvent(old);
            using (var connection = new SqliteConnection("Data Source=" + store.DatabasePath))
            {
                connection.Open(); using var command = connection.CreateCommand();
                var payload = JsonSerializer.SerializeToNode(old, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; payload.AsObject().Remove("group");
                command.CommandText = "UPDATE date_events SET payload=$payload WHERE id=$id"; command.Parameters.AddWithValue("$payload", payload.ToJsonString()); command.Parameters.AddWithValue("$id", old.Id.ToString()); command.ExecuteNonQuery();
            }
            Assert.Equal(old, Assert.Single(store.GetDateEvents()));
            var item = new DateEvent(Guid.NewGuid(), "考试", new(2026, 12, 1), Repeat: DateRepeat.Yearly, Notes: "准备资料", ProjectId: Guid.NewGuid(), TodoId: Guid.NewGuid(), Reminder: new(true, 30), Group: "考试");
            store.SaveDateEvent(item); store.SaveDateGroup("空分组"); store = new(store.DatabasePath);
            Assert.Contains("空分组", store.GetDateGroups()); store.RenameDateGroup("考试", "学习");
            Assert.Equal(item with { Group = "学习" }, store.GetDateEvents().Single(row => row.Id == item.Id)); Assert.DoesNotContain("考试", store.GetDateGroups());
            var archive = Path.Combine(root, "groups.fbbak"); await new LocalBackupService(store.DatabasePath).CreateAsync(archive);
            var target = new SqliteFocusStore(Path.Combine(root, "target", "focusbloom.db")); var backup = new LocalBackupService(target.DatabasePath);
            using (var prepared = await backup.PrepareRestoreAsync(archive)) backup.ApplyRestore(prepared);
            target = new(target.DatabasePath); Assert.Contains("空分组", target.GetDateGroups()); Assert.Equal("学习", target.GetDateEvents().Single(row => row.Id == item.Id).Group);
            target.DeleteDateGroup("学习"); Assert.Equal(item with { Group = "未分组" }, target.GetDateEvents().Single(row => row.Id == item.Id)); Assert.Equal(2, target.GetDateEvents().Count);
            Assert.Throws<InvalidOperationException>(() => target.DeleteDateGroup("未分组"));
        }
        finally { Clean(root); }
    }

    [Fact]
    public void CurrentAndAllScopesPersistAndKeepCompletedTodosAtTheBottomOfTheirFolder()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = new DateOnly(2026, 10, 20);
                store.SaveDateEvent(new(Guid.NewGuid(), "考试", day, Group: "学习")); store.SaveDateEvent(new(Guid.NewGuid(), "相识", day, Mode: DateCountMode.CountUp, Group: "生活"));
                var dates = new DateEventsViewModel(store); Assert.True(dates.ShowBoard); Assert.Equal(2, dates.VisibleItems.Count);
                dates.SelectedGroup = dates.GroupChoices.Single(item => item.Name == "学习"); Assert.Single(dates.VisibleItems); Assert.Equal(2, dates.Items.Count);
                Assert.Null(dates.SaveGroup("学习", "课程")); Assert.Equal("课程", dates.SelectedGroup!.Name); Assert.NotNull(dates.SaveGroup(null, "  课程  "));
                dates = new(store); Assert.Equal("课程", dates.SelectedGroup!.Name); Assert.Null(dates.DeleteGroup("课程")); Assert.Equal("未分组", dates.SelectedGroup!.Name); Assert.Single(dates.VisibleItems);
                var a = new TodoFolder(Guid.NewGuid(), "学习", "未分类"); var b = new TodoFolder(Guid.NewGuid(), "生活", "未分类"); store.SaveTodoFolder(a); store.SaveTodoFolder(b);
                var pending = new TodoItem(Guid.NewGuid(), a.Id, "复习"); var completed = new TodoItem(Guid.NewGuid(), a.Id, "已读", Status: TodoStatus.Completed);
                store.SaveTodo(completed); store.SaveTodo(pending); store.SaveTodo(new(Guid.NewGuid(), b.Id, "购物"));
                var todos = new TodoLibraryViewModel(store, _ => { }, _ => true) { SelectedFolder = a }; Assert.Equal(2, todos.VisibleItems.Count); Assert.True(todos.VisibleItems.Last().Completed);
                todos.ListScope = "全部文件夹"; Assert.Equal(3, todos.VisibleItems.Count); Assert.True(todos.ShowGroupedList); var group = todos.FolderGroups.Single(item => item.Folder.Id == a.Id); Assert.Single(group.Pending); Assert.Single(group.Completed); Assert.False(group.CompletedExpanded); group.CompletedExpanded = true;
                todos.Reload(); Assert.True(todos.FolderGroups.Single(item => item.Folder.Id == a.Id).CompletedExpanded);
                todos.ViewMode = "四象限"; todos.QuadrantScope = "当前文件夹"; Assert.Equal(2, todos.VisibleItems.Count); todos.ViewMode = "列表"; Assert.Equal(3, todos.VisibleItems.Count);
                todos = new(store, _ => { }, _ => true); Assert.Equal("全部文件夹", todos.ListScope); Assert.Equal("当前文件夹", todos.QuadrantScope);
                var project = new FocusProject(Guid.NewGuid(), "散步", "生活", "#718578"); store.SaveProject(project); main = new(store); main.ProjectLibraryScope = "全部文件夹";
                main.SelectedLibraryProject = main.Projects.Single(item => item.Id == project.Id); Assert.Equal("生活", main.SelectedFolder!.Name); Assert.Equal(project.Id, main.SelectedProject!.Id);
                Assert.Equal(main.Projects.Count, main.ProjectLibraryItems.Count()); main.ProjectLibraryScope = "当前文件夹"; Assert.Single(main.ProjectLibraryItems);
            }
            finally { main?.Media.Dispose(); Clean(root); }
        });
    }

    [Fact]
    public void GroupBoardsShowCompleteCardsScrollToTheEndAndOpenEditorsInTheChosenGroup()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
                for (var index = 0; index < 9; index++) store.SaveDateEvent(new(Guid.NewGuid(), index == 0 ? "期末考试" : "课程安排 " + index, today.AddDays(index + 3), Group: "考试"));
                store.SaveDateEvent(new(Guid.NewGuid(), "阅读开始", today.AddDays(-30), Mode: DateCountMode.CountUp, Group: "生活"));
                store.SaveDateEvent(new(Guid.NewGuid(), "提交材料", today.AddDays(4)));
                var a = new TodoFolder(Guid.NewGuid(), "课程作业", "未分类"); var b = new TodoFolder(Guid.NewGuid(), "个人安排", "未分类"); store.SaveTodoFolder(a); store.SaveTodoFolder(b);
                for (var index = 0; index < 10; index++) store.SaveTodo(new(Guid.NewGuid(), a.Id, "整理课程笔记 " + index, Status: index == 9 ? TodoStatus.Completed : TodoStatus.Pending, DueDate: today.AddDays(index)));
                store.SaveTodo(new(Guid.NewGuid(), b.Id, "去图书馆", DueDate: today.AddDays(2)));
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1120, Height = 720, NavigationCollapsed = true }; window.Show(); Flush();
                main.Navigation.Items.Single(item => item.Key == "dates").OpenCommand.Execute(null); Flush(); var dates = Children<DateEventsView>(window).Single();
                var board = (ItemsControl)dates.FindName("DateGroupBoard"); Assert.Equal(3, board.Items.Count); var dateScroll = Children<ScrollViewer>(board).Single(item => item.Tag as string == "考试"); Assert.True(dateScroll.ScrollableHeight > 0);
                Assert.All(Children<Border>(dateScroll).Where(item => item.Height == 108), item => Assert.Equal(108, item.ActualHeight)); dateScroll.ScrollToEnd(); Flush(); Assert.Equal(dateScroll.ScrollableHeight, dateScroll.VerticalOffset);
                dateScroll.ScrollToTop(); Flush(); GoalsUiTests.Save(window, "groups-dates-light.png");
                var add = Children<Button>(board).Single(item => item.Content as string == "＋" && item.DataContext is DateEventGroup { Name: "生活" });
                InDialog<DateEventEditorWindow>(() => Click(add), dialog => { Assert.Equal("生活", ((ComboBox)dialog.FindName("DateGroupSelector")).SelectedItem); dialog.Close(); });
                var dateButton = Children<Button>(dateScroll).First(item => item.DataContext is DateEventRow);
                var dateId = ((DateEventRow)dateButton.DataContext).Item.Id;
                InDialog<DateEventEditorWindow>(() => Click(dateButton), dialog => { ((ComboBox)dialog.FindName("DateGroupSelector")).SelectedItem = "生活"; Click((Button)dialog.FindName("SaveButton")); }); Flush();
                Assert.Equal("生活", store.GetDateEvents().Single(item => item.Id == dateId).Group);
                InDialog<DateGroupWindow>(() => Click((Button)dates.FindName("NewDateGroupButton")), dialog => { ((TextBox)dialog.FindName("NameInput")).Text = "临时分组"; Click((Button)dialog.FindName("SaveButton")); }); Flush(); Assert.Contains("临时分组", main.DateEvents.GroupNames);
                var edit = Children<Button>(board).Single(item => item.Content as string == "···" && item.DataContext is DateEventGroup { Name: "临时分组" });
                InDialog<DateGroupWindow>(() => Click(edit), dialog =>
                {
                    InDialog<ConfirmDeletionWindow>(() => Click((Button)dialog.FindName("DeleteButton")), confirmation => { Assert.Contains("移至", ((TextBlock)confirmation.FindName("DeletionMessage")).Text); confirmation.Close(); }); Assert.Contains("临时分组", store.GetDateGroups());
                    InDialog<ConfirmDeletionWindow>(() => Click((Button)dialog.FindName("DeleteButton")), confirmation => Click((Button)confirmation.FindName("ConfirmDeletionButton")));
                }); Flush(); Assert.DoesNotContain("临时分组", store.GetDateGroups());
                ((ComboBox)dates.FindName("DateGroupScopeSelector")).SelectedItem = main.DateEvents.GroupChoices.Single(item => item.Name == "生活"); Flush(); Assert.False(main.DateEvents.ShowBoard); Assert.Equal(2, ((ItemsControl)dates.FindName("DateCards")).Items.Count);
                main.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); main.Todos.ListScope = "全部文件夹"; Flush(); var todos = (TodosView)window.FindName("TodoPage"); var todoBoard = (ItemsControl)todos.FindName("TodoGroupBoard");
                Assert.True(main.Todos.ShowGroupedList); Assert.Contains(Children<TextBlock>(todoBoard), item => item.Text == "去图书馆");
                var todoScroll = Children<ScrollViewer>(todoBoard).Single(item => item.Tag is Guid id && id == a.Id); Assert.True(todoScroll.ScrollableHeight > 0); todoScroll.ScrollToVerticalOffset(150); Flush(); var offset = todoScroll.VerticalOffset;
                Assert.All(Children<Border>(todoScroll).Where(item => item.Name == "TodoCardSurface"), item => Assert.Equal(104, item.ActualHeight));
                var row = Children<Button>(todoScroll).First(item => item.Content as string == "···" && item.DataContext is TodoRow);
                InDialog<TodoEditorWindow>(() => Click(row), dialog => Click((Button)dialog.FindName("SaveButton"))); Flush(); todoScroll = Children<ScrollViewer>(todoBoard).Single(item => item.Tag is Guid id && id == a.Id); Assert.Equal(offset, todoScroll.VerticalOffset);
                var completed = Children<Expander>(todoBoard).Single(item => item.DataContext is TodoFolderGroup group && group.Folder.Id == a.Id); Assert.False(completed.IsExpanded); completed.IsExpanded = true; Flush(); todoScroll.ScrollToEnd(); Flush(); Assert.Equal(todoScroll.ScrollableHeight, todoScroll.VerticalOffset);
                todoScroll.ScrollToTop(); Flush(); GoalsUiTests.Save(window, "groups-todos-light.png");
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); window.Width = 900; window.Height = 560; Flush(); Assert.True(((ScrollViewer)todos.FindName("TodoGroupsViewport")).ScrollableWidth > 0); GoalsUiTests.Save(window, "groups-todos-dark-compact.png");
                main.Todos.ListScope = "当前文件夹"; main.Todos.SelectedFolder = main.Todos.Folders.Single(item => item.Id == b.Id); Flush(); Assert.Single(((ListBox)todos.FindName("TodoList")).Items);
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (Window dialog in Application.Current.Windows.Cast<Window>().Where(item => item != window).ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    [Fact]
    public void ProjectLibraryAllFoldersKeepsFocusSelectionAndSortingConsistent()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db"));
                var a = new FocusProject(Guid.NewGuid(), "系统实验", "学习", "#718578"); var b = new FocusProject(Guid.NewGuid(), "阅读", "生活", "#718578"); var c = new FocusProject(Guid.NewGuid(), "散步", "生活", "#718578");
                foreach (var item in new[] { a, b, c }) store.SaveProject(item);
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1120, Height = 720 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "projects").OpenCommand.Execute(null); Flush();
                var list = (ListBox)window.FindName("ProjectList"); var selector = (ComboBox)window.FindName("ProjectLibraryScopeSelector"); selector.SelectedItem = "全部文件夹"; Flush(); Assert.Equal(main.Projects.Count, list.Items.Count);
                list.SelectedItem = main.Projects.Single(item => item.Id == b.Id); Flush(); Assert.Equal(b.Id, main.SelectedProject!.Id); Assert.Equal("生活", main.SelectedFolder!.Name);
                main.MoveProject(main.Projects.Single(item => item.Id == c.Id), OrderMove.First); Flush(); Assert.Equal(c.Id, main.Projects[0].Id); Assert.Equal(b.Id, main.SelectedProject!.Id);
                selector.SelectedItem = "当前文件夹"; Flush(); Assert.Equal(2, list.Items.Count); Assert.Equal(c.Id, ((FocusProject)list.Items[0]).Id); Assert.Equal(b.Id, main.SelectedProject!.Id);
                selector.SelectedItem = "全部文件夹"; Flush(); GoalsUiTests.Save(window, "groups-projects-light.png"); Assert.Equal("全部文件夹", store.GetSetting("projects.scope")); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "FocusBloomGroups", Guid.NewGuid().ToString());
    private static void Clean(string root) { SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void InDialog<T>(Action open, Action<T> action) where T : Window
    { Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => action(Application.Current.Windows.OfType<T>().Single()))); open(); }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
