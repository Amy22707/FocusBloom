using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class SidebarLayoutTests
{
    [Fact]
    public void FolderActionsStayCompactAndDraggingPreservesCollapseAndSavedWidths()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomSidebars", Guid.NewGuid().ToString("N"));
            MainWindow? window = null;
            MainViewModel? model = null;
            try
            {
                var path = Path.Combine(root, "test.db");
                var store = new SqliteFocusStore(path);
                var folder = new TodoFolder(Guid.NewGuid(), "课程学习", "学习");
                store.SaveTodoCategory(folder.Category);
                store.SaveTodoFolder(folder);
                model = new MainViewModel(store);
                model.Typography.InterfaceSizePercent = 120;
                model.Typography.InterfaceFont = model.Typography.InterfaceFonts.Single(font => font.Family == "KaiTi");
                window = new MainWindow(model, store, Path.Combine(root, "browser")) { Width = 1280, Height = 820 };
                window.Show();
                model.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null);
                model.Todos.SelectedFolder = model.Todos.Folders.Single(item => item.Id == folder.Id);
                Flush();
                var page = (TodosView)window.FindName("TodoPage");
                var list = (ListBox)page.FindName("TodoFolderList");
                var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(model.Todos.SelectedFolder);
                var label = Descendants<TextBlock>(row).Single(item => item.Text == folder.Name);
                var action = Descendants<Button>(row).Single(item => item is not ReorderButton);
                Assert.Equal(24, action.ActualWidth, 2);
                Assert.True(label.ActualWidth > 80);
                Assert.Equal(folder.Name, label.ToolTip);
                var icon = Descendants<System.Windows.Shapes.Path>(action).Single();
                Assert.Equal(14, icon.ActualWidth, 2);

                var splitter = (SidebarSplitter)page.FindName("TodoSidebarSplitter");
                Drag(splitter, 80);
                Assert.Equal(300, model.TodoSidebarWidth, 2);
                var toggle = (ToggleButton)page.FindName("TodoSidebarToggle");
                toggle.IsChecked = true; Flush();
                Assert.Equal(0, ((ColumnDefinition)page.FindName("TodoSidebarColumn")).ActualWidth);
                toggle.IsChecked = false; Flush();
                Assert.Equal(300, ((ColumnDefinition)page.FindName("TodoSidebarColumn")).ActualWidth, 2);

                model.Navigation.Items.Single(item => item.Key == "projects").OpenCommand.Execute(null); Flush();
                Drag((SidebarSplitter)window.FindName("ProjectSidebarSplitter"), 40);
                Assert.Equal(260, model.ProjectSidebarWidth, 2);
                var tabs = (TabControl)window.FindName("MainTabs");
                Drag((SidebarSplitter)tabs.Template.FindName("NavigationSplitter", tabs), 40);
                Assert.Equal(200, model.NavigationWidth, 2);
                window.NavigationCollapsed = true; Flush();
                Assert.Equal(64, ((ColumnDefinition)tabs.Template.FindName("NavigationColumn", tabs)).ActualWidth, 2);
                window.NavigationCollapsed = false; Flush();
                Assert.Equal(200, ((ColumnDefinition)tabs.Template.FindName("NavigationColumn", tabs)).ActualWidth, 2);

                var reopened = new MainViewModel(new SqliteFocusStore(path));
                try
                {
                    Assert.Equal(300, reopened.TodoSidebarWidth, 2);
                    Assert.Equal(260, reopened.ProjectSidebarWidth, 2);
                    Assert.Equal(200, reopened.NavigationWidth, 2);
                }
                finally { reopened.Media.Dispose(); }
                model.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); Flush();
                GoalsUiTests.Save(window, "sidebar-compact-actions.png");
            }
            finally
            {
                window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); model?.Media.Dispose();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }

    private static void Drag(SidebarSplitter splitter, double change)
    {
        splitter.RaiseEvent(new DragDeltaEventArgs(change, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        Flush();
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
