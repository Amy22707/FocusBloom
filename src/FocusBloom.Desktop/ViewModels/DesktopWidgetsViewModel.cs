using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public enum DesktopWidgetKind { Focus, Todos, Dates, Habits, Mood, Calendar }
public sealed record DesktopWidgetPreferences(bool Enabled = false, string View = "", string Period = "日", bool Topmost = false, double Transparency = 0,
    double Width = 340, double Height = 360, double? Left = null, double? Top = null, string? Monitor = null, Guid? FolderId = null, Guid? ProjectId = null,
    bool IncludeCompleted = false, bool AllItems = true, Guid[]? ItemIds = null, bool ShowCovers = true,
    string BackgroundMode = "跟随主题", string BackgroundColor = "#EDE7F3", string? BackgroundImage = null, string[]? DateGroups = null);

public sealed class DesktopWidgetOption : ObservableObject
{
    private readonly IFocusStore _store;
    private DesktopWidgetPreferences _value;
    public DesktopWidgetKind Kind { get; }
    public string Name { get; }
    public IReadOnlyList<string> Views { get; }
    public double MinimumWidth => Kind == DesktopWidgetKind.Focus ? 240 : Kind == DesktopWidgetKind.Todos && View == "四象限" ? 360 : Kind == DesktopWidgetKind.Mood ? 300 : 280;
    public double MinimumHeight => Kind == DesktopWidgetKind.Focus ? View == "数字" ? 190 : View == "热力图" && Period == "月" ? 340 : 300 : View == "四象限" ? 380 : Kind == DesktopWidgetKind.Mood || View == "月历" ? 320 : 240;
    public DesktopWidgetPreferences Preferences => _value;
    public bool Enabled { get => _value.Enabled; set => Save(_value with { Enabled = value }, nameof(Enabled)); }
    public string View { get => _value.View; set { if (Views.Contains(value)) Save(_value with { View = value }, nameof(View)); } }
    public string Period { get => _value.Period; set { if (new[] { "日", "周", "月", "年" }.Contains(value)) Save(_value with { Period = value }, nameof(Period)); } }
    public bool Topmost { get => _value.Topmost; set => Save(_value with { Topmost = value }, nameof(Topmost)); }
    public double Transparency { get => _value.Transparency; set => Save(_value with { Transparency = Math.Clamp(Math.Round(value), 0, 70) }, nameof(Transparency)); }
    public double Width { get => _value.Width; set => Save(_value with { Width = Math.Max(MinimumWidth, Math.Round(value)) }, nameof(Width)); }
    public double Height { get => _value.Height; set => Save(_value with { Height = Math.Max(MinimumHeight, Math.Round(value)) }, nameof(Height)); }
    public Guid? FolderId { get => _value.FolderId; set => Save(_value with { FolderId = value }, nameof(FolderId)); }
    public Guid? ProjectId { get => _value.ProjectId; set => Save(_value with { ProjectId = value }, nameof(ProjectId)); }
    public bool IncludeCompleted { get => _value.IncludeCompleted; set => Save(_value with { IncludeCompleted = value }, nameof(IncludeCompleted)); }
    public bool AllItems { get => _value.AllItems; set => Save(_value with { AllItems = value }, nameof(AllItems)); }
    public bool ShowCovers { get => _value.ShowCovers; set => Save(_value with { ShowCovers = value }, nameof(ShowCovers)); }
    public IReadOnlyList<string> BackgroundModes { get; } = new[] { "跟随主题", "纯色", "图片" };
    public string BackgroundMode { get => _value.BackgroundMode; set { if (BackgroundModes.Contains(value)) Save(_value with { BackgroundMode = value }, nameof(BackgroundMode)); } }
    public string BackgroundColor { get => _value.BackgroundColor; set => Save(_value with { BackgroundColor = value }, nameof(BackgroundColor)); }
    public string? BackgroundImage { get => _value.BackgroundImage; set => Save(_value with { BackgroundImage = value }, nameof(BackgroundImage)); }
    public bool Includes(Guid id) => AllItems || (_value.ItemIds ?? Array.Empty<Guid>()).Contains(id);
    public bool IncludesGroup(string name) => _value.DateGroups is null || _value.DateGroups.Contains(name);
    public bool AllDateGroups { get => _value.DateGroups is null; set { if (value != AllDateGroups) Save(_value with { DateGroups = value ? null : Array.Empty<string>() }, nameof(AllDateGroups)); } }
    public void SelectDateGroup(string name, bool selected)
    { var groups = (_value.DateGroups ?? Array.Empty<string>()).ToHashSet(); if (selected) groups.Add(name); else groups.Remove(name); Save(_value with { DateGroups = groups.ToArray() }, nameof(Preferences)); }
    public void RemapDateGroup(string previous, string name)
    { if (_value.DateGroups?.Contains(previous) == true) Save(_value with { DateGroups = _value.DateGroups.Select(group => group == previous ? name : group).Distinct().ToArray() }, nameof(Preferences)); }
    public void SelectItem(Guid id, bool selected) { var ids = (_value.ItemIds ?? Array.Empty<Guid>()).ToHashSet(); if (selected) ids.Add(id); else ids.Remove(id); Save(_value with { ItemIds = ids.ToArray() }, nameof(Preferences)); }
    public void SaveBounds(double left, double top, double width, double height, string monitor) => Save(_value with { Left = left, Top = top, Width = width, Height = height, Monitor = monitor }, nameof(Preferences));
    public event EventHandler? Changed;
    public DesktopWidgetOption(IFocusStore store, DesktopWidgetKind kind, string name, string[] views, double height)
    {
        _store = store; Kind = kind; Name = name; Views = views;
        _value = store.GetSetting("widgets." + kind) is { } json ? JsonSerializer.Deserialize<DesktopWidgetPreferences>(json)! : new(Height: height, Width: kind == DesktopWidgetKind.Calendar ? 460 : kind == DesktopWidgetKind.Todos ? 420 : 340);
        if (!views.Contains(_value.View)) _value = _value with { View = views[0] };
    }
    private void Save(DesktopWidgetPreferences value, string property)
    {
        if (_value == value) return; _value = value; _store.SaveSetting("widgets." + Kind, JsonSerializer.Serialize(value)); Notify(property); Changed?.Invoke(this, EventArgs.Empty);
    }
}

public sealed class DesktopWidgetsViewModel
{
    public ObservableCollection<DesktopWidgetOption> Items { get; }
    public event EventHandler? Changed;
    public DesktopWidgetsViewModel(IFocusStore store)
    {
        Items = new() {
            new(store, DesktopWidgetKind.Focus, "专注时长", new[] { "数字", "趋势", "热力图" }, 240),
            new(store, DesktopWidgetKind.Todos, "待办列表", new[] { "平铺", "紧凑列表", "按截止日期", "四象限" }, 420),
            new(store, DesktopWidgetKind.Dates, "日期事项", new[] { "卡片", "列表", "分组", "大字", "月历" }, 370),
            new(store, DesktopWidgetKind.Habits, "习惯打卡", new[] { "日", "周", "月", "年", "列表", "平铺" }, 340),
            new(store, DesktopWidgetKind.Mood, "心情月历", new[] { "月历", "趋势" }, 400),
            new(store, DesktopWidgetKind.Calendar, "日历与课表", new[] { "日", "周", "月", "年", "课程表" }, 460)
        };
        foreach (var item in Items) item.Changed += (sender, _) => Changed?.Invoke(sender, EventArgs.Empty);
    }
    public DesktopWidgetOption this[DesktopWidgetKind kind] => Items.Single(item => item.Kind == kind);
}
