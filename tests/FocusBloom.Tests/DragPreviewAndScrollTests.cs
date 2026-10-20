using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class DragPreviewAndScrollTests
{
    [Fact]
    public void GroupAndCardPreviewsFollowScreenCoordinatesAfterWindowMovesAndLeaveNoOverlay()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = DateOnly.FromDateTime(DateTime.Today);
                store.SaveDateEvent(new(Guid.NewGuid(), "课程考试", day.AddDays(10), Group: "考试")); store.SaveDateEvent(new(Guid.NewGuid(), "阅读计划", day.AddDays(5), Group: "工作"));
                var folder = new TodoFolder(Guid.NewGuid(), "学习", "未分类"); store.SaveTodoFolder(folder); store.SaveTodo(new(Guid.NewGuid(), folder.Id, "阅读课程材料"));
                main = new(store); Assert.Equal("", main.StatusMessage); window = new(main, store, Path.Combine(root, "browser")) { Width = 1120, Height = 720, NavigationCollapsed = true }; window.Show(); main.Navigation.Items.Single(item => item.Key == "dates").OpenCommand.Execute(null); Flush();
                var dates = Children<DateEventsView>(window).Single(); var board = (ItemsControl)dates.FindName("DateGroupBoard");
                foreach (var left in new[] { 120d, 320d })
                {
                    window.Left = left; window.Top = 80; Flush();
                    CheckPreview(Children<ReorderButton>(board).Single(handle => handle.DataContext is DateEventGroup { Name: "考试" }), window, "drag-preview-column.png");
                    CheckPreview(Children<Button>(board).Single(button => button.DataContext is DateEventRow row && row.Title == "课程考试"), window, "drag-preview-date-card.png");
                }
                main.Navigation.Items.Single(item => item.Key == "todos").OpenCommand.Execute(null); Flush(); var todos = (TodosView)window.FindName("TodoPage");
                CheckPreview(Children<Border>(todos).Single(border => border.Name == "TodoCardSurface"), window, "drag-preview-todo-card.png");
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static void CheckPreview(FrameworkElement source, MainWindow window, string file)
    {
        ItemsControl? owner = null;
        for (var node = VisualTreeHelper.GetParent(source); node is not null && owner is null; node = VisualTreeHelper.GetParent(node)) owner = node as ItemsControl;
        Assert.NotNull(owner); var container = (FrameworkElement)owner.ItemContainerGenerator.ContainerFromItem(source.DataContext); var originalOpacity = container.Opacity; var originalTransform = container.RenderTransform;
        var type = typeof(ReorderButton).Assembly.GetType("FocusBloom.Desktop.Presentation.ReorderPreview")!;
        var preview = (IDisposable)Activator.CreateInstance(type, source, owner, new Point(10, 10))!;
        var root = (FrameworkElement)window.Content;
        try
        {
            var ghost = type.GetField("_ghost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!; Assert.NotNull(ghost);
            var grab = (Point)type.GetField("_grab", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
            foreach (var cursor in new[] { new Point(600, 250), new Point(760, 330) })
            {
                var screen = root.PointToScreen(cursor); type.GetMethod("FollowScreenPoint", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(preview, [screen]); Flush();
                var position = (Point)ghost.GetType().GetField("_point", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ghost)!;
                Assert.InRange((position + (Vector)grab - cursor).Length, 0, .001); Assert.True(position.X > 100);
            }
            var bitmap = (BitmapSource)ghost.GetType().GetField("_image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(ghost)!;
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa", file)); using var output = File.Create(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); encoder.Save(output);
        }
        finally { preview.Dispose(); }
        Assert.Equal(originalOpacity, container.Opacity); Assert.Same(originalTransform, container.RenderTransform);
        Assert.DoesNotContain(AdornerLayer.GetAdornerLayer(root)?.GetAdorners(root) ?? Array.Empty<Adorner>(), adorner => adorner.GetType().Name == "Ghost");
    }

    [Fact]
    public void ThinScrollbarsHideUntilHoveredAndKeepShowingWhileDraggingWithoutLayoutChanges()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainViewModel? main = null; Window? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); main = new(store);
                var panel = new Border { Width = 1000, Height = 900 }; panel.SetResourceReference(Border.BackgroundProperty, "FieldBrush");
                var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, CanContentScroll = false };
                window = new Window { Content = scroll, Width = 500, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterScreen }; window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window)); window.Show(); Flush();
                Assert.All(Children<System.Windows.Shapes.Rectangle>(scroll), corner => Assert.Equal(0, corner.Fill.Opacity == 0 ? 0 : ((SolidColorBrush)corner.Fill).Color.A));
                var vertical = (ScrollBar)scroll.Template.FindName("PART_VerticalScrollBar", scroll); var horizontal = (ScrollBar)scroll.Template.FindName("PART_HorizontalScrollBar", scroll);
                var hover = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                var dragging = (DependencyPropertyKey)typeof(Thumb).GetField("IsDraggingPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
                foreach (var dark in new[] { false, true })
                {
                    main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == (dark ? AppearanceMode.Dark : AppearanceMode.Light)); Flush(); var width = scroll.ViewportWidth; var height = scroll.ViewportHeight;
                    foreach (var bar in new[] { vertical, horizontal })
                    {
                        bar.ApplyTemplate(); var thumb = (Thumb)bar.Template.FindName("DragThumb", bar); thumb.ApplyTemplate(); Assert.Equal(0, thumb.Opacity);
                        bar.SetValue(hover, true); Flush(); Assert.Equal(.7, thumb.Opacity); var surface = Children<Border>(thumb).Single();
                        Assert.True((bar.Orientation == Orientation.Vertical ? bar.ActualWidth : bar.ActualHeight) >= 3); Assert.InRange(bar.Orientation == Orientation.Vertical ? surface.ActualWidth : surface.ActualHeight, 2.5, 3.5);
                        thumb.SetValue(dragging, true); bar.SetValue(hover, false); Flush(); Assert.Equal(1, thumb.Opacity);
                        thumb.SetValue(dragging, false); Flush(); Assert.Equal(0, thumb.Opacity); Assert.Equal(width, scroll.ViewportWidth); Assert.Equal(height, scroll.ViewportHeight);
                    }
                    scroll.ScrollToVerticalOffset(scroll.ScrollableHeight); scroll.ScrollToHorizontalOffset(scroll.ScrollableWidth); Flush(); Assert.Equal(scroll.ScrollableHeight, scroll.VerticalOffset); Assert.Equal(scroll.ScrollableWidth, scroll.HorizontalOffset);
                    vertical.SetValue(hover, true); horizontal.SetValue(hover, true); Flush(); GoalsUiTests.Save(window, "scrollbars-thin-hover-" + (dark ? "dark" : "light") + ".png"); vertical.SetValue(hover, false); horizontal.SetValue(hover, false); Flush();
                }
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "FocusBloomDragScroll", Guid.NewGuid().ToString());
    private static void Clean(string path) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(path)) Directory.Delete(path, true); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
