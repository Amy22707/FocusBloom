using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
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
public sealed class GroupDragDropTests
{
    [Fact]
    public void CardsAndHandlesMoveBetweenGroupsAndFoldersWithoutLosingInformation()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomGroupDrag", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = DateOnly.FromDateTime(DateTime.Today);
                var date = new DateEvent(Guid.NewGuid(), "期中考试", day.AddDays(10), new(10, 30), Repeat: DateRepeat.Yearly, Notes: "带准考证", Pinned: true, TodoId: Guid.NewGuid(), ProjectId: Guid.NewGuid(), Reminder: new(true, 30));
                store.SaveDateEvent(date); store.SaveDateGroup("考试");
                var a = new TodoFolder(Guid.NewGuid(), "课程", "未分类"); var b = new TodoFolder(Guid.NewGuid(), "生活", "未分类"); store.SaveTodoFolder(a); store.SaveTodoFolder(b);
                var todo = new TodoItem(Guid.NewGuid(), a.Id, "阅读", DueDate: day, DueTime: new(15, 30), Notes: "完整的备注", ResumeNote: "下一页", PreferredProjectId: store.GetProjects()[0].Id); store.SaveTodo(todo); store.SaveReminderOptions("待办", todo.Id, new(true, 45));
                var project = new FocusProject(Guid.NewGuid(), "写作", "学习", "#718578"); store.SaveProject(project); store.SaveFolder("生活");
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1120, Height = 720, NavigationCollapsed = true }; window.Show(); Flush();
                main.Navigation.Items.Single(item => item.Key == "dates").OpenCommand.Execute(null); Flush();
                var dates = Children<DateEventsView>(window).Single(); var dateBoard = (ItemsControl)dates.FindName("DateGroupBoard");
                var source = Children<Button>(dateBoard).Single(item => item.DataContext is DateEventRow row && row.Item.Id == date.Id); Assert.True(GroupDragDrop.GetSource(source));
                var target = Target(dateBoard, item => item is DateEventGroup { Name: "考试" });
                var data = CardData(source); Over(target, data); Flush(); Assert.Equal(date, store.GetDateEvents().Single()); Assert.NotEmpty(AdornerLayer.GetAdornerLayer(target)!.GetAdorners(target));
                GoalsUiTests.Save(window, "group-drag-date-preview.png"); Drop(target, data); Flush(); Assert.Equal(date with { Group = "考试" }, store.GetDateEvents().Single());
                Assert.Null(AdornerLayer.GetAdornerLayer(target)?.GetAdorners(target)); Assert.Single(main.DateEvents.Groups.Single(item => item.Name == "考试").Items);
                main.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); main.Todos.ListScope = "全部文件夹"; Flush(); var todos = (TodosView)window.FindName("TodoPage");
                var board = (ItemsControl)todos.FindName("TodoGroupBoard"); var handle = Children<ReorderButton>(board).Single(item => item.DataContext is TodoRow row && row.Id == todo.Id);
                target = Target(board, item => item is TodoFolderGroup group && group.Folder.Id == b.Id); data = HandleData(handle); Over(target, data); Flush(); GoalsUiTests.Save(window, "group-drag-todo-preview.png"); Drop(target, data); Flush();
                Assert.Equal(todo with { FolderId = b.Id }, store.GetTodos().Single()); Assert.Equal(45, store.GetReminderOptions("待办", todo.Id).MinutesBefore);
                main.Todos.ViewMode = "四象限"; main.Todos.QuadrantScope = "全部文件夹"; Flush(); board = (ItemsControl)todos.FindName("QuadrantBoard");
                handle = Children<ReorderButton>(board).Single(item => item.DataContext is TodoRow row && row.Id == todo.Id); target = Target(board, item => item is TodoQuadrant { Important: true, Urgent: true });
                data = HandleData(handle); Over(target, data); Drop(target, data); Flush(); Assert.Equal(todo with { FolderId = b.Id, Important = true, Urgent = true }, store.GetTodos().Single());
                main.Navigation.Items.Single(item => item.Key == "projects").OpenCommand.Execute(null); main.ProjectLibraryScope = "全部文件夹"; Flush();
                var list = (ListBox)window.FindName("ProjectList"); handle = Children<ReorderButton>(list).Single(item => item.DataContext is FocusProject row && row.Id == project.Id);
                target = Target((ListBox)window.FindName("FolderList"), item => item is ProjectFolder { Name: "生活" }); data = HandleData(handle); Over(target, data); Drop(target, data); Flush();
                Assert.Contains(store.GetProjects(), item => item == project with { Category = "生活" }); Assert.Equal("生活", main.SelectedFolder!.Name); Assert.Equal(project.Id, main.SelectedProject!.Id);
                var folder = main.Todos.Folders.Single(item => item.Id == a.Id); store.SaveTodo(new(Guid.NewGuid(), a.Id, "阅读")); main.Todos.Reload();
                Assert.False(main.Todos.MoveToFolder(main.Todos.Items.Single(item => item.Id == todo.Id), folder)); Assert.Equal(b.Id, store.GetTodos().Single(item => item.Id == todo.Id).FolderId);
                Assert.Equal(2, store.GetTodos().Count); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void DetailTextSupportsSelectionAndCopyCommandsWhileRemainingReadOnly()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomCopyDetails", Guid.NewGuid().ToString()); MainViewModel? main = null; TodoDetailsWindow? details = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var folder = new TodoFolder(Guid.NewGuid(), "工作", "未分类"); store.SaveTodoFolder(folder);
                var todo = new TodoItem(Guid.NewGuid(), folder.Id, "示例学术论坛", DueDate: new(2026, 11, 7), Notes: "会议地址：示例大学\n准备签到资料", ResumeNote: "联系参会人员"); store.SaveTodo(todo); main = new(store);
                details = new(main, main.Todos.Items.Single(item => item.Id == todo.Id)); details.Show(); Flush();
                foreach (var field in Children<SelectableText>(details))
                {
                    Assert.True(field.IsReadOnly); field.SelectAll(); Assert.Equal(field.Text, field.SelectedText); Assert.True(ApplicationCommands.Copy.CanExecute(null, field));
                    string? copied = null; var capture = new CommandBinding(ApplicationCommands.Copy, (_, args) => { copied = field.SelectedText; args.Handled = true; }); field.CommandBindings.Add(capture);
                    ApplicationCommands.Copy.Execute(null, field); Assert.Equal(field.Text, copied); field.CommandBindings.Remove(capture);
                    Assert.Equal(0, field.BorderThickness.Left); field.Select(0, 0);
                }
                var title = (SelectableText)details.FindName("DetailTitle"); Assert.Equal(todo.Title, title.Text); var notes = (SelectableText)details.FindName("DetailNotes"); notes.Select(5, 4); Assert.Equal("示例大学", notes.SelectedText);
                GoalsUiTests.Save(details, "detail-copy-light.png"); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(details, "detail-copy-dark.png");
                Assert.Equal(todo, store.GetTodos().Single()); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { details?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static FrameworkElement Target(DependencyObject parent, Func<object, bool> matches) => Children<FrameworkElement>(parent).Single(element => GroupDragDrop.GetAcceptType(element) is not null && matches(element.DataContext));
    private static ItemsControl Owner(DependencyObject source)
    { for (var node = VisualTreeHelper.GetParent(source); node is not null; node = VisualTreeHelper.GetParent(node)) if (node is ItemsControl owner) return owner; throw new InvalidOperationException(); }
    private static IDataObject HandleData(ReorderButton handle) => new DataObject("FocusBloom.ItemOrder", Activator.CreateInstance(typeof(ReorderButton).GetNestedType("DragItem", BindingFlags.NonPublic)!, handle, Owner(handle))!);
    private static IDataObject CardData(FrameworkElement card)
    {
        var payload = Activator.CreateInstance(typeof(GroupDragDrop).GetNestedType("DragItem", BindingFlags.NonPublic)!, card.DataContext)!;
        var preview = Activator.CreateInstance(typeof(ReorderButton).Assembly.GetType("FocusBloom.Desktop.Presentation.ReorderPreview")!, card, Owner(card), new Point(10, 10));
        payload.GetType().GetProperty("Preview")!.SetValue(payload, preview); return new DataObject("FocusBloom.GroupItem", payload);
    }
    private static void Over(FrameworkElement target, IDataObject data) { var args = Args(target, data, DragDrop.DragOverEvent); target.RaiseEvent(args); Assert.Equal(DragDropEffects.Move, args.Effects); }
    private static void Drop(FrameworkElement target, IDataObject data) { var args = Args(target, data, DragDrop.DropEvent); target.RaiseEvent(args); Assert.True(args.Handled); Assert.Equal(DragDropEffects.Move, args.Effects); }
    private static DragEventArgs Args(FrameworkElement target, IDataObject data, RoutedEvent routed)
    { var args = (DragEventArgs)typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(60, 60)]); args.RoutedEvent = routed; return args; }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
