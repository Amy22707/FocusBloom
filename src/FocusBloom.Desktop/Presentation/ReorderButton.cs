using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System;
using System.Windows.Input;

namespace FocusBloom.Desktop.Presentation;

public sealed class ReorderEventArgs(OrderMove direction, object? target = null) : RoutedEventArgs(ReorderButton.ReorderRequestedEvent)
{
    public OrderMove Direction { get; } = direction;
    public object? Target { get; } = target;
}

public sealed class ReorderButton : Button
{
    private const string DragFormat = "FocusBloom.ItemOrder";
    private sealed class DragItem(ReorderButton handle, ItemsControl owner)
    {
        public ReorderButton Handle { get; } = handle;
        public ItemsControl Owner { get; } = owner;
        public ReorderPreview? Preview { get; set; }
    }
    private Point _start;
    private bool _pressed;
    private static readonly DependencyProperty LastScrollProperty = DependencyProperty.RegisterAttached("LastScroll", typeof(long), typeof(ReorderButton), new PropertyMetadata(0L));
    public static readonly DependencyProperty EnableDropProperty = DependencyProperty.RegisterAttached("EnableDrop", typeof(bool), typeof(ReorderButton), new PropertyMetadata(false, OnEnableDrop));
    public static bool GetEnableDrop(DependencyObject target) => (bool)target.GetValue(EnableDropProperty);
    public static void SetEnableDrop(DependencyObject target, bool value) => target.SetValue(EnableDropProperty, value);
    public static readonly RoutedEvent ReorderRequestedEvent = EventManager.RegisterRoutedEvent(nameof(ReorderRequested), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(ReorderButton));
    public event RoutedEventHandler ReorderRequested { add => AddHandler(ReorderRequestedEvent, value); remove => RemoveHandler(ReorderRequestedEvent, value); }
    public ReorderButton()
    {
        Content = "≡"; ToolTip = "拖动调整顺序，点击打开排序菜单"; Cursor = Cursors.SizeAll; Width = 24; Height = 24; Padding = new Thickness(0); FontSize = 17; Background = Brushes.Transparent;
        SetResourceReference(StyleProperty, "ReorderHandle");
        ContextMenu = new ContextMenu { PlacementTarget = this };
        ContextMenu.SetResourceReference(BackgroundProperty, "CardBrush");
        ContextMenu.SetResourceReference(ForegroundProperty, "TextBrush");
        ContextMenu.SetResourceReference(BorderBrushProperty, "BorderBrush");
        foreach (var (title, direction) in new[] { ("移到最前", OrderMove.First), ("前移", OrderMove.Previous), ("后移", OrderMove.Next), ("移到最后", OrderMove.Last) })
        {
            var item = new MenuItem { Header = title };
            item.Click += (_, _) => RaiseEvent(new ReorderEventArgs(direction));
            ContextMenu.Items.Add(item);
        }
        Click += (_, _) => ContextMenu.IsOpen = true;
        PreviewMouseLeftButtonDown += (_, args) => { _start = args.GetPosition(this); _pressed = true; };
        PreviewMouseLeftButtonUp += (_, _) => _pressed = false;
        PreviewMouseMove += (_, args) =>
        {
            if (!_pressed || args.LeftButton != MouseButtonState.Pressed) return;
            var point = args.GetPosition(this);
            if (Math.Abs(point.X - _start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            var container = ParentItemsControl(this);
            if (container is null || !GetEnableDrop(container)) return;
            _pressed = false; ReleaseMouseCapture(); args.Handled = true;
            var drag = new DragItem(this, container) { Preview = new ReorderPreview(this, container, _start) };
            GiveFeedbackEventHandler follow = (_, _) => drag.Preview?.FollowMouse();
            GiveFeedback += follow;
            try { DragDrop.DoDragDrop(this, new DataObject(DragFormat, drag), DragDropEffects.Move); }
            finally { GiveFeedback -= follow; drag.Preview?.Dispose(); }
        };
    }
    internal static ItemsControl? ParentItemsControl(DependencyObject item)
    {
        for (var parent = VisualTreeHelper.GetParent(item); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ItemsControl control) return control;
        return null;
    }
    internal static object? DraggedItem(IDataObject data) => (data.GetData(DragFormat) as DragItem)?.Handle.DataContext;
    internal static void LeaveList(IDataObject data)
    {
        if (data.GetData(DragFormat) is not DragItem item) return;
        item.Preview ??= new ReorderPreview(item.Handle, item.Owner, item.Handle._start);
        item.Preview.LeaveList();
    }
    internal static void FinishDrag(IDataObject data)
    {
        if (data.GetData(DragFormat) is not DragItem item) return;
        item.Preview?.Dispose(); item.Preview = null;
    }
    private static void OnEnableDrop(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ItemsControl list) return;
        list.AllowDrop = (bool)args.NewValue;
        if ((bool)args.NewValue) { list.DragOver += OnDragOver; list.Drop += OnDrop; }
        else { list.DragOver -= OnDragOver; list.Drop -= OnDrop; }
    }
    private static FrameworkElement? DropTarget(ItemsControl list, DragEventArgs args)
        => args.OriginalSource is DependencyObject source ? ItemsControl.ContainerFromElement(list, source) as FrameworkElement : null;
    private static void OnDragOver(object sender, DragEventArgs args)
    {
        var list = (ItemsControl)sender;
        if (args.Data.GetData(DragFormat) is not DragItem item || item.Owner != list) return;
        AutoScroll(list, args);
        item.Preview ??= new ReorderPreview(item.Handle, list, item.Handle._start);
        item.Preview.Update(args);
        args.Effects = DragDropEffects.Move; args.Handled = true;
    }
    private static void OnDrop(object sender, DragEventArgs args)
    {
        var list = (ItemsControl)sender;
        if (args.Data.GetData(DragFormat) is not DragItem item || item.Owner != list) return;
        var target = item.Preview?.Target ?? DropTarget(list, args)?.DataContext;
        item.Preview?.Dispose(); item.Preview = null;
        if (target is not null && !Equals(target, item.Handle.DataContext))
            item.Handle.RaiseEvent(new ReorderEventArgs(OrderMove.First, target));
        args.Handled = true;
    }
    private static void AutoScroll(ItemsControl list, DragEventArgs args)
    {
        if (Environment.TickCount64 - (long)list.GetValue(LastScrollProperty) < 100) return;
        var scroll = list is ListBox box ? box.Template.FindName("PART_ScrollViewer", box) as ScrollViewer : null;
        for (DependencyObject? parent = list; scroll is null && parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer found) scroll = found;
        if (scroll is null) return;
        var point = args.GetPosition(scroll);
        if (scroll.ScrollableWidth > 0 && point.X < 28) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 36);
        else if (scroll.ScrollableWidth > 0 && point.X > scroll.ActualWidth - 28) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 36);
        else if (point.Y < 28) scroll.LineUp();
        else if (point.Y > scroll.ActualHeight - 28) scroll.LineDown();
        else return;
        list.SetValue(LastScrollProperty, Environment.TickCount64);
    }
}
