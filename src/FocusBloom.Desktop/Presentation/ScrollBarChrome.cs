using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FocusBloom.Desktop.Presentation;

public static class ScrollBarChrome
{
    public static readonly DependencyProperty HideCornerProperty = DependencyProperty.RegisterAttached("HideCorner", typeof(bool), typeof(ScrollBarChrome), new PropertyMetadata(false, Changed));
    public static bool GetHideCorner(DependencyObject element) => (bool)element.GetValue(HideCornerProperty);
    public static void SetHideCorner(DependencyObject element, bool value) => element.SetValue(HideCornerProperty, value);
    private static void Changed(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ScrollBar bar) return;
        if ((bool)args.NewValue) bar.Loaded += Loaded; else bar.Loaded -= Loaded;
    }
    private static void Loaded(object sender, RoutedEventArgs args)
    {
        if (VisualTreeHelper.GetParent((ScrollBar)sender) is not Grid grid) return;
        foreach (var corner in grid.Children.OfType<Rectangle>().Where(rectangle => Grid.GetColumn(rectangle) == 1 && Grid.GetRow(rectangle) == 1)) corner.Fill = Brushes.Transparent;
    }
}
