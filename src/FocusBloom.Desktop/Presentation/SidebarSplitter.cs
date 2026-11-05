using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FocusBloom.Desktop.Presentation;

public sealed class SidebarSplitter : Thumb
{
    public static readonly DependencyProperty PaneWidthProperty = DependencyProperty.Register(
        nameof(PaneWidth), typeof(double), typeof(SidebarSplitter),
        new FrameworkPropertyMetadata(220d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
    public double PaneWidth { get => (double)GetValue(PaneWidthProperty); set => SetValue(PaneWidthProperty, value); }

    public SidebarSplitter()
    {
        Cursor = Cursors.SizeWE;
        Focusable = true;
        DragDelta += (_, e) => Resize(e.HorizontalChange);
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right)) return;
            Resize(e.Key == Key.Left ? -10 : 10);
            e.Handled = true;
        };
    }

    private void Resize(double change)
    {
        if (Parent is not FrameworkElement parent) return;
        var maximum = Math.Min(360, Math.Max(160, parent.ActualWidth - 440));
        SetCurrentValue(PaneWidthProperty, Math.Clamp(PaneWidth + change, 160, maximum));
    }
}
