using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class NavigationOption : ObservableObject
{
    private bool _visible = true, _active;
    private readonly Action _changed;
    public string Key { get; }
    public string Name { get; }
    public string Icon { get; }
    public bool CanHide => Key != "settings";
    public bool Visible { get => _visible; set { value = CanHide ? value : true; if (_visible == value) return; _visible = value; Notify(); _changed(); } }
    public bool Active { get => _active; set { if (_active == value) return; _active = value; Notify(); } }
    public RelayCommand OpenCommand { get; }
    public NavigationOption(string key, string name, string icon, Action changed, Action<string> open)
    { Key = key; Name = name; Icon = icon; _changed = changed; OpenCommand = new(() => open(key)); }
}

public sealed class NavigationViewModel : ObservableObject
{
    private sealed record SavedOption(string Key, bool Visible);
    private readonly IFocusStore _store;
    private bool _loading;
    public ObservableCollection<NavigationOption> Items { get; } = new();
    public IReadOnlyList<NavigationOption> VisibleItems => Items.Where(item => item.Visible).ToArray();
    public event Action<string>? NavigateRequested;
    public NavigationViewModel(IFocusStore store)
    {
        _store = store; _loading = true;
        var defaults = new[] { ("focus", "专注时刻", "\uE916"), ("projects", "项目库", "\uE8B7"), ("todos", "待办事项", "\uE73A"), ("calendar", "日历与课表", "\uE787"), ("dates", "日期事项", "\uE916"), ("records", "专注记录", "\uE9D9"), ("goals", "目标与习惯", "\uE734"), ("settings", "偏好设置", "\uE713") }
            .Select(item => new NavigationOption(item.Item1, item.Item2, item.Item3, Save, key => NavigateRequested?.Invoke(key))).ToArray();
        var saved = store.GetSetting("ui.navigation.items") is { } json ? JsonSerializer.Deserialize<SavedOption[]>(json) ?? Array.Empty<SavedOption>() : Array.Empty<SavedOption>();
        foreach (var option in saved) { var item = defaults.FirstOrDefault(item => item.Key == option.Key); if (item is null || Items.Contains(item)) continue; item.Visible = option.Visible; Items.Add(item); }
        foreach (var item in defaults.Where(item => !Items.Contains(item)))
        {
            var settingsIndex = Items.ToList().FindIndex(existing => existing.Key == "settings");
            if (item.Key != "settings" && settingsIndex >= 0) Items.Insert(settingsIndex, item);
            else Items.Add(item);
        }
        _loading = false; SetActive("focus");
    }
    public void SetActive(string? key) { foreach (var item in Items) item.Active = item.Key == key; }
    public void Move(NavigationOption item, OrderMove direction, NavigationOption? target = null)
    {
        if (CollectionOrder.Move(Items, item, direction, targetItem: target)) Save();
    }
    private void Save()
    {
        if (_loading) return;
        _store.SaveSetting("ui.navigation.items", JsonSerializer.Serialize(Items.Select(item => new SavedOption(item.Key, item.Visible))));
        Notify(nameof(VisibleItems));
    }
}
