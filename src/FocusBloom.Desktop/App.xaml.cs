using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Microsoft.Win32;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Core.Focus;

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

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Appearance.Apply(Appearance.Palettes[0], AppearanceMode.System);
        var loadingWindow = new LoadingWindow();
        loadingWindow.Show();
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FocusBloom", "guest");
            var store = await Task.Run(() => new SqliteFocusStore(Path.Combine(dataDirectory, "focusbloom.db")));
            _viewModel = new MainViewModel(store);
            _viewModel.PropertyChanged += OnFocusPhaseChanged;
            _viewModel.ShortFocusDiscarded += OnShortFocusDiscarded;
            SystemEvents.UserPreferenceChanged += OnSystemAppearanceChanged;
            _window = new MainWindow(_viewModel, store);
            MainWindow = _window;
            _window.Closing += HideToTray;
            _window.ExitRequested += (_, _) => ExitApplication();
            _restrictions = new ApplicationRestrictionService(_viewModel, () => _window.ReturnToFocus());
            _keepAwake = new KeepAwakeService(_viewModel);
            _tray = new TrayIcon(_viewModel, ShowWindow, ExitApplication);
            _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _refresh.Tick += (_, _) =>
            {
                _viewModel.Tick();
                _restrictions.CheckForeground();
                _tray.Update($"一刻专注 · {_viewModel.StateText} {_viewModel.TimerText}");
            };
            _refresh.Start();
            _window.Show();
            loadingWindow.Close();
        }
        catch (Exception ex)
        {
            loadingWindow.Close();
            MessageBox.Show($"一刻专注无法启动：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
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

    private void ExitApplication()
    {
        if (!_viewModel!.PrepareExit()) { ShowWindow(); return; }
        _restrictions?.Dispose();
        _restrictions = null;
        _exiting = true;
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
        if (_lastFocusState is FocusState.Focusing or FocusState.Paused && state is FocusState.Resting or FocusState.Ended)
            _viewModel.Media.PlayReminder(false);
        else if (_lastFocusState == FocusState.Resting && state == FocusState.Ready) _viewModel.Media.PlayReminder(true);
        _lastFocusState = state;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refresh?.Stop();
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
        base.OnExit(e);
    }
}
