using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop;

public partial class MainWindow
{
    private ReleaseUpdate? _releaseUpdate;
    public event Action<string>? InstallUpdateRequested;

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        UpdateStatus.Text = UiText.Get("正在检查更新…");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            _releaseUpdate = await ReleaseUpdates.CheckAsync(timeout.Token);
            if (_releaseUpdate is null)
                UpdateStatus.Text = UiText.Format("当前版本 {0} 已是最新版本。", ReleaseUpdates.CurrentVersion);
            else
            {
                UpdateStatus.Text = UiText.Format("当前版本 {0}，可更新到 {1}。", ReleaseUpdates.CurrentVersion, _releaseUpdate.Version);
                InstallUpdateButton.Content = UiText.Get(ReleaseUpdates.IsInstalled ? "下载安装更新" : "打开下载页面");
                InstallUpdateButton.Visibility = Visibility.Visible;
            }
        }
        catch (Exception error) { UpdateStatus.Text = UiText.Format("检查更新失败：{0}", error.Message); }
        finally { CheckUpdatesButton.IsEnabled = true; }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_releaseUpdate is not { } update) return;
        if (!ReleaseUpdates.IsInstalled)
        {
            try { Process.Start(new ProcessStartInfo(ReleaseUpdates.ReleasesPage) { UseShellExecute = true })?.Dispose(); }
            catch (Exception error) { UpdateStatus.Text = UiText.Format("检查更新失败：{0}", error.Message); }
            return;
        }
        if (!_viewModel.CanConfigure || _viewModel.HasUnsavedRecord)
        {
            UpdateStatus.Text = UiText.Get("请先结束本次专注并保存记录，再安装更新。");
            return;
        }
        InstallUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = false;
        UpdateStatus.Text = UiText.Get("正在下载安装包…");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            var installer = await ReleaseUpdates.DownloadAsync(update, timeout.Token);
            if (!_viewModel.CanConfigure || _viewModel.HasUnsavedRecord)
            {
                UpdateStatus.Text = UiText.Get("请先结束本次专注并保存记录，再安装更新。");
                return;
            }
            UpdateStatus.Text = UiText.Get("安装包已下载，正在退出一刻专注并打开安装程序。");
            InstallUpdateRequested?.Invoke(installer);
        }
        catch (Exception error) { UpdateStatus.Text = UiText.Format("下载更新失败：{0}", error.Message); }
        finally { InstallUpdateButton.IsEnabled = CheckUpdatesButton.IsEnabled = true; }
    }
}
