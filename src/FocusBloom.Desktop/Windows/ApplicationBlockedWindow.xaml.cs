using System;
using System.Windows;

namespace FocusBloom.Desktop.Windows;

public partial class ApplicationBlockedWindow : Window
{
    public ApplicationBlockedWindow() => InitializeComponent();
    public event EventHandler? ReturnToFocusRequested;
    public void SetApplication(string name) => ApplicationName.Text = $"{name} 暂时不能操作";
    private void ReturnToFocus_Click(object sender, RoutedEventArgs e) => ReturnToFocusRequested?.Invoke(this, EventArgs.Empty);
}
