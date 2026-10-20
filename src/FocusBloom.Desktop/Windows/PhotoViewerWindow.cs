using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public sealed class PhotoViewerWindow : Window
{
    public PhotoViewerWindow(string path)
    {
        Title = "查看图片"; Width = Math.Min(900, SystemParameters.WorkArea.Width - 70); Height = Math.Min(720, SystemParameters.WorkArea.Height - 70); WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this);
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var close = new Button { Content = "关闭", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var image = new Image { Stretch = Stretch.Uniform }; root.Children.Add(image);
        Loaded += async (_, _) => { image.Source = await Task.Run(() => ReflectionImages.ReadImage(path, 2400)); if (image.Source is null) root.Children.Add(new TextBlock { Text = "图片暂时无法读取。", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); };
    }
}
