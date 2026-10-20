using System;
using System.Windows;
using System.Windows.Media;
using FocusBloom.Core.Reflection;

namespace FocusBloom.Desktop.Presentation;

public sealed class MoodIcon : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(int), typeof(MoodIcon), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static Color ColorFor(int value) => (Color)ColorConverter.ConvertFromString(ReflectionRules.Mood(value).Color);
    public static SolidColorBrush BrushFor(int value) => new(ColorFor(value));
    protected override Size MeasureOverride(Size availableSize) => new(Math.Min(40, availableSize.Width), Math.Min(40, availableSize.Height));
    protected override void OnRender(DrawingContext drawing)
    {
        if (Value == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = Math.Min(ActualWidth, ActualHeight); var color = ColorFor(Value);
        drawing.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2)); drawing.PushTransform(new ScaleTransform(size / 48, size / 48));
        var ink = new SolidColorBrush(Color.FromRgb(65, 71, 82)); var pen = new Pen(ink, 2.1) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var light = Color.FromRgb((byte)(color.R + (255 - color.R) * .18), (byte)(color.G + (255 - color.G) * .18), (byte)(color.B + (255 - color.B) * .18));
        var face = new LinearGradientBrush(light, color, 90);
        drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(15, 0, 0, 0)), null, new(24, 44), 15, 2);
        Path("M24,3 C37,2 45,12 45,24 C47,37 36,45 24,45 C11,46 2,36 3,24 C2,11 12,2 24,3 Z", face);
        drawing.DrawEllipse(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)), null, new(17, 10), 6, 2);
        if (Value is 4 or 5)
        { var blush = new SolidColorBrush(Color.FromArgb(90, 217, 111, 123)); drawing.DrawEllipse(blush, null, new(12, 28), 3, 1.7); drawing.DrawEllipse(blush, null, new(36, 28), 3, 1.7); }
        switch (Value)
        {
            case 1:
                Eyes(); Stroke("M12,17 L20,15 M28,15 L36,17 M18,34 Q24,29 30,34");
                Path("M35,26 C31,31 32,34 35,34 C38,34 39,31 35,26 Z", new SolidColorBrush(Color.FromRgb(112, 155, 198))); break;
            case 2:
                Stroke("M12,22 L20,21 M28,21 L36,22 M14,26 L19,26 M29,26 L34,26"); drawing.DrawEllipse(ink, null, new(24, 33), 2.8, 2); break;
            case 3:
                Stroke("M13,22 Q17,24 21,22 M27,22 Q31,24 35,22 M20,31 Q24,34 28,31"); break;
            case 4:
                Eyes(); Stroke("M17,30 Q24,37 31,30"); break;
            case 5:
                Stroke("M12,22 Q17,17 22,22 M26,22 Q31,17 36,22"); OpenSmile(); break;
            case 6:
                Stroke("M12,19 L20,23 L12,25 M36,19 L28,23 L36,25 M17,34 L21,31 L25,34 L30,31");
                Path("M36,1 L42,1 L39,6 L44,6 L36,14 L38,8 L34,8 Z", new SolidColorBrush(Color.FromRgb(129, 108, 171))); break;
        }
        drawing.Pop(); drawing.Pop();
        void Path(string data, Brush brush) => drawing.DrawGeometry(brush, null, Geometry.Parse(data));
        void Stroke(string data) => drawing.DrawGeometry(null, pen, Geometry.Parse(data));
        void Eyes() { drawing.DrawEllipse(ink, null, new(17, 23), 1.65, 2.1); drawing.DrawEllipse(ink, null, new(31, 23), 1.65, 2.1); }
        void OpenSmile() { Path("M17,29 Q24,31 31,29 C31,40 17,40 17,29 Z", ink); Path("M20,35 Q24,32 28,35 Q24,39 20,35 Z", new SolidColorBrush(Color.FromRgb(218, 134, 142))); }
    }
}
