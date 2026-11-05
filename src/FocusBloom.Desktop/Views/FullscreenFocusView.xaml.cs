using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Views;

public partial class FullscreenFocusView : UserControl
{
    public FullscreenFocusView() => InitializeComponent();
    public event EventHandler? ReturnToWindowRequested;
    public event EventHandler? OpenBrowserRequested;
    public event EventHandler? ScreenshotRequested;
    internal BitmapSource CaptureScene() => FocusSceneCapture.Render(FocusScene, FocusHeader, FocusActions);
    private void Screenshot_Click(object sender, RoutedEventArgs e) => ScreenshotRequested?.Invoke(this, EventArgs.Empty);
    private void OpenBrowser_Click(object sender, RoutedEventArgs e) => OpenBrowserRequested?.Invoke(this, EventArgs.Empty);
    private void ReturnToWindow_Click(object sender, RoutedEventArgs e) => ReturnToWindowRequested?.Invoke(this, EventArgs.Empty);
}
