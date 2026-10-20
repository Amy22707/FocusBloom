using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class DateEventRow(DateEvent item, DateOnly today) : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private ImageSource? _thumbnail;
    private Task? _loading;
    public DateEvent Item { get; } = item;
    public string Title => Item.Title;
    public DateOnly? Next => CalendarRules.NextDate(Item, today);
    public int Days => Item.Mode == DateCountMode.CountUp ? today.DayNumber - Item.Date.DayNumber : (Next ?? Item.Date).DayNumber - today.DayNumber;
    public string Number => Item.Mode == DateCountMode.Countdown && Item.Repeat != DateRepeat.None && Next is null ? "—" : Math.Abs(Days).ToString();
    public string Label => Item.Mode == DateCountMode.Countdown && Item.Repeat != DateRepeat.None && Next is null ? "循环已结束" : Item.Mode == DateCountMode.CountUp ? Days >= 0 ? "已经" : "距离开始" : Days >= 0 ? "还有" : "已过去";
    public string Detail => $"{Next ?? Item.Date:yyyy-MM-dd}" + (Item.Time is { } time ? $" {time:HH:mm}" : "") + (Item.Repeat != DateRepeat.None ? " · " + (Item.Repeat switch { DateRepeat.Daily => "每日", DateRepeat.Monthly => "每月", _ => "每年" }) : "") + (Item.Pinned ? " · 置顶" : "");
    public string Notes => Item.Notes;
    public string? Cover => Item.CoverPath;
    public ImageSource? Thumbnail { get { _loading ??= LoadCover(); return _thumbnail; } }
    public bool HasCover => _thumbnail is not null;
    private async Task LoadCover()
    {
        if (string.IsNullOrWhiteSpace(Cover)) return;
        var image = await Task.Run(() => FocusMediaViewModel.LoadImage(Cover, 720)).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() => { _thumbnail = image; Notify(nameof(Thumbnail)); Notify(nameof(HasCover)); });
    }
}
public sealed record DateGroupChoice(string? Name)
{
    public string Label => Name ?? "全部分组";
}
public sealed record DateEventGroup(string Name, IReadOnlyList<DateEventRow> Items)
{
    public string CountText => $"{Items.Count} 项";
    public bool IsEmpty => Items.Count == 0;
    public bool Editable => Name != "未分组";
}
public sealed class DateGroupFilter(string name, bool selected, Action changed) : ObservableObject
{
    private bool _selected = selected;
    public string Name { get; } = name;
    public bool Selected { get => _selected; set { if (_selected == value) return; _selected = value; Notify(); changed(); } }
}
public sealed class DateEventsViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private DateGroupChoice? _selected;
    private List<string> _groupOrder;
    private HashSet<string>? _mixedGroups;
    private bool _mixedView;
    public ObservableCollection<DateEventRow> Items { get; } = [];
    public ObservableCollection<DateEventRow> VisibleItems { get; } = [];
    public ObservableCollection<string> GroupNames { get; } = [];
    public ObservableCollection<DateGroupChoice> GroupChoices { get; } = [];
    public ObservableCollection<DateEventGroup> Groups { get; } = [];
    public ObservableCollection<DateGroupFilter> GroupFilters { get; } = [];
    public event EventHandler? Changed;
    public DateGroupChoice? SelectedGroup
    {
        get => _selected;
        set { if (value is null || _selected == value) return; _selected = value; _store.SaveSetting("dates.selectedGroup", JsonSerializer.Serialize(value.Name)); Refresh(); Notify(); }
    }
    public bool ShowAllGroups => _selected?.Name is null;
    public bool ShowSingleGroup => !ShowAllGroups;
    public bool MixedView
    {
        get => _mixedView;
        set { if (_mixedView == value) return; _mixedView = value; _store.SaveSetting("dates.mixedView", value.ToString()); Notify(); Refresh(); }
    }
    public bool ShowMixedGroups => ShowAllGroups && MixedView;
    public string MixedGroupSummary => GroupFilters.All(item => item.Selected) ? "全部分组" : $"已选 {GroupFilters.Count(item => item.Selected)} 组";
    public string EmptyMessage => ShowMixedGroups && GroupFilters.All(item => !item.Selected) ? "请选择要查看的分组。" : "还没有日期事项，点击“新建日期事项”添加。";
    public bool ShowBoard => ShowAllGroups && !MixedView && GroupNames.Count > 1;
    public bool ShowCards => !ShowBoard;
    public string Heading => _selected?.Name ?? "日期事项";
    public string Summary => $"{VisibleItems.Count} 个事项" + (ShowAllGroups ? $" · {(ShowMixedGroups ? GroupFilters.Count(item => item.Selected) : GroupNames.Count)} 个分组" : "");
    public DateEventsViewModel(IFocusStore store)
    {
        _store = store;
        _groupOrder = store.GetSetting("dates.groupOrder") is { } order ? JsonSerializer.Deserialize<List<string>>(order)! : [];
        _mixedGroups = store.GetSetting("dates.mixedGroups") is { } groups ? JsonSerializer.Deserialize<HashSet<string>>(groups) : null;
        _mixedView = store.GetSetting("dates.mixedView") == "True";
        var saved = store.GetSetting("dates.selectedGroup"); _selected = new(saved is null ? null : JsonSerializer.Deserialize<string?>(saved)); Reload();
    }
    public void Reload()
    {
        Items.Clear(); var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var item in _store.GetDateEvents().OrderByDescending(item => item.Pinned).ThenBy(item => CalendarRules.NextDate(item, today))) Items.Add(new(item, today));
        var selected = _selected?.Name; GroupNames.Clear(); GroupChoices.Clear(); GroupChoices.Add(new(null));
        foreach (var name in _store.GetDateGroups()) GroupNames.Add(name);
        CollectionOrder.Apply(GroupNames, _groupOrder, name => name);
        _groupOrder = GroupNames.ToList();
        GroupFilters.Clear();
        foreach (var name in GroupNames) { GroupChoices.Add(new(name)); GroupFilters.Add(new(name, _mixedGroups?.Contains(name) ?? true, FilterChanged)); }
        _selected = GroupChoices.FirstOrDefault(choice => choice.Name == selected) ?? GroupChoices[0]; Notify(nameof(SelectedGroup)); Refresh();
    }
    private void Refresh()
    {
        VisibleItems.Clear(); foreach (var row in Items.Where(row => ShowAllGroups ? !ShowMixedGroups || _mixedGroups is null || _mixedGroups.Contains(row.Item.Group) : row.Item.Group == _selected?.Name)) VisibleItems.Add(row);
        Groups.Clear(); foreach (var name in GroupNames) Groups.Add(new(name, Items.Where(row => row.Item.Group == name).ToArray()));
        foreach (var name in new[] { nameof(ShowAllGroups), nameof(ShowSingleGroup), nameof(ShowBoard), nameof(ShowCards), nameof(ShowMixedGroups), nameof(MixedGroupSummary), nameof(EmptyMessage), nameof(Heading), nameof(Summary), nameof(IsEmpty) }) Notify(name);
    }
    private void FilterChanged()
    {
        _mixedGroups = GroupFilters.All(item => item.Selected) ? null : GroupFilters.Where(item => item.Selected).Select(item => item.Name).ToHashSet();
        _store.SaveSetting("dates.mixedGroups", JsonSerializer.Serialize(_mixedGroups)); Refresh();
    }
    public void MoveGroup(DateEventGroup group, OrderMove direction, DateEventGroup? target = null)
    {
        if (!CollectionOrder.Move(Groups, group, direction, targetItem: target)) return;
        _groupOrder = Groups.Select(item => item.Name).ToList(); SaveGroupOrder();
        CollectionOrder.Apply(GroupNames, _groupOrder, name => name);
        CollectionOrder.Apply(GroupFilters, _groupOrder, item => item.Name);
        CollectionOrder.Apply(GroupChoices, new string?[] { null }.Concat(_groupOrder).ToArray(), item => item.Name);
    }
    private void SaveGroupOrder() => _store.SaveSetting("dates.groupOrder", JsonSerializer.Serialize(_groupOrder));
    public string? SaveGroup(string? previous, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == "全部分组" || GroupNames.Any(existing => existing != previous && existing.Equals(name, StringComparison.OrdinalIgnoreCase))) return "请填写不重复的分组名称。";
        if (previous == "未分组") return "默认分组保留，事项可以移入其他分组。";
        if (previous == name) return null;
        try
        {
            var selected = _selected?.Name;
            if (previous is null) _store.SaveDateGroup(name); else _store.RenameDateGroup(previous, name);
            if (previous is not null)
            {
                _groupOrder = _groupOrder.Select(item => item == previous ? name : item).ToList(); SaveGroupOrder();
                if (_mixedGroups?.Remove(previous) == true) { _mixedGroups.Add(name); _store.SaveSetting("dates.mixedGroups", JsonSerializer.Serialize(_mixedGroups)); }
            }
            if (previous is not null && selected == previous) _selected = new(name);
            Reload(); _store.SaveSetting("dates.selectedGroup", JsonSerializer.Serialize(_selected?.Name)); Changed?.Invoke(this, EventArgs.Empty); return null;
        }
        catch (Exception error) { return "分组保存失败：" + error.Message; }
    }
    public string? DeleteGroup(string name)
    {
        if (name == "未分组") return "默认分组保留，事项可以移入其他分组。";
        try { _store.DeleteDateGroup(name); _groupOrder.Remove(name); SaveGroupOrder(); if (_mixedGroups?.Remove(name) == true) _store.SaveSetting("dates.mixedGroups", JsonSerializer.Serialize(_mixedGroups)); if (_selected?.Name == name) _selected = new("未分组"); Reload(); _store.SaveSetting("dates.selectedGroup", JsonSerializer.Serialize(_selected?.Name)); Changed?.Invoke(this, EventArgs.Empty); return null; }
        catch (Exception error) { return "分组删除失败：" + error.Message; }
    }
    public string? MoveToGroup(DateEventRow row, string group)
    {
        if (row.Item.Group == group) return null;
        if (!GroupNames.Contains(group)) return "目标分组不存在。";
        try { _store.SaveDateEvent(row.Item with { Group = group }); Reload(); Changed?.Invoke(this, EventArgs.Empty); return null; }
        catch (Exception error) { return "日期事项移动失败：" + error.Message; }
    }
    public bool IsEmpty => VisibleItems.Count == 0;
}
