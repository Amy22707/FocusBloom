using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusBloom.Desktop.Presentation;

internal static class FocusSceneCapture
{
    public static BitmapSource Render(FrameworkElement scene, UIElement header, UIElement actions)
    {
        var headerOpacity = header.Opacity;
        var actionsOpacity = actions.Opacity;
        header.SetCurrentValue(UIElement.OpacityProperty, 0d);
        actions.SetCurrentValue(UIElement.OpacityProperty, 0d);
        try
        {
            var dpi = VisualTreeHelper.GetDpi(scene);
            var image = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(scene.ActualWidth * dpi.DpiScaleX)),
                Math.Max(1, (int)Math.Ceiling(scene.ActualHeight * dpi.DpiScaleY)),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
                drawing.DrawRectangle(new VisualBrush(scene), null, new Rect(scene.RenderSize));
            image.Render(visual);
            image.Freeze();
            return image;
        }
        finally
        {
            header.SetCurrentValue(UIElement.OpacityProperty, headerOpacity);
            actions.SetCurrentValue(UIElement.OpacityProperty, actionsOpacity);
        }
    }
}
