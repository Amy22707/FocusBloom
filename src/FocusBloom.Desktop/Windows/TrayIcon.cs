using System;
using System.Drawing;
using System.Windows.Forms;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _applicationIcon;

    public TrayIcon(MainViewModel viewModel, Action open, Action exit)
    {
        using var iconStream = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/FocusBloom;component/Resources/Branding/FocusBloom.ico"))!.Stream;
        using var sourceIcon = new Icon(iconStream);
        _applicationIcon = (Icon)sourceIcon.Clone();
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开一刻专注", null, (_, _) => open());
        menu.Items.Add("暂停／继续", null, (_, _) =>
        {
            if (viewModel.PauseCommand.CanExecute(null)) viewModel.PauseCommand.Execute(null);
            else if (viewModel.ResumeCommand.CanExecute(null)) viewModel.ResumeCommand.Execute(null);
        });
        menu.Items.Add("结束专注", null, (_, _) => viewModel.EndCommand.Execute(null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        _icon = new NotifyIcon
        {
            Icon = _applicationIcon,
            Text = "一刻专注 · FocusBloom",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
    }

    public void Update(string text) => _icon.Text = text.Length > 63 ? text[..63] : text;

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _applicationIcon.Dispose();
    }
}
