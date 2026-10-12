using System;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class TimerFloatWindow : Window
{
    private readonly MainViewModel _model;
    private readonly IFocusStore _store;
    private readonly Action _openMain;
    private bool _ready;
    private bool _closingForExit;

    public TimerFloatWindow(MainViewModel model, IFocusStore store, Action openMain)
    {
        InitializeComponent();
        _model = model; _store = store; _openMain = openMain;
        DataContext = model;
        OptionsButton.ContextMenu.DataContext = model;
        model.PropertyChanged += OnPreferenceChanged;
        Closed += (_, _) => model.PropertyChanged -= OnPreferenceChanged;
        SourceInitialized += (_, _) =>
        {
            RestoreLayout();
            ApplyTransparency();
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(OnWindowMessage);
        };
        Closing += OnClosing;
    }

    public TimerFloatLayout CaptureLayout() => new(Left, Top, Width, Height, Topmost,
        System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName);

    public void SaveLayout()
    {
        if (!_ready) return;
        try { _store.SaveSetting(TimerFloatLayout.SettingKey, JsonSerializer.Serialize(CaptureLayout())); }
        catch (Exception ex) { _model.ReportStatus($"浮窗位置保存失败：{ex.Message}"); }
    }

    public void FitToScreen()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var fitted = CaptureLayout().FitTo(new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY));
        Left = fitted.Left; Top = fitted.Top; Width = fitted.Width; Height = fitted.Height;
    }

    private void RestoreLayout()
    {
        var json = _store.GetSetting(TimerFloatLayout.SettingKey);
        var saved = json is null ? null : JsonSerializer.Deserialize<TimerFloatLayout>(json);
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == saved?.Monitor)
            ?? (saved is null ? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle)
                : System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                    (int)(saved.Left * dpi.DpiScaleX), (int)(saved.Top * dpi.DpiScaleY),
                    (int)(saved.Width * dpi.DpiScaleX), (int)(saved.Height * dpi.DpiScaleY))));
        var area = screen.WorkingArea;
        var workArea = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY, area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        var layout = saved?.FitTo(workArea) ?? TimerFloatLayout.CreateDefault(workArea);
        MinWidth = Math.Min(TimerFloatLayout.MinimumWidth, workArea.Width - 16);
        MinHeight = Math.Min(TimerFloatLayout.MinimumHeight, workArea.Height - 16);
        Left = layout.Left; Top = layout.Top; Width = layout.Width; Height = layout.Height;
        Topmost = layout.Topmost;
        UpdateControls();
        _ready = true;
    }

    private void UpdateControls()
    {
        PinItem.IsChecked = Topmost;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0232) SaveLayout();
        return IntPtr.Zero;
    }

    private void DragArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        DragMove(); SaveLayout();
    }
    private void Pin_Click(object sender, RoutedEventArgs e) { Topmost = !Topmost; UpdateControls(); SaveLayout(); }
    private void Options_Click(object sender, RoutedEventArgs e)
    {
        SolidBackgroundItem.IsChecked = !_model.TimerFloatUseBackground;
        CurrentBackgroundItem.IsChecked = _model.TimerFloatUseBackground;
        PinItem.IsChecked = Topmost;
        OptionsButton.ContextMenu.PlacementTarget = OptionsButton;
        OptionsButton.ContextMenu.IsOpen = true;
    }
    private void SolidBackground_Click(object sender, RoutedEventArgs e) => _model.TimerFloatUseBackground = false;
    private void CurrentBackground_Click(object sender, RoutedEventArgs e) => _model.TimerFloatUseBackground = true;
    private void Hide_Click(object sender, RoutedEventArgs e) => _model.TimerFloatVisible = false;
    private void OpenMain_Click(object sender, RoutedEventArgs e) => _openMain();
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SaveLayout();
        if (_closingForExit) return;
        e.Cancel = true;
        _model.TimerFloatVisible = false;
    }
    public void CloseForExit() { _closingForExit = true; Close(); }

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.TimerFloatTransparency)) ApplyTransparency();
    }

    private void ApplyTransparency()
    {
        Opacity = 1 - _model.TimerFloatTransparency / 100;
    }
}
