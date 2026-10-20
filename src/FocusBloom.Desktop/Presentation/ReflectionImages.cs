using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Presentation;

public sealed class ReflectionImages(string profileDirectory)
{
    public string DirectoryPath { get; } = Path.Combine(profileDirectory, "reflection-images");
    public string ImportFile(string source)
    {
        if (ReadImage(source, 256) is null) throw new InvalidDataException("无法读取这张图片。请选择 PNG、JPG、GIF 或 BMP 图片。");
        Directory.CreateDirectory(DirectoryPath); var target = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + Path.GetExtension(source)); File.Copy(source, target); return target;
    }
    public string SavePastedImage(BitmapSource image)
    {
        Directory.CreateDirectory(DirectoryPath); var target = Path.Combine(DirectoryPath, Guid.NewGuid().ToString("N") + ".png");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var output = File.Create(target); encoder.Save(output); return target;
    }
    public void DeleteCopy(string path)
    { if (string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(DirectoryPath), StringComparison.OrdinalIgnoreCase)) File.Delete(path); }
    public static ImageSource? ReadImage(string path, int width)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = width; image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException) { return null; }
    }
}

public sealed class ReflectionPhoto(string path) : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private Task? _loading;
    private ImageSource? _thumbnail;
    public string Path { get; } = path;
    public ImageSource? Thumbnail { get { _loading ??= Load(); return _thumbnail; } }
    private async Task Load()
    {
        var image = await Task.Run(() => ReflectionImages.ReadImage(Path, 640)).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() => { _thumbnail = image; Notify(nameof(Thumbnail)); });
    }
}
