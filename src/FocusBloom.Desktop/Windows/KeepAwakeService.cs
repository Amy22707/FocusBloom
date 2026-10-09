using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class KeepAwakeService : IDisposable
{
    private bool _active;
    private readonly MainViewModel? _viewModel;
    public KeepAwakeService(MainViewModel? viewModel = null)
    {
        _viewModel = viewModel;
        if (viewModel is not null) { viewModel.PropertyChanged += OnPreferenceChanged; Update(); }
    }
    public bool Active => _active;
    public void SetActive(bool active)
    {
        if (_active == active) return;
        var flags = 0x80000000u | (active ? 0x00000001u | 0x00000002u : 0);
        if (SetThreadExecutionState(flags) == 0) throw new Win32Exception("无法更新常亮请求。");
        _active = active;
    }

    private void OnPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsFocusActive) or nameof(MainViewModel.KeepScreenOn)) Update();
    }

    private void Update()
    {
        try { SetActive(_viewModel!.IsFocusActive && _viewModel.KeepScreenOn); }
        catch (Exception ex) { _viewModel!.ReportStatus($"常亮请求失败：{ex.Message}"); }
    }

    public void Dispose()
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnPreferenceChanged;
        SetActive(false);
    }
    [DllImport("kernel32.dll")] private static extern uint SetThreadExecutionState(uint flags);
}
