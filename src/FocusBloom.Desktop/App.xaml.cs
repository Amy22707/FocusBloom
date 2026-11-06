using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using FocusBloom.Infrastructure.Cloud;
using Microsoft.Win32;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Core.Focus;
using FocusBloom.Infrastructure.Backup;
using System.Diagnostics;

namespace FocusBloom.Desktop;

public partial class App : Application
{
    private TrayIcon? _tray;
    private DispatcherTimer? _refresh;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private bool _exiting;
    private ApplicationRestrictionService? _restrictions;
    private KeepAwakeService? _keepAwake;
    private FocusState _lastFocusState;
    private WindowsReminderService? _reminders;
    private PreparedRestore? _restore;
    private bool _restartForAccount;
    private string? _updateInstaller;
    private string? _alternateDataDirectory;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        LoadingWindow? loadingWindow = null;
        string? dataDirectory = null;
        try
        {
            if (Array.IndexOf(e.Args, "--data-dir") is var index && index >= 0)
            {
                if (index + 1 >= e.Args.Length || string.IsNullOrWhiteSpace(e.Args[index + 1]))
                    throw new ArgumentException("--data-dir 需要指定独立资料目录。");
                _alternateDataDirectory = Path.GetFullPath(e.Args[index + 1]);
            }
            dataDirectory = _alternateDataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusBloom");
            WriteStartupStatus(dataDirectory, "Starting");
            Appearance.Apply(Appearance.Palettes[0], AppearanceMode.System);
            var profiles = new AccountProfiles(dataDirectory);
            profiles.Load();
            var store = await Task.Run(() => new SqliteFocusStore(profiles.CurrentDatabase));
            UiText.Configure(store.GetSetting(UiText.SettingKey));
            loadingWindow = new LoadingWindow();
            loadingWindow.Show();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            _viewModel = new MainViewModel(store);
            _viewModel.PropertyChanged += OnFocusPhaseChanged;
            _viewModel.ShortFocusDiscarded += OnShortFocusDiscarded;
            SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
            _window = new MainWindow(_viewModel, store, Path.Combine(profiles.Root, "browser"), profiles);
            if (_alternateDataDirectory is not null) _window.Title += " · 独立资料库";
            MainWindow = _window;
            _window.Closing += HideToTray;
            _window.ExitRequested += (_, _) => ExitApplication();
            _window.InstallUpdateRequested += installer => ExitApplication(installer);
            _window.AccountRestartRequested += () => { _restartForAccount = true; ExitApplication(); };
            _window.BackupRestoreRequested += prepared => { _restore = prepared; ExitApplication(); };
            _restrictions = new ApplicationRestrictionService(_viewModel, () => _window.ReturnToFocus());
            _keepAwake = new KeepAwakeService(_viewModel);
            _tray = new TrayIcon(_viewModel, ShowWindow, ExitApplication);
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _refresh.Tick += (_, _) =>
            {
                _viewModel.Tick();
                _restrictions.CheckForeground();
                _tray.Update($"一刻专注 · {_viewModel.StateText} {_viewModel.TimerText}");
                _reminders?.Tick();
            };
            _refresh.Start();
            _window.Show();
            loadingWindow.Close();
            WriteStartupStatus(dataDirectory, "Main window opened");
            if (_alternateDataDirectory is null)
            {
                _ = Dispatcher.InvokeAsync(() =>
                {
                    if (_exiting) return;
                    try { _reminders = new WindowsReminderService(store, _viewModel, entry => _window.OpenReminder(entry)); }
                    catch (Exception ex) { _viewModel.NotificationStatus = "系统提醒不可用：" + ex.Message; }
                }, DispatcherPriority.ApplicationIdle);
            }
            _window.ShowFocusRecovery();
            _window.ShowLimitNotice();
            _window.Backups?.StartAutomatic();
            _window.Accounts?.Start();
        }
        catch (Exception ex)
        {
            if (dataDirectory is not null) WriteStartupStatus(dataDirectory, ex.ToString());
            loadingWindow?.Close();
            MessageBox.Show($"一刻专注无法启动：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void WriteStartupStatus(string directory, string status)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "startup.log"), $"{DateTimeOffset.Now:O} PID {Environment.ProcessId} {status}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private void HideToTray(object? sender, CancelEventArgs e)
    {
        if (_exiting) return;
        e.Cancel = true;
        _window!.Hide();
    }

    private void ShowWindow()
    {
        _window!.ShowFromTray();
    }

    private void ExitApplication() => ExitApplication(null);

    private async void ExitApplication(string? updateInstaller)
    {
        if (_exiting) return;
        if (_restore is not null)
        {
            var otherInstance = false;
            foreach (var process in Process.GetProcessesByName("FocusBloom")) using (process) if (process.Id != Environment.ProcessId) otherInstance = true;
            if (otherInstance)
            {
                _restore.Dispose(); _restore = null;
                _window!.Backups!.ReportError(new InvalidOperationException("请先退出其他正在运行的一刻专注实例，再恢复备份。"));
                ConfirmDeletionWindow.ShowNotice(_window, "请先从托盘退出其他正在运行的一刻专注实例，再恢复备份。", "暂时不能恢复"); return;
            }
        }
        if (!_viewModel!.PrepareExit()) { _restore?.Dispose(); _restore = null; ShowWindow(); return; }
        _updateInstaller = updateInstaller;
        _exiting = true; _refresh?.Stop();
        foreach (Window window in Windows) window.IsEnabled = false;
        _restrictions?.Dispose();
        _restrictions = null;
        if (_window?.Backups is { } backups) await backups.StopAsync();
        _window?.CloseDesktopWidgetsForExit();
        Shutdown();
    }

    private void OnSystemAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_viewModel?.SelectedAppearanceMode.Mode == AppearanceMode.System)
            Dispatcher.BeginInvoke(new Action(() => _viewModel.RefreshSystemTheme()));
    }

