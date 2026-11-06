using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    [DllImport("libFocusBloomNotifications.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr focusbloom_frame([MarshalAs(UnmanagedType.LPUTF8Str)] string path, double seconds, out int width, out int height);
    [DllImport("libFocusBloomNotifications.dylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void focusbloom_frame_free(IntPtr pixels);
    private Image? _animatedImage;
    private string? _animatedPath;
    private DateTimeOffset _animationStarted;
    private DateTimeOffset _animationAt;
    private bool _decodingFrame;
    private WriteableBitmap? _animatedBitmap;

    private void SetAnimation(Image image, string? file)
    {
        _animatedImage = image;
        var extension = Path.GetExtension(file)?.ToLowerInvariant();
        var path = OperatingSystem.IsMacOS() && extension is ".gif" or ".mp4" or ".mov" or ".m4v" ? file : null;
        if (_animatedPath != path) { _animatedPath = path; _animationStarted = DateTimeOffset.UtcNow; }
    }
    private async Task UpdateAnimation()
    {
        if (_decodingFrame || _animatedPath is not { } path || _animatedImage is not { } image || !image.IsEffectivelyVisible ||
            DateTimeOffset.UtcNow - _animationAt < TimeSpan.FromMilliseconds(100)) return;
        _decodingFrame = true;
        _animationAt = DateTimeOffset.UtcNow;
        try
        {
            var elapsed = (DateTimeOffset.UtcNow - _animationStarted).TotalSeconds;
            var frame = await Task.Run(() => DecodeFrame(path, elapsed));
            if (frame is null) { _animatedPath = null; return; }
            if (_animatedPath != path || _animatedImage != image) { frame.Dispose(); return; }
            var previous = _animatedBitmap;
            _animatedBitmap = frame; image.Source = frame; previous?.Dispose();
        }
        catch (Exception error) { _animatedPath = null; Report(error.Message); }
        finally { _decodingFrame = false; }
    }
    private static unsafe WriteableBitmap? DecodeFrame(string path, double seconds)
    {
        var pixels = focusbloom_frame(path, seconds, out var width, out var height);
        if (pixels == IntPtr.Zero) return null;
        try
        {
            var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var target = bitmap.Lock();
            for (var row = 0; row < height; row++)
                Buffer.MemoryCopy((byte*)pixels + row * width * 4, (byte*)target.Address + row * target.RowBytes, target.RowBytes, width * 4);
            return bitmap;
        }
        finally { focusbloom_frame_free(pixels); }
    }
}
