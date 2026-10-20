using System;
using System.Windows;
using System.Windows.Controls;
using System.Linq;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class TodosView : UserControl
{
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    private TodoLibraryViewModel Library => (TodoLibraryViewModel)DataContext;
    public TodosView() => InitializeComponent();
    private void GroupMove_Requested(object sender, RoutedEventArgs e)
    {
        var move = (GroupMoveEventArgs)e; var row = (TodoRow)move.Item; var offsets = CaptureGroupOffsets();
        move.Moved = move.Target switch
        {
            TodoFolderGroup group => Library.MoveToFolder(row, group.Folder),
            TodoFolder folder => Library.MoveToFolder(row, folder),
            TodoQuadrant quadrant => Library.MoveToQuadrant(row, quadrant),
            _ => false
        };
        if (!move.Moved) ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), Main.StatusMessage, "未能移动");
        else Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RestoreGroupOffsets(offsets)));
        e.Handled = true;
    }
    private void QuadrantBoard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (QuadrantAxes is null || QuadrantBoard.ActualWidth <= 0 || QuadrantBoard.ActualHeight <= 0) return;
        var origin = QuadrantBoard.TranslatePoint(new Point(QuadrantBoard.ActualWidth / 2, QuadrantBoard.ActualHeight / 2), QuadrantPlane);
        var bottom = QuadrantBoard.TranslatePoint(new Point(0, QuadrantBoard.ActualHeight), QuadrantPlane).Y;
        var top = QuadrantBoard.TranslatePoint(new Point(), QuadrantPlane).Y;
        var rightTip = new Point(QuadrantPlane.ActualWidth - 2, origin.Y);
        var topTip = new Point(origin.X, top - 10);
        var lines = new StreamGeometry();
        using (var drawing = lines.Open())
        {
            drawing.BeginFigure(new Point(1, origin.Y), false, false); drawing.LineTo(rightTip, true, false);
            drawing.BeginFigure(new Point(rightTip.X - 6, rightTip.Y - 4), false, false);
            drawing.LineTo(rightTip, true, false); drawing.LineTo(new Point(rightTip.X - 6, rightTip.Y + 4), true, false);
            drawing.BeginFigure(new Point(origin.X, bottom), false, false); drawing.LineTo(topTip, true, false);
            drawing.BeginFigure(new Point(topTip.X - 4, topTip.Y + 6), false, false);
            drawing.LineTo(topTip, true, false); drawing.LineTo(new Point(topTip.X + 4, topTip.Y + 6), true, false);
        }
        var axes = new GeometryGroup(); axes.Children.Add(lines); axes.Children.Add(new EllipseGeometry(origin, 2, 2));
        axes.Freeze(); QuadrantAxes.Data = axes;
    }
    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        new TodoGroupWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void EditFolder_Click(object sender, RoutedEventArgs e) => new TodoGroupWindow(Main, (TodoFolder)((Button)sender).DataContext) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewTodo_Click(object sender, RoutedEventArgs e)
    { NewTodo(); }
    private void NewQuadrantTodo_Click(object sender, RoutedEventArgs e)
    { var quadrant = (TodoQuadrant)((Button)sender).DataContext; NewTodo(quadrant.Important, quadrant.Urgent); }
    private void NewInFolder_Click(object sender, RoutedEventArgs e) => new TodoEditorWindow(Main, initialFolderId: ((TodoFolder)((Button)sender).DataContext).Id) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewTodo(bool important = false, bool urgent = false)
    {
        if (Library.SelectedFolder is null) Library.SaveFolder(null, "待整理", "未分类");
        new TodoEditorWindow(Main, important: important, urgent: urgent) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void QuadrantScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var scroll = (ScrollViewer)sender;
        if (e.Delta < 0 && scroll.VerticalOffset < scroll.ScrollableHeight || e.Delta > 0 && scroll.VerticalOffset > 0) return;
        QuadrantViewport.ScrollToVerticalOffset(QuadrantViewport.VerticalOffset - e.Delta / 120d * 48); e.Handled = true;
    }
    private void EditTodo_Click(object sender, RoutedEventArgs e)
    {
        var folder = Library.SelectedFolder?.Id;
        var scroll = FindScroll(TodoList); var offset = scroll?.VerticalOffset ?? 0;
        var groups = CaptureGroupOffsets();
        new TodoEditorWindow(Main, ((TodoRow)((Button)sender).DataContext).Item) { Owner = Window.GetWindow(this) }.ShowDialog();
        if (Library.ShowGroupedList || Library.SelectedFolder?.Id == folder) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { scroll?.ScrollToVerticalOffset(offset); RestoreGroupOffsets(groups); }));
    }
    private void TodoDetails_Click(object sender, RoutedEventArgs e) => OpenTodoDetails((TodoRow)((Button)sender).DataContext);
    private void TodoCard_MouseUp(object sender, MouseButtonEventArgs e) { if (!e.Handled) { e.Handled = true; OpenTodoDetails((TodoRow)((Border)sender).DataContext); } }
    private void OpenTodoDetails(TodoRow item)
    {
        var folder = Library.SelectedFolder?.Id;
        var listScroll = FindScroll(TodoList); var listOffset = listScroll?.VerticalOffset ?? 0;
        var groups = CaptureGroupOffsets();
        var offsets = Library.Quadrants.ToDictionary(quadrant => quadrant, quadrant => FindQuadrantScroll(QuadrantBoard, quadrant)?.VerticalOffset ?? 0);
        var dialog = new TodoDetailsWindow(Main, item) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() == true) ((MainWindow)Window.GetWindow(this)).ReturnToFocus();
        if (Library.ShowGroupedList || Library.SelectedFolder?.Id == folder) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            listScroll?.ScrollToVerticalOffset(listOffset);
            RestoreGroupOffsets(groups);
            foreach (var pair in offsets) FindQuadrantScroll(QuadrantBoard, pair.Key)?.ScrollToVerticalOffset(pair.Value);
        }));
    }
    private System.Collections.Generic.Dictionary<Guid, double> CaptureGroupOffsets() => GroupScrolls(TodoGroupBoard).ToDictionary(scroll => (Guid)scroll.Tag, scroll => scroll.VerticalOffset);
    private void RestoreGroupOffsets(System.Collections.Generic.Dictionary<Guid, double> offsets)
    { foreach (var scroll in GroupScrolls(TodoGroupBoard)) if (offsets.TryGetValue((Guid)scroll.Tag, out var offset)) scroll.ScrollToVerticalOffset(offset); }
    private static System.Collections.Generic.IEnumerable<ScrollViewer> GroupScrolls(DependencyObject parent)
    {
        if (parent is ScrollViewer { Tag: Guid } scroll) yield return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in GroupScrolls(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }
    private static ScrollViewer? FindQuadrantScroll(DependencyObject parent, TodoQuadrant quadrant)
    {
        if (parent is ScrollViewer scroll && ReferenceEquals(scroll.DataContext, quadrant)) return scroll;
        return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(parent)).Select(index => FindQuadrantScroll(VisualTreeHelper.GetChild(parent, index), quadrant)).FirstOrDefault(item => item is not null);
    }
    private static ScrollViewer? FindScroll(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        return Enumerable.Range(0, VisualTreeHelper.GetChildrenCount(parent)).Select(index => FindScroll(VisualTreeHelper.GetChild(parent, index))).FirstOrDefault(item => item is not null);
    }
    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        var groups = CaptureGroupOffsets(); Library.ToggleCompleted((TodoRow)((CheckBox)sender).DataContext);
        if (Library.ShowGroupedList) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => RestoreGroupOffsets(groups)));
    }
    private void FolderOrder_Requested(object sender, RoutedEventArgs e)
    { Library.MoveFolder((TodoFolder)((ReorderButton)sender).DataContext, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as TodoFolder); e.Handled = true; }
    private void ItemOrder_Requested(object sender, RoutedEventArgs e)
    { Library.MoveItem((TodoRow)((ReorderButton)sender).DataContext, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as TodoRow); e.Handled = true; }
    private void QuadrantItemOrder_Requested(object sender, RoutedEventArgs e)
    {
        var item = (TodoRow)((ReorderButton)sender).DataContext;
        var quadrant = Library.Quadrants.Single(group => group.Important == item.Item.Important && group.Urgent == item.Item.Urgent);
        Library.MoveItem(item, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as TodoRow, quadrant); e.Handled = true;
    }
}
