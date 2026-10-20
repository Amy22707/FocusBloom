using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace FocusBloom.Desktop.Presentation;

public sealed class GroupMoveEventArgs(object item, object target) : RoutedEventArgs(GroupDragDrop.MoveRequestedEvent)
{
    public object Item { get; } = item;
    public object Target { get; } = target;
    public bool Moved { get; set; }
}

public static class GroupDragDrop
{
    private const string Format = "FocusBloom.GroupItem";
    private sealed class DragItem(object item) { public object Item { get; } = item; public ReorderPreview? Preview { get; set; } }
    private static readonly DependencyProperty StartProperty = DependencyProperty.RegisterAttached("Start", typeof(Point?), typeof(GroupDragDrop));
    private static readonly DependencyProperty HighlightProperty = DependencyProperty.RegisterAttached("Highlight", typeof(TargetHighlight), typeof(GroupDragDrop));
    private static readonly DependencyProperty LastScrollProperty = DependencyProperty.RegisterAttached("LastScroll", typeof(long), typeof(GroupDragDrop), new PropertyMetadata(0L));
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached("Source", typeof(bool), typeof(GroupDragDrop), new PropertyMetadata(false, SourceChanged));
    public static bool GetSource(DependencyObject element) => (bool)element.GetValue(SourceProperty);
    public static void SetSource(DependencyObject element, bool value) => element.SetValue(SourceProperty, value);
    public static readonly DependencyProperty AcceptTypeProperty = DependencyProperty.RegisterAttached("AcceptType", typeof(Type), typeof(GroupDragDrop), new PropertyMetadata(null, TargetChanged));
    public static Type? GetAcceptType(DependencyObject element) => (Type?)element.GetValue(AcceptTypeProperty);
    public static void SetAcceptType(DependencyObject element, Type? value) => element.SetValue(AcceptTypeProperty, value);
    public static readonly RoutedEvent MoveRequestedEvent = EventManager.RegisterRoutedEvent("MoveRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(GroupDragDrop));
    public static void AddMoveRequestedHandler(DependencyObject element, RoutedEventHandler handler) => ((UIElement)element).AddHandler(MoveRequestedEvent, handler);
    public static void RemoveMoveRequestedHandler(DependencyObject element, RoutedEventHandler handler) => ((UIElement)element).RemoveHandler(MoveRequestedEvent, handler);

    private static void SourceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement source) return;
        if ((bool)args.NewValue) { source.PreviewMouseLeftButtonDown += Press; source.PreviewMouseLeftButtonUp += Release; source.PreviewMouseMove += Move; }
        else { source.PreviewMouseLeftButtonDown -= Press; source.PreviewMouseLeftButtonUp -= Release; source.PreviewMouseMove -= Move; }
    }
    private static void Press(object sender, MouseButtonEventArgs args)
    {
        var source = (FrameworkElement)sender;
        for (var node = args.OriginalSource as DependencyObject; node is not null && node != source; node = VisualTreeHelper.GetParent(node))
            if (node is ReorderButton or ToggleButton or ScrollBar or TextBoxBase) return;
        source.SetValue(StartProperty, (Point?)args.GetPosition(source));
    }
    private static void Release(object sender, MouseButtonEventArgs args) => ((FrameworkElement)sender).ClearValue(StartProperty);
    private static void Move(object sender, MouseEventArgs args)
    {
        var source = (FrameworkElement)sender;
        if (args.LeftButton != MouseButtonState.Pressed || source.GetValue(StartProperty) is not Point start) return;
        var point = args.GetPosition(source);
        if (Math.Abs(point.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        source.ClearValue(StartProperty); var owner = ReorderButton.ParentItemsControl(source); if (owner is null) return;
        Mouse.Capture(null); args.Handled = true;
        var drag = new DragItem(source.DataContext) { Preview = new ReorderPreview(source, owner, start) };
        GiveFeedbackEventHandler feedback = (_, _) => drag.Preview?.FollowMouse(); source.GiveFeedback += feedback;
        try { DragDrop.DoDragDrop(source, new DataObject(Format, drag), DragDropEffects.Move); }
        finally { source.GiveFeedback -= feedback; drag.Preview?.Dispose(); }
    }
    private static object? Item(IDataObject data) => (data.GetData(Format) as DragItem)?.Item ?? ReorderButton.DraggedItem(data);
    private static void TargetChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement target) return;
        target.AllowDrop = args.NewValue is not null;
        if (args.OldValue is not null) { target.DragOver -= Over; target.DragLeave -= Leave; target.Drop -= Drop; }
        if (args.NewValue is not null) { target.DragOver += Over; target.DragLeave += Leave; target.Drop += Drop; }
    }
    private static void Over(object sender, DragEventArgs args)
    {
        var target = (FrameworkElement)sender;
        if (Item(args.Data) is not { } item || GetAcceptType(target)?.IsInstanceOfType(item) != true) return;
        ReorderButton.LeaveList(args.Data); Scroll(target, args);
        if (target.GetValue(HighlightProperty) is null && AdornerLayer.GetAdornerLayer(target) is { } layer)
        { var highlight = new TargetHighlight(target); layer.Add(highlight); target.SetValue(HighlightProperty, highlight); }
        args.Effects = DragDropEffects.Move; args.Handled = true;
    }
    private static void Leave(object sender, DragEventArgs args) => ClearHighlight((FrameworkElement)sender);
    private static void ClearHighlight(FrameworkElement target)
    {
        if (target.GetValue(HighlightProperty) is TargetHighlight highlight) AdornerLayer.GetAdornerLayer(target)?.Remove(highlight);
        target.ClearValue(HighlightProperty);
    }
    private static void Drop(object sender, DragEventArgs args)
    {
        var target = (FrameworkElement)sender; ClearHighlight(target);
        if (Item(args.Data) is not { } item || GetAcceptType(target)?.IsInstanceOfType(item) != true) return;
        ReorderButton.FinishDrag(args.Data);
        if (args.Data.GetData(Format) is DragItem drag) { drag.Preview?.Dispose(); drag.Preview = null; }
        var move = new GroupMoveEventArgs(item, target.DataContext); target.RaiseEvent(move);
        args.Effects = move.Moved ? DragDropEffects.Move : DragDropEffects.None; args.Handled = true;
    }
    private static void Scroll(FrameworkElement target, DragEventArgs args)
    {
        if (Environment.TickCount64 - (long)target.GetValue(LastScrollProperty) < 100) return;
        for (DependencyObject? node = target; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is not ScrollViewer scroll) continue; var point = args.GetPosition(scroll);
            if (scroll.ScrollableWidth > 0) { if (point.X < 28) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset - 36); else if (point.X > scroll.ActualWidth - 28) scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset + 36); }
            if (scroll.ScrollableHeight > 0) { if (point.Y < 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 30); else if (point.Y > scroll.ActualHeight - 28) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 30); }
        }
        target.SetValue(LastScrollProperty, Environment.TickCount64);
    }
    private sealed class TargetHighlight : Adorner
    {
        private readonly FrameworkElement _target;
        public TargetHighlight(FrameworkElement target) : base(target) { _target = target; IsHitTestVisible = false; }
        protected override void OnRender(DrawingContext drawing)
        {
            var brush = (Brush)_target.FindResource("AccentBrush");
            drawing.DrawRoundedRectangle(null, new Pen(brush, 2), new Rect(1, 1, Math.Max(0, _target.ActualWidth - 2), Math.Max(0, _target.ActualHeight - 2)), 12, 12);
        }
    }
}
