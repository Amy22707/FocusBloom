using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Views;

public partial class DateEventsView : UserControl
{
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public DateEventsView() => InitializeComponent();
    private void GroupOrder_Requested(object sender, RoutedEventArgs e)
    {
        var offsets = GroupScrolls(DateGroupBoard).ToDictionary(scroll => (string)scroll.Tag, scroll => scroll.VerticalOffset);
        Main.DateEvents.MoveGroup((DateEventGroup)((ReorderButton)sender).DataContext, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as DateEventGroup);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { foreach (var scroll in GroupScrolls(DateGroupBoard)) if (offsets.TryGetValue((string)scroll.Tag, out var offset)) scroll.ScrollToVerticalOffset(offset); }));
        e.Handled = true;
    }
    private void GroupMove_Requested(object sender, RoutedEventArgs e)
    {
        var move = (GroupMoveEventArgs)e; var row = (DateEventRow)move.Item; var group = (DateEventGroup)move.Target;
        var offsets = GroupScrolls(DateGroupBoard).ToDictionary(scroll => (string)scroll.Tag, scroll => scroll.VerticalOffset);
        var error = Main.DateEvents.MoveToGroup(row, group.Name);
        if (error is not null) ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), error, "未能移动");
        else { move.Moved = true; Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { foreach (var scroll in GroupScrolls(DateGroupBoard)) if (offsets.TryGetValue((string)scroll.Tag, out var offset)) scroll.ScrollToVerticalOffset(offset); })); }
        e.Handled = true;
    }
    private void New_Click(object sender, RoutedEventArgs e) => new DateEventEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewGroup_Click(object sender, RoutedEventArgs e) => new DateGroupWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditGroup_Click(object sender, RoutedEventArgs e) => new DateGroupWindow(Main, ((DateEventGroup)((Button)sender).DataContext).Name) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewInGroup_Click(object sender, RoutedEventArgs e) => new DateEventEditorWindow(Main, initialGroup: ((DateEventGroup)((Button)sender).DataContext).Name) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditSelectedGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = Main.DateEvents.SelectedGroup?.Name;
        if (name == "未分组") { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), "默认分组保留，事项可以移入其他分组。", "默认分组"); return; }
        if (name is not null) new DateGroupWindow(Main, name) { Owner = Window.GetWindow(this) }.ShowDialog();
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var offset = DateCardsViewport.VerticalOffset;
        var groups = GroupScrolls(DateGroupBoard).ToDictionary(scroll => (string)scroll.Tag, scroll => scroll.VerticalOffset);
        var editor = new DateEventEditorWindow(Main, ((DateEventRow)((Button)sender).DataContext).Item) { Owner = Window.GetWindow(this) };
        editor.ShowDialog(); if (editor.FocusRequested) ((MainWindow)Window.GetWindow(this)).ReturnToFocus();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            DateCardsViewport.ScrollToVerticalOffset(offset);
            foreach (var scroll in GroupScrolls(DateGroupBoard)) if (groups.TryGetValue((string)scroll.Tag, out var previous)) scroll.ScrollToVerticalOffset(previous);
        }));
    }
    private static IEnumerable<ScrollViewer> GroupScrolls(DependencyObject parent)
    {
        if (parent is ScrollViewer { Tag: string } scroll) yield return scroll;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            foreach (var child in GroupScrolls(VisualTreeHelper.GetChild(parent, index))) yield return child;
    }
}
