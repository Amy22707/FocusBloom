using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Restrictions;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using FocusBloom.Desktop.Views;

namespace FocusBloom.Desktop;

public partial class MainWindow : Window
{
    private bool _fullscreen;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResizeMode;
    private Rect _previousBounds;
    private bool _previousTopmost;
    private double _previousMinWidth;
    private double _previousMinHeight;
    private WindowState _lastWindowState = WindowState.Normal;
    private bool _layoutInitialized;
    private bool _browserFullscreen;
    private readonly MainViewModel _viewModel;
    private readonly StudyBrowserView _studyBrowser;

    public MainWindow(MainViewModel viewModel, IFocusStore store, string? browserUserDataFolder = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _studyBrowser = browserUserDataFolder is null ? new StudyBrowserView() : new StudyBrowserView(browserUserDataFolder);
        BrowserTab.Content = _studyBrowser;
        _studyBrowser.ReturnToFocusRequested += BrowserReturnToFocusRequested;
        _studyBrowser.FullscreenRequested += BrowserFullscreenRequested;
        DataContext = viewModel;
        viewModel.PropertyChanged += OnFocusStateChanged;
        Closed += (_, _) => { viewModel.PropertyChanged -= OnFocusStateChanged; _studyBrowser.Dispose(); };
        var savedJson = store.GetSetting(WindowLayout.SettingKey);
        var savedLayout = savedJson is null ? null : JsonSerializer.Deserialize<WindowLayout>(savedJson);
        SourceInitialized += (_, _) => RestoreWindowLayout(savedLayout);
        Closing += (_, _) =>
        {
            if (!_layoutInitialized) return;
            try { store.SaveSetting(WindowLayout.SettingKey, JsonSerializer.Serialize(CaptureWindowLayout())); }
            catch (Exception ex) { viewModel.ReportStatus($"窗口布局保存失败：{ex.Message}"); }
        };
        StateChanged += (_, _) =>
        {
            if (!_fullscreen && WindowState != WindowState.Minimized) _lastWindowState = WindowState;
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F11 || (e.Key == Key.Escape && _fullscreen))
            {
                if (e.Key == Key.F11 && !_fullscreen && MainTabs.SelectedItem == BrowserTab) BrowserFullscreenRequested(this, EventArgs.Empty);
                else ToggleFullscreen();
                e.Handled = true;
            }
        };
    }

    public event EventHandler? ExitRequested;
    public void ReturnToFocus()
    {
        _browserFullscreen = false;
        RestoreBrowserContent();
        MainTabs.SelectedIndex = 0;
        UpdateFullscreenContent();
        ShowFromTray();
    }
    public void DisposeBrowser() => _studyBrowser.Dispose();
    private void BrowserReturnToFocusRequested(object? sender, EventArgs e) => ReturnToFocus();
    private void OpenStudyBrowser_Click(object sender, RoutedEventArgs e) => OpenStudyBrowser();
    private void FullscreenOpenBrowserRequested(object? sender, EventArgs e) => OpenStudyBrowser();
    private void OpenStudyBrowser()
    {
        MainTabs.SelectedItem = BrowserTab;
        if (_fullscreen) { _browserFullscreen = true; UpdateFullscreenContent(); }
    }
    private void BrowserFullscreenRequested(object? sender, EventArgs e)
    {
        if (_fullscreen && _browserFullscreen) { ToggleFullscreen(); return; }
        _browserFullscreen = true;
        if (!_fullscreen) ToggleFullscreen();
        else UpdateFullscreenContent();
    }
    private void OnFocusStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_fullscreen && e.PropertyName == nameof(MainViewModel.IsFocusActive)) UpdateFullscreenContent();
    }

    private void UpdateFullscreenContent()
    {
        var browser = _fullscreen && _browserFullscreen;
        if (browser && BrowserFullscreenHost.Content is null)
        {
            BrowserTab.Content = null;
            BrowserFullscreenHost.Content = _studyBrowser;
        }
        if (!browser) RestoreBrowserContent();
        var timer = _fullscreen && !browser && _viewModel.IsFocusActive;
        WorkspaceView.Visibility = timer || browser ? Visibility.Collapsed : Visibility.Visible;
        FullscreenView.Visibility = timer ? Visibility.Visible : Visibility.Collapsed;
        BrowserFullscreenView.Visibility = browser ? Visibility.Visible : Visibility.Collapsed;
        _studyBrowser.SetFullscreenPresentation(browser);
        EnterFullscreenButton.Content = _fullscreen ? "退出全屏  ↙" : "全屏  ↗";
    }

    private void RestoreBrowserContent()
    {
        if (BrowserFullscreenHost.Content is null) return;
        BrowserFullscreenHost.Content = null;
        BrowserTab.Content = _studyBrowser;
    }
    private void SelectApplicationFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择允许操作的程序", Filter = "程序 (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        var name = FileVersionInfo.GetVersionInfo(dialog.FileName).FileDescription;
        ((MainViewModel)DataContext).Whitelist.AddApplication(new AllowedApplication(
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(dialog.FileName) : name, dialog.FileName));
    }
    private void SelectRunningApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow { Owner = this };
        if (picker.ShowDialog() == true) ((MainViewModel)DataContext).Whitelist.AddApplication(picker.SelectedApplication!);
    }

    public WindowLayout CaptureWindowLayout()
    {
        var bounds = _fullscreen ? _previousBounds
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var state = _fullscreen ? _previousState : _lastWindowState;
        return new WindowLayout(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            state == WindowState.Maximized, _fullscreen ? _previousTopmost : Topmost);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastWindowState;
        Activate();
        ApplicationRestrictionService.ActivateForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void RestoreWindowLayout(WindowLayout? savedLayout)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = savedLayout is null
            ? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle)
            : System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                (int)Math.Round(savedLayout.Left * dpi.DpiScaleX), (int)Math.Round(savedLayout.Top * dpi.DpiScaleY),
                (int)Math.Round(savedLayout.Width * dpi.DpiScaleX), (int)Math.Round(savedLayout.Height * dpi.DpiScaleY)));
        var area = screen.WorkingArea;
        var workArea = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        var layout = savedLayout?.FitTo(workArea) ?? WindowLayout.CreateDefault(workArea);
        MinWidth = Math.Min(WindowLayout.MinimumWidth, workArea.Width - 32);
        MinHeight = Math.Min(WindowLayout.MinimumHeight, workArea.Height - 32);
        Left = layout.Left;
        Top = layout.Top;
        Width = layout.Width;
        Height = layout.Height;
        Topmost = layout.Topmost;
        WindowState = layout.Maximized ? WindowState.Maximized : WindowState.Normal;
        _layoutInitialized = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ReturnToWindowRequested(object? sender, EventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).EnsureHandle());
            var dpi = VisualTreeHelper.GetDpi(this);
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _previousTopmost = Topmost;
            _previousMinWidth = MinWidth;
            _previousMinHeight = MinHeight;
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = MinHeight = 0;
            Left = screen.Bounds.Left / dpi.DpiScaleX;
            Top = screen.Bounds.Top / dpi.DpiScaleY;
            Width = screen.Bounds.Width / dpi.DpiScaleX;
            Height = screen.Bounds.Height / dpi.DpiScaleY;
            Topmost = true;
            UpdateFullscreenContent();
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            Left = _previousBounds.Left;
            Top = _previousBounds.Top;
            Width = _previousBounds.Width;
            Height = _previousBounds.Height;
            MinWidth = _previousMinWidth;
            MinHeight = _previousMinHeight;
            Topmost = _previousTopmost;
            WindowState = _previousState;
            _fullscreen = false;
            _browserFullscreen = false;
            UpdateFullscreenContent();
            _lastWindowState = _previousState;
        }
    }
}
