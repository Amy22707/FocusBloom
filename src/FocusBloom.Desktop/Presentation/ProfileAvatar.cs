using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusBloom.Desktop.Presentation;

public static class ProfileAvatar
{
    public static string FromFile(string path)
    {
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 512; image.UriSource = new Uri(Path.GetFullPath(path)); image.EndInit();
        var side = Math.Min(image.PixelWidth, image.PixelHeight);
        var cropped = new CroppedBitmap(image, new Int32Rect((image.PixelWidth - side) / 2, (image.PixelHeight - side) / 2, side, side));
        var drawing = new DrawingVisual(); using (var context = drawing.RenderOpen()) context.DrawImage(cropped, new Rect(0, 0, 256, 256));
        var bitmap = new RenderTargetBitmap(256, 256, 96, 96, PixelFormats.Pbgra32); bitmap.Render(drawing);
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 }; encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream(); encoder.Save(output);
        return "data:image/jpeg;base64," + Convert.ToBase64String(output.ToArray());
    }
    public static ImageSource? Read(string picture)
    {
        if (string.IsNullOrWhiteSpace(picture)) return null;
        try
        {
            if (!picture.StartsWith("data:image/", StringComparison.Ordinal)) return new BitmapImage(new Uri(picture, UriKind.Absolute));
            using var input = new MemoryStream(Convert.FromBase64String(picture[(picture.IndexOf(',') + 1)..]));
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = input; image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception error) when (error is FormatException or IOException or NotSupportedException or ArgumentException) { return null; }
    }
}
