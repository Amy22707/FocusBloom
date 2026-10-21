using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed record DesktopWidgetData(int Version, IReadOnlyList<FocusRecord> Records, IReadOnlyList<TodoRow> Todos,
    IReadOnlyList<DateEventRow> Dates, IReadOnlyList<HabitRow> Habits, IReadOnlyList<DailyReflection> Moods, IReadOnlyList<HabitCheckIn> CheckIns);

public sealed class DesktopWidgetManager : IDisposable
{
    private readonly MainViewModel _main;
    private readonly IFocusStore _store;
    private readonly Action<DesktopWidgetKind, Guid?, DateOnly?> _open;
    private readonly Action<CalendarEntry> _openCalendar;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<DesktopWidgetKind, DesktopWidgetWindow> _windows = new();
    private bool _started, _disposed, _dirty = true, _queued;
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    private FocusState _state;
    public IReadOnlyDictionary<DesktopWidgetKind, DesktopWidgetWindow> Windows => _windows;
    public DesktopWidgetData Data { get; private set; } = new(0, Array.Empty<FocusRecord>(), Array.Empty<TodoRow>(), Array.Empty<DateEventRow>(), Array.Empty<HabitRow>(), Array.Empty<DailyReflection>(), Array.Empty<HabitCheckIn>());
    public DesktopWidgetManager(MainViewModel main, IFocusStore store, Action<DesktopWidgetKind, Guid?, DateOnly?> open, Action<CalendarEntry> openCalendar)
    {
        _main = main; _store = store; _open = open; _openCalendar = openCalendar; _state = main.State;
        main.Widgets.Changed += PreferenceChanged; main.CalendarDataChanged += DataChanged; main.PropertyChanged += MainChanged; Appearance.Changed += AppearanceChanged;
        _tick.Tick += (_, _) => Tick();
    }
    public void Start() { if (_started || _disposed) return; _started = true; Reconcile(null); }
    private void PreferenceChanged(object? sender, EventArgs args) { if (_started && !_disposed) Reconcile(sender as DesktopWidgetOption); }
    private void Reconcile(DesktopWidgetOption? changed)
    {
        if (_dirty && _main.Widgets.Items.Any(item => item.Enabled)) LoadData();
        foreach (var option in _main.Widgets.Items)
        {
            if (!option.Enabled)
            { if (_windows.Remove(option.Kind, out var hidden)) hidden.Close(); continue; }
            if (!_windows.TryGetValue(option.Kind, out var window))
            {
                window = new(_main, _store, option, this, _open); _windows.Add(option.Kind, window);
                var opened = window; window.Closed += (_, _) => { if (_windows.TryGetValue(option.Kind, out var current) && current == opened) _windows.Remove(option.Kind); if (!_disposed && option.Enabled) option.Enabled = false; };
                window.Show(); window.ApplyWindowLevel();
            }
            else if (changed is null || changed == option) { window.ApplyPreferences(); window.Render(); }
        }
        if (_windows.Count == 0) _tick.Stop(); else _tick.Start();
    }
    private void MainChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.RecordSummary)) QueueRefresh();
        if (args.PropertyName == nameof(MainViewModel.State) && _state != _main.State) { _state = _main.State; QueueRefresh(); }
    }
    private void DataChanged(object? sender, EventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        _dirty = true; if (!_started || _disposed || _queued || _windows.Count == 0) return; _queued = true;
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { _queued = false; if (!_disposed) RefreshNow(); }));
    }
    private void AppearanceChanged(object? sender, EventArgs args) { foreach (var window in _windows.Values.ToArray()) { window.ApplyBackground(); window.Render(); } }
    public void OpenCalendarEntry(CalendarEntry entry) => _openCalendar(entry);
    private void LoadData()
    {
        Data = new(Data.Version + 1, _store.GetRecords(), _main.Todos.Items.ToArray(), _main.DateEvents.Items.ToArray(), _main.Goals.Habits.ToArray(), _main.Reflection.Entries.ToArray(), _store.GetHabitCheckIns()); _dirty = false;
    }
    public void RefreshNow() { if (_disposed) return; LoadData(); foreach (var window in _windows.Values.ToArray()) window.Render(); }
    public void Tick()
    {
        if (_disposed) return;
        var day = DateOnly.FromDateTime(DateTime.Today); if (day != _day) { _day = day; _dirty = true; }
        if (_dirty) RefreshNow(); foreach (var window in _windows.Values.ToArray()) window.Tick();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _tick.Stop();
        _main.Widgets.Changed -= PreferenceChanged; _main.CalendarDataChanged -= DataChanged; _main.PropertyChanged -= MainChanged; Appearance.Changed -= AppearanceChanged;
        foreach (var window in _windows.Values.ToArray()) window.Close(); _windows.Clear();
    }
}
