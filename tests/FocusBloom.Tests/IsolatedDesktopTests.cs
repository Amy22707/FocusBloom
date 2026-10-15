using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class IsolatedDesktopTests
{
    [Fact]
    public void WindowRendersOnSeparateDesktopWithoutChangingInputDesktop()
    {
        WpfTestHost.Run(() =>
        {
            var current = TestDesktop.Current; var input = TestDesktop.Input; Assert.NotEqual(input, current);
            var window = new Window { Width = 160, Height = 100, Title = "FocusBloom 测试隔离验证", Content = new System.Windows.Controls.TextBlock { Text = "独立测试桌面" }, ShowInTaskbar = false };
            try
            {
                window.Show(); window.UpdateLayout(); Assert.NotEqual(IntPtr.Zero, new WindowInteropHelper(window).Handle);
                var bitmap = new RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
                var directory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/qa")); Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "desktop-isolation.txt"), $"WindowDesktop={current}\nInputDesktopBefore={input}\nInputDesktopAfter={TestDesktop.Input}\nRendered={bitmap.PixelWidth}x{bitmap.PixelHeight}");
                Assert.Equal(input, TestDesktop.Input);
            }
            finally { window.Close(); }
        });
    }
}
