using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Backup;

namespace FocusBloom.Desktop.Windows;

public sealed class BackupRestoreWindow : Window
{
    private bool _preparing;
    public PreparedRestore? Prepared { get; private set; }
    public BackupRestoreWindow(BackupSettingsViewModel model, string file, BackupManifest manifest)
    {
        Title = "恢复备份"; Width = 510; MaxHeight = SystemParameters.WorkArea.Height - 60; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this); DataContext = model; NameScope.SetNameScope(this, new NameScope());
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        var cancel = new Button { Content = "取消", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 0, 10, 0) }; actions.Children.Add(cancel);
        var restore = new Button { Content = "备份当前数据并恢复" }; restore.SetResourceReference(StyleProperty, "PrimaryButton"); RegisterName("ConfirmRestoreButton", restore); actions.Children.Add(restore);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(240, SystemParameters.WorkArea.Height - 190) });
        void Label(string text, bool muted = false) { var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 0, 0, 10) }; if (muted) label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); body.Children.Add(label); }
        body.Children.Add(new TextBlock { Text = "恢复这份记录", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) });
        Label(Path.GetFileName(file), true); Label($"备份时间：{manifest.CreatedAt.LocalDateTime:yyyy年M月d日 HH:mm}");
        var counts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 4, 0, 14) };
        foreach (var (name, count) in manifest.Counts) { var panel = new TextBlock { Text = $"{name}  {count}", FontSize = 12, Margin = new Thickness(0, 0, 8, 8) }; counts.Children.Add(panel); } body.Children.Add(counts);
        Label($"素材文件：{manifest.Assets.Count} 个 · {manifest.Assets.Sum(item => (double)item.Bytes) / 1024 / 1024:0.#} MB");
        if (manifest.Missing.Count > 0) { Label($"这份备份有 {manifest.Missing.Count} 个原文件未能打包；对应记录保留。", true); var list = new TextBlock { Text = string.Join("\n", manifest.Missing), FontSize = 11, TextWrapping = TextWrapping.Wrap }; var expander = new Expander { Header = "查看未打包文件", Content = list, Margin = new Thickness(0, 0, 0, 12) }; body.Children.Add(expander); }
        Label("当前数据会被这份备份替换，不会合并。恢复前会自动创建一份完整备份，随后重新启动软件。");
        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap }; status.SetBinding(TextBlock.TextProperty, new Binding("Status")); body.Children.Add(status);
        Closing += (_, args) => { if (_preparing) args.Cancel = true; };
        restore.Click += async (_, _) =>
        {
            _preparing = true; restore.IsEnabled = cancel.IsEnabled = false;
            try { Prepared = await model.PrepareRestoreAsync(file); _preparing = false; DialogResult = true; }
            catch (Exception error) { if (error is not OperationCanceledException) model.ReportError(error); _preparing = false; restore.IsEnabled = cancel.IsEnabled = true; }
        };
    }
}
