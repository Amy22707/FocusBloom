using System;
using System.Windows;
using System.Windows.Controls;

namespace FocusBloom.Desktop.Views;

public partial class FullscreenFocusView : UserControl
{
    public FullscreenFocusView() => InitializeComponent();
    public event EventHandler? ReturnToWindowRequested;
    public event EventHandler? OpenBrowserRequested;
    private void OpenBrowser_Click(object sender, RoutedEventArgs e) => OpenBrowserRequested?.Invoke(this, EventArgs.Empty);
    private void ReturnToWindow_Click(object sender, RoutedEventArgs e) => ReturnToWindowRequested?.Invoke(this, EventArgs.Empty);
}
