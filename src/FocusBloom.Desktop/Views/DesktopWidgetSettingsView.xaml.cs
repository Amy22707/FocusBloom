using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class DesktopWidgetSettingsView : UserControl
{
    public DesktopWidgetSettingsView() => InitializeComponent();
    private void Configure_Click(object sender, RoutedEventArgs args) => new DesktopWidgetSettingsWindow((MainViewModel)Window.GetWindow(this).DataContext, (DesktopWidgetOption)((Button)sender).DataContext) { Owner = Window.GetWindow(this) }.ShowDialog();
}
