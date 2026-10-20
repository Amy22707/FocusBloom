using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Runtime.InteropServices;

namespace FocusBloom.Desktop.Presentation;

internal sealed class ReorderPreview : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    private sealed record ElementState(FrameworkElement Element, Transform Original, double Opacity, TranslateTransform Offset);
    private readonly ItemsControl _owner;
    private readonly object _source;
    private readonly Dictionary<FrameworkElement, ElementState> _states = [];
    private readonly FrameworkElement _root;
    private readonly Ghost? _ghost;
    private readonly Point _grab;
    private int _targetIndex = -1;
    public object? Target { get; private set; }

    public ReorderPreview(FrameworkElement handle, ItemsControl owner, Point grab)
    {
        _owner = owner; _source = handle.DataContext;
        _root = Window.GetWindow(owner)?.Content as FrameworkElement ?? owner;
        if (owner.ItemContainerGenerator.ContainerFromItem(_source) is not FrameworkElement source) return;
        _grab = handle.TranslatePoint(grab, source);
        var size = source.RenderSize;
        if (size.Width > 0 && size.Height > 0 && AdornerLayer.GetAdornerLayer(_root) is { } layer)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen()) drawing.DrawRectangle(new VisualBrush(source), null, new Rect(size));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual); bitmap.Freeze();
            _ghost = new Ghost(_root, bitmap, size); _ghost.Follow(source.TranslatePoint(new Point(), _root)); layer.Add(_ghost);
        }
        TrackElements();
        source.SetCurrentValue(UIElement.OpacityProperty, 0.16);
    }

    private void TrackElements()
    {
        foreach (var item in _owner.Items)
        {
            if (_owner.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement element || _states.ContainsKey(element)) continue;
            var offset = new TranslateTransform();
            var state = new ElementState(element, element.RenderTransform, element.Opacity, offset);
            var group = new TransformGroup(); group.Children.Add(state.Original); group.Children.Add(offset);
            element.SetCurrentValue(UIElement.RenderTransformProperty, group); _states.Add(element, state);
        }
    }
    private Rect Bounds(FrameworkElement element)
    {
        var point = element.TransformToAncestor(_owner).Transform(new Point());
        if (_states.TryGetValue(element, out var state)) { point.X -= state.Offset.X; point.Y -= state.Offset.Y; }
        return new Rect(point, element.RenderSize);
    }
    public void Update(DragEventArgs args)
    {
        TrackElements();
        _ghost?.Follow(args.GetPosition(_root) - (Vector)_grab);
        var point = args.GetPosition(_owner);
        var slots = _owner.Items.Cast<object>().Select((item, index) => new
        {
            Item = item, Index = index, Element = _owner.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement
        }).Where(slot => slot.Element is not null).Select(slot => (slot.Item, slot.Index, Element: slot.Element!, Rect: Bounds(slot.Element!))).ToArray();
        if (slots.Length == 0) return;
        var nearest = slots.OrderBy(slot => slot.Rect.Contains(point) ? -1 : (point - new Point(slot.Rect.Left + slot.Rect.Width / 2, slot.Rect.Top + slot.Rect.Height / 2)).LengthSquared).First();
        Target = nearest.Item;
        if (_targetIndex == nearest.Index) return;
        _targetIndex = nearest.Index;
        var sourceIndex = _owner.Items.IndexOf(_source);
        foreach (var slot in slots)
        {
            var destination = slot.Index == sourceIndex ? _targetIndex
                : sourceIndex < _targetIndex && slot.Index > sourceIndex && slot.Index <= _targetIndex ? slot.Index - 1
                : sourceIndex > _targetIndex && slot.Index >= _targetIndex && slot.Index < sourceIndex ? slot.Index + 1 : slot.Index;
            var destinationSlot = slots.FirstOrDefault(candidate => candidate.Index == destination);
            if (destinationSlot.Element is null) continue;
            var delta = destinationSlot.Rect.TopLeft - slot.Rect.TopLeft;
            var state = _states[slot.Element];
            Animate(state.Offset, TranslateTransform.XProperty, delta.X);
            Animate(state.Offset, TranslateTransform.YProperty, delta.Y);
        }
    }
    public void FollowMouse()
    {
        if (GetCursorPos(out var cursor)) FollowScreenPoint(new Point(cursor.X, cursor.Y));
    }
    internal void FollowScreenPoint(Point cursor) => _ghost?.Follow(_root.PointFromScreen(cursor) - (Vector)_grab);
    public void LeaveList()
    {
        if (_targetIndex < 0) return;
        Target = null; _targetIndex = -1;
        foreach (var state in _states.Values) { Animate(state.Offset, TranslateTransform.XProperty, 0); Animate(state.Offset, TranslateTransform.YProperty, 0); }
    }
    private static void Animate(TranslateTransform transform, DependencyProperty property, double target)
        => transform.BeginAnimation(property, new DoubleAnimation(target, TimeSpan.FromMilliseconds(170)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

    public void Dispose()
    {
        foreach (var state in _states.Values)
        {
            state.Offset.BeginAnimation(TranslateTransform.XProperty, null); state.Offset.BeginAnimation(TranslateTransform.YProperty, null);
            state.Element.SetCurrentValue(UIElement.RenderTransformProperty, state.Original);
            state.Element.SetCurrentValue(UIElement.OpacityProperty, state.Opacity);
        }
        _states.Clear();
        if (_ghost is not null) AdornerLayer.GetAdornerLayer(_root)?.Remove(_ghost);
    }
    private sealed class Ghost : Adorner
    {
        private readonly BitmapSource _image;
        private readonly Size _size;
        private Point _point;
        public Ghost(FrameworkElement root, BitmapSource image, Size size) : base(root) { _image = image; _size = size; IsHitTestVisible = false; }
        public void Follow(Point point) { _point = point; InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawingContext)
        {
            drawingContext.PushClip(new RectangleGeometry(new Rect(AdornedElement.RenderSize)));
            var bounds = new Rect(_point, new Size(_size.Width * 1.025, _size.Height * 1.025));
            var shadow = bounds; shadow.Offset(3, 5);
            drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)), null, shadow, 12, 12);
            drawingContext.PushOpacity(0.95); drawingContext.DrawImage(_image, bounds); drawingContext.Pop();
            drawingContext.Pop();
        }
    }
}
