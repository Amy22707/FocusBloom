using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Core.Restrictions;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class ApplicationRestrictionService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Action _returnToFocus;
    private readonly WinEventCallback _callback;
    private readonly IntPtr _hook;
    private ApplicationBlockedWindow? _blockedWindow;
    private IntPtr _lastAllowedWindow;

    public ApplicationRestrictionService(MainViewModel viewModel, Action returnToFocus)
    {
        _viewModel = viewModel;
        _returnToFocus = returnToFocus;
        _callback = (_, _, _, _, _, _, _) => { _lastAllowedWindow = IntPtr.Zero; CheckForeground(); };
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 2);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法监测前台应用。");
        _viewModel.RestrictionsChanged += OnRestrictionsChanged;
    }

    public void CheckForeground() => CheckWindow(GetForegroundWindow());

    private void CheckWindow(IntPtr window)
    {
        if (!_viewModel.RestrictionsActive || window == IntPtr.Zero) return;
        if (_blockedWindow is not null && window == new WindowInteropHelper(_blockedWindow).Handle) return;
        if (window == _lastAllowedWindow) return;
        var owner = GetAncestor(window, 3);
        if (owner != IntPtr.Zero && owner != window && ForegroundApplications.FromWindow(owner)?.ProcessId == Environment.ProcessId)
        {
            _lastAllowedWindow = window;
            _blockedWindow?.Hide();
            return;
        }
        var app = ForegroundApplications.FromWindow(window);
        if (app is null) return;
        if (ApplicationAccess.IsAllowed(app.ProcessId, Environment.ProcessId, app.ProcessName, app.ExecutablePath, _viewModel.SessionApplications, _viewModel.SessionApplicationMode))
        {
            _lastAllowedWindow = window;
            _blockedWindow?.Hide();
            return;
        }
        var target = GetAncestor(window, 3);
        _lastAllowedWindow = IntPtr.Zero;
        if (target == IntPtr.Zero) target = window;
        _blockedWindow ??= CreateBlockedWindow();
        _blockedWindow.SetApplication(app.Name);
        var screen = System.Windows.Forms.Screen.FromHandle(target);
        new WindowInteropHelper(_blockedWindow).EnsureHandle();
        var dpi = VisualTreeHelper.GetDpi(_blockedWindow);
        _blockedWindow.Left = screen.Bounds.Left / dpi.DpiScaleX;
        _blockedWindow.Top = screen.Bounds.Top / dpi.DpiScaleY;
        _blockedWindow.Width = screen.Bounds.Width / dpi.DpiScaleX;
        _blockedWindow.Height = screen.Bounds.Height / dpi.DpiScaleY;
        _blockedWindow.Show();
        _blockedWindow.Activate();
        ActivateForegroundWindow(new WindowInteropHelper(_blockedWindow).Handle);
    }

    public static bool ActivateForegroundWindow(IntPtr window)
    {
        if (GetForegroundWindow() == window) return true;
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var currentThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread
            && AttachThreadInput(currentThread, foregroundThread, true);
        try { return SetForegroundWindow(window); }
        finally { if (attached) AttachThreadInput(currentThread, foregroundThread, false); }
    }

    private ApplicationBlockedWindow CreateBlockedWindow()
    {
        var window = new ApplicationBlockedWindow { DataContext = _viewModel };
        window.ReturnToFocusRequested += (_, _) => ReturnToFocus();
        window.Closing += OnBlockedClosing;
        return window;
    }

    private void OnBlockedClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ReturnToFocus();
    }

    private void ReturnToFocus()
    {
        _returnToFocus();
        _blockedWindow?.Hide();
    }

    private void OnRestrictionsChanged(object? sender, EventArgs e)
    {
        _lastAllowedWindow = IntPtr.Zero;
        if (_viewModel.RestrictionsActive) CheckForeground();
        else _blockedWindow?.Hide();
    }

    public void Dispose()
    {
        _viewModel.RestrictionsChanged -= OnRestrictionsChanged;
        UnhookWinEvent(_hook);
        if (_blockedWindow is not null)
        {
            _blockedWindow.Closing -= OnBlockedClosing;
            _blockedWindow.Close();
        }
    }

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window, int objectId, int childId, uint threadId, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint minimum, uint maximum, IntPtr module, WinEventCallback callback, uint processId, uint threadId, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint otherThread, bool attach);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