    private void OnShortFocusDiscarded(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_exiting || _window is null) return;
            _window.ShowFromTray();
            ConfirmDeletionWindow.ShowNotice(_window, "本次有效专注不足10秒，未计入专注时长和记录。", "本次专注未计入");
        }));
    }

    private void OnFocusPhaseChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.State) || _viewModel is null || _lastFocusState == _viewModel.State) return;
        var state = _viewModel.State;
        if ((_lastFocusState is FocusState.Focusing or FocusState.Paused) && (state is FocusState.Resting or FocusState.Ended) && !_viewModel.EndedAtDurationLimit)
            _viewModel.Media.PlayReminder(false);
        else if (_lastFocusState == FocusState.Resting && state is FocusState.Ready or FocusState.Focusing) _viewModel.Media.PlayReminder(true);
        _lastFocusState = state;
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e); if (!e.Cancel) _window?.CloseDesktopWidgetsForExit();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _window?.Accounts?.Dispose();
        _window?.CloseDesktopWidgetsForExit();
        _refresh?.Stop();
        _reminders?.Dispose();
        _keepAwake?.Dispose();
        _viewModel?.Media.Dispose();
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnFocusPhaseChanged;
            _viewModel.ShortFocusDiscarded -= OnShortFocusDiscarded;
        }
        _restrictions?.Dispose();
        _window?.DisposeBrowser();
        SystemEvents.UserPreferenceChanged -= OnSystemAppearanceChanged;
        _tray?.Dispose();
        if (_restore is { } restore)
        {
            try { _window!.Backups!.Service.ApplyRestore(restore); }
            catch (Exception error) { MessageBox.Show("恢复未完成，当前数据已保留。恢复前备份位于：\n" + _window!.Backups!.LastBackup + "\n\n" + error.Message, "恢复备份", MessageBoxButton.OK, MessageBoxImage.Information); }
            finally { restore.Dispose(); }
            try { Restart(); }
            catch (Exception error) { MessageBox.Show("请重新打开一刻专注。\n" + error.Message, "重新启动", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        if (_restartForAccount && _restore is null)
        {
            try { _window?.Accounts?.CompleteLocalImport(); }
            catch (Exception error) { MessageBox.Show("本机数据未导入，原数据和导入前备份均已保留。\n" + error.Message, "导入本机数据", MessageBoxButton.OK, MessageBoxImage.Information); }
            try { Restart(); }
            catch (Exception error) { MessageBox.Show("请重新打开一刻专注。\n" + error.Message, "切换账号", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
        if (_updateInstaller is { } installer)
        {
            try
            {
                var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe")) { UseShellExecute = false };
                start.ArgumentList.Add("/i");
                start.ArgumentList.Add(installer);
                Process.Start(start)?.Dispose();
            }
            catch (Exception error)
            {
                MessageBox.Show(UiText.Format("无法启动更新安装程序。可手动打开 {0}\n{1}", installer, error.Message),
                    UiText.Get("检查更新"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        base.OnExit(e);
    }
    private void Restart()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true };
        if (_alternateDataDirectory is not null) { start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(_alternateDataDirectory); }
        Process.Start(start);
    }
}
