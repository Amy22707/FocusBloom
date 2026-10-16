using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow : Window
{
    private readonly MainViewModel _main;
    private readonly IFocusStore _store;
    private readonly DesktopWidgetManager _manager;
    private readonly Action<DesktopWidgetKind, Guid?, DateOnly?> _open;
    private bool _ready, _applying, _rendering, _nativeLevelReady, _deferredRender;
    private DatePicker? _datePicker;
    private bool _nativeTopmost;
    private DateOnly _anchor = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _lastDay = DateOnly.FromDateTime(DateTime.Today);
    private int _page;
    private long _minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
    public DesktopWidgetOption Option { get; }
    public DesktopWidgetWindow(MainViewModel main, IFocusStore store, DesktopWidgetOption option, DesktopWidgetManager manager, Action<DesktopWidgetKind, Guid?, DateOnly?> open)
    {
        InitializeComponent(); _main = main; _store = store; Option = option; _manager = manager; _open = open; DataContext = option; Title = "一刻专注 · " + option.Name;
        MinWidth = option.MinimumWidth; MinHeight = option.MinimumHeight; Width = option.Width; Height = option.Height;
        SourceInitialized += (_, _) =>
        {
            ApplyPreferences(); _ready = true;
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook((IntPtr hwnd, int message, IntPtr w, IntPtr l, ref bool handled) => { if (message == 0x0232) SaveLayout(); return IntPtr.Zero; });
        };
        Loaded += (_, _) => { ApplyPreferences(); Render(); }; Closing += (_, _) => SaveLayout();
    }
    public void ApplyPreferences()
    {
        if (_applying) return; _applying = true;
        try
        {
            var settings = Option.Preferences; var dpi = VisualTreeHelper.GetDpi(this);
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(item => item.DeviceName == settings.Monitor)
                ?? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle); var bounds = screen.WorkingArea;
            var area = new Rect(bounds.Left / dpi.DpiScaleX, bounds.Top / dpi.DpiScaleY, bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY);
            MinWidth = Math.Min(Option.MinimumWidth, area.Width - 16); MinHeight = Math.Min(Option.MinimumHeight, area.Height - 16);
            Width = Math.Clamp(settings.Width, MinWidth, area.Width - 16); Height = Math.Clamp(settings.Height, MinHeight, area.Height - 16);
            Left = Math.Clamp(settings.Left ?? area.Right - Width - 24 - (int)Option.Kind * 44, area.Left + 8, area.Right - Width - 8);
            Top = Math.Clamp(settings.Top ?? area.Top + 24 + (int)Option.Kind * 36, area.Top + 8, area.Bottom - Height - 8);
            Topmost = settings.Topmost; Opacity = 1 - settings.Transparency / 100;
            if (IsVisible && (!_nativeLevelReady || _nativeTopmost != settings.Topmost))
                ApplyWindowLevel();
            ApplyBackground();
        }
        finally { _applying = false; }
    }
    public void ApplyWindowLevel()
    {
        if (!IsVisible) return;
        Topmost = Option.Topmost;
        SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(Topmost ? -1 : -2), 0, 0, 0, 0, 0x0213);
        _nativeTopmost = Topmost; _nativeLevelReady = true;
    }
    public void SaveLayout()
    {
        if (!_ready || _applying) return;
        Option.SaveBounds(Left, Top, Width, Height, System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName);
    }
    private void Drag_MouseDown(object sender, MouseButtonEventArgs args) { if (args.LeftButton == MouseButtonState.Pressed) { DragMove(); SaveLayout(); } }
    private void Open_Click(object sender, RoutedEventArgs args) => _open(Option.Kind, null, Option.Kind == DesktopWidgetKind.Focus ? DateOnly.FromDateTime(DateTime.Today) : _anchor);
    private void Options_Click(object sender, RoutedEventArgs args) => new DesktopWidgetSettingsWindow(_main, Option) { Owner = this }.ShowDialog();
    private void Hide_Click(object sender, RoutedEventArgs args) => Option.Enabled = false;
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs args) { if (Math.Abs(args.NewSize.Width - args.PreviousSize.Width) > 1 || Math.Abs(args.NewSize.Height - args.PreviousSize.Height) > 1) Render(); }
    public void Render()
    {
        if (!_ready || _rendering || WidgetViewport.ActualWidth < 100) return;
        if (_datePicker?.IsDropDownOpen == true) { _deferredRender = true; return; }
        _rendering = true; _datePicker = null;
        var offset = WidgetViewport.VerticalOffset;
        try
        {
            WidgetBody.Children.Clear(); WidgetToolbar.Children.Clear();
            switch (Option.Kind)
            {
                case DesktopWidgetKind.Focus: DrawFocus(); break;
                case DesktopWidgetKind.Todos: DrawTodos(); break;
                case DesktopWidgetKind.Dates: DrawDates(); break;
                case DesktopWidgetKind.Habits: DrawHabits(); break;
                case DesktopWidgetKind.Mood: DrawMood(); break;
                case DesktopWidgetKind.Calendar: DrawCalendar(); break;
            }
            WidgetViewport.ScrollToVerticalOffset(offset);
        }
        finally { _rendering = false; }
    }
    public void Tick()
    {
        var today = DateOnly.FromDateTime(DateTime.Today); if (today != _lastDay) { if (_anchor == _lastDay) _anchor = today; _lastDay = today; Render(); }
        if (Option.Kind == DesktopWidgetKind.Focus) UpdateFocus();
        var minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
        if (_minute != minute) { _minute = minute; if (Option.Kind != DesktopWidgetKind.Focus || Option.View != "数字") Render(); }
    }
    private TextBlock Text(string text, double size = 12, bool muted = false)
    { var label = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedBrush" : "TextBrush"); return label; }
    private Button Tool(string text, Action action)
    { var button = new Button { Content = text, Style = (Style)FindResource("WidgetTool"), Margin = new Thickness(3, 0, 0, 0) }; button.Click += (_, _) => action(); WidgetToolbar.Children.Add(button); return button; }
    private Border Card(UIElement child, double padding = 10)
    { var card = new Border { Child = child, Padding = new Thickness(padding), Margin = new Thickness(0, 0, 0, 7), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1) }; card.SetResourceReference(Border.BackgroundProperty, "FieldBrush"); card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); return card; }
    private void MonthTools()
    {
        Tool("‹", () => { _anchor = _anchor.AddMonths(-1); Render(); }); Tool("今天", () => { _anchor = DateOnly.FromDateTime(DateTime.Today); Render(); }); Tool("›", () => { _anchor = _anchor.AddMonths(1); Render(); });
    }
    private void MonthPicker()
    {
        var picker = new DatePicker { SelectedDate = _anchor.ToDateTime(TimeOnly.MinValue), MinHeight = 34, Width = 150, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), ToolTip = "跳转年月日期" };
        _datePicker = picker; picker.CalendarClosed += (_, _) => { if (_deferredRender) { _deferredRender = false; Dispatcher.BeginInvoke(new Action(Render)); } };
        picker.SelectedDateChanged += (_, _) => { if (picker.SelectedDate is { } date && DateOnly.FromDateTime(date) != _anchor) { _anchor = DateOnly.FromDateTime(date); Render(); } }; WidgetBody.Children.Add(picker);
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
}
