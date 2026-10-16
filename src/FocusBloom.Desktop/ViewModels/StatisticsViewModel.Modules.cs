using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class StatisticsModuleOption(string key, string name, Action changed) : ObservableObject
{
    private bool _visible = true;
    public string Key { get; } = key;
    public string Name { get; } = name;
    public bool Visible { get => _visible; set { if (_visible == value) return; _visible = value; Notify(); changed(); } }
}

public sealed partial class StatisticsViewModel
{
    private sealed record SavedModule(string Key, bool Visible);
    private bool _loadingModules;
    public ObservableCollection<StatisticsModuleOption> Modules { get; } = new();
    private void LoadModules()
    {
        _loadingModules = true;
        var defaults = new[] { ("overview", "投入概览"), ("trend", "投入趋势"), ("shares", "投入占比"), ("activity", "活跃时间段"), ("annual", "年度投入"), ("goals", "目标进度"), ("concentration", "专注程度") }
            .Select(item => new StatisticsModuleOption(item.Item1, item.Item2, SaveModules)).ToArray();
        var saved = _store.GetSetting("statistics.modules") is { } json ? JsonSerializer.Deserialize<SavedModule[]>(json) ?? Array.Empty<SavedModule>() : Array.Empty<SavedModule>();
        foreach (var option in saved) { var item = defaults.FirstOrDefault(item => item.Key == option.Key); if (item is null || Modules.Contains(item)) continue; item.Visible = option.Visible; Modules.Add(item); }
        foreach (var item in defaults.Where(item => !Modules.Contains(item))) Modules.Add(item);
        _loadingModules = false;
    }
    public void MoveModule(StatisticsModuleOption item, OrderMove direction, StatisticsModuleOption? target = null)
    { if (CollectionOrder.Move(Modules, item, direction, targetItem: target)) SaveModules(); }
    private void SaveModules()
    {
        if (_loadingModules) return;
        _store.SaveSetting("statistics.modules", JsonSerializer.Serialize(Modules.Select(item => new SavedModule(item.Key, item.Visible)))); RequestRedraw();
    }
}
