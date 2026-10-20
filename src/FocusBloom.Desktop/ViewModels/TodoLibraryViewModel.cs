using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class TodoRow(TodoItem item, TimeSpan invested) : ObservableObject
{
    public TodoItem Item { get; } = item;
    public Guid Id => Item.Id;
    public string Title => Item.Title;
    public bool Completed => Item.Status == TodoStatus.Completed;
    public string StatusText => Item.Status switch { TodoStatus.InProgress => "进行中", TodoStatus.Completed => "已完成", _ => "待办" };
    public string PriorityText => Item.Priority switch { TodoPriority.High => "高优先级", TodoPriority.Low => "低优先级", _ => "普通优先级" };
    public string QuadrantText => (Item.Important ? "重要" : "不重要") + " · " + (Item.Urgent ? "紧急" : "不紧急");
    public string DueText => Item.DueDate is { } date ? $"截止 {date:yyyy-MM-dd}" + (Item.DueTime is { } time ? $" {time:HH:mm}" : "") : "无截止日期";
    public string DueDisplayText => Item.DueDate is { } date ? "截止 " + (date.Year == DateTime.Today.Year ? date.ToString("M月d日") : date.ToString("yyyy/M/d")) + (Item.DueTime is { } time ? $" {time:HH:mm}" : "") : "";
    public bool HasDueDate => Item.DueDate is not null;
    public string DueCompactText => Item.DueDate is { } date ? $"{date:MM-dd}" + (Item.DueTime is { } time ? $" {time:HH:mm}" : "") : "";
    public bool Overdue => !Completed && Item.DueDate is { } date &&
        (date < DateOnly.FromDateTime(DateTime.Now) || (date == DateOnly.FromDateTime(DateTime.Now) && Item.DueTime is { } time && time < TimeOnly.FromDateTime(DateTime.Now)));
    public string Notes => Item.Notes;
    public bool HasNotes => Item.Notes.Length > 0;
    public bool HasResumeNote => Item.ResumeNote.Length > 0;
    public string ResumeText => string.IsNullOrEmpty(Item.ResumeNote) ? "" : "下一步：" + Item.ResumeNote;
    public string InvestmentText => $"已投入 {(int)invested.TotalHours:00}:{invested.Minutes:00}:{invested.Seconds:00}" + (Item.EstimatedMinutes is { } estimate ? $" · 预计 {estimate} 分钟" : "");
    public void RefreshDeadline() { Notify(nameof(Overdue)); Notify(nameof(DueDisplayText)); }
}

public sealed class TodoQuadrant(bool important, bool urgent, string hint) : ObservableObject
{
    public bool Important { get; } = important;
    public bool Urgent { get; } = urgent;
    public int Row => Urgent ? 0 : 1;
    public int Column => Important ? 1 : 0;
    public string Title => (Important ? "重要" : "不重要") + "且" + (Urgent ? "紧急" : "不紧急");
    public string Hint { get; } = hint;
    public ObservableCollection<TodoRow> Items { get; } = [];
    public string CountText => $"{Items.Count} 项";
    public bool IsEmpty => Items.Count == 0;
    public void Refresh() { Notify(nameof(CountText)); Notify(nameof(IsEmpty)); }
}

public sealed class TodoFolderGroup : ObservableObject
{
    private readonly IFocusStore _store;
    private bool _completedExpanded;
    public TodoFolder Folder { get; }
    public string Name => Folder.Name;
    public IReadOnlyList<TodoRow> Pending { get; }
    public IReadOnlyList<TodoRow> Completed { get; }
    public string CountText => $"{Pending.Count + Completed.Count} 项";
    public string CompletedLabel => $"已完成 · {Completed.Count}";
    public bool HasCompleted => Completed.Count > 0;
    public bool IsEmpty => Pending.Count + Completed.Count == 0;
    public bool CompletedExpanded { get => _completedExpanded; set { _completedExpanded = value; _store.SaveSetting("ui.todos.completedExpanded." + Folder.Id, value.ToString()); Notify(); } }
    public TodoFolderGroup(TodoFolder folder, IEnumerable<TodoRow> items, IFocusStore store, bool completedOnly)
    {
        _store = store; Folder = folder; var rows = items.ToArray(); Pending = rows.Where(item => !item.Completed).ToArray(); Completed = rows.Where(item => item.Completed).ToArray();
        _completedExpanded = completedOnly || store.GetSetting("ui.todos.completedExpanded." + folder.Id) == "True";
    }
}

public sealed class TodoLibraryViewModel : ObservableObject
{
    private sealed record TodoOrder(List<string> Categories, List<Guid> Folders, List<Guid> Items);
    private readonly IFocusStore _store;
    private readonly Action<string> _report;
    private readonly Func<Guid, bool> _canDelete;
    private TodoOrder _order;
    private string? _category;
    private TodoFolder? _folder;
    private string _search = "";
    private string _filter = "全部";
    private long _deadlineMinute = -1;
    private string _viewMode;
    private string _quadrantScope;
    private string _listScope;
    public event EventHandler? Changed;
    public ObservableCollection<string> Categories { get; } = [];
    public ObservableCollection<TodoFolder> Folders { get; } = [];
    public ObservableCollection<TodoRow> Items { get; } = [];
    public ObservableCollection<TodoRow> VisibleItems { get; } = [];
    public ObservableCollection<TodoFolderGroup> FolderGroups { get; } = [];
    public IReadOnlyList<TodoQuadrant> Quadrants { get; } = new[]
    {
        new TodoQuadrant(true, true, "优先处理"), new TodoQuadrant(true, false, "安排时间"),
        new TodoQuadrant(false, true, "合理协调"), new TodoQuadrant(false, false, "适当取舍")
    };
    public IReadOnlyList<string> ViewModes { get; } = new[] { "列表", "四象限" };
    public string ViewMode
    {
        get => _viewMode;
        set
        {
            _viewMode = value; RefreshItems(); Notify(); Notify(nameof(ShowList)); Notify(nameof(ShowQuadrants)); Notify(nameof(Heading)); Notify(nameof(Scope));
            try { _store.SaveSetting("todos.view", value); } catch (Exception ex) { _report($"视图偏好保存失败：{ex.Message}"); }
        }
    }
    public bool ShowList => ViewMode == "列表";
    public bool ShowQuadrants => ViewMode == "四象限";
    public IReadOnlyList<string> QuadrantScopes { get; } = new[] { "全部文件夹", "当前文件夹" };
    public string QuadrantScope
    {
        get => _quadrantScope;
        set
        {
            _quadrantScope = value; RefreshItems(); Notify(); Notify(nameof(Heading)); Notify(nameof(Scope));
            try { _store.SaveSetting("todos.quadrantScope", value); } catch (Exception ex) { _report($"象限范围保存失败：{ex.Message}"); }
        }
    }
    public string ListScope
    {
        get => _listScope;
        set { _listScope = value; _store.SaveSetting("todos.listScope", value); RefreshItems(); Notify(); Notify(nameof(Scope)); Notify(nameof(Heading)); }
    }
    public string Scope { get => ShowQuadrants ? QuadrantScope : ListScope; set { if (ShowQuadrants) QuadrantScope = value; else ListScope = value; } }
    public bool ShowAllFolders => Scope == "全部文件夹";
    public bool ShowGroupedList => ShowList && ShowAllFolders;
    public bool ShowFlatList => ShowList && !ShowAllFolders;
    public IReadOnlyList<string> Filters { get; } = new[] { "全部", "待办", "进行中", "已完成" };
    public string? SelectedCategory { get => _category; set { _category = value; Notify(); } }
    public TodoFolder? SelectedFolder { get => _folder; set { _folder = value; RefreshItems(); Notify(); Notify(nameof(Heading)); SaveSelection(); } }
    public string Search { get => _search; set { _search = value; RefreshItems(); Notify(); } }
    public string Filter { get => _filter; set { _filter = value; RefreshItems(); Notify(); } }
    public string Heading => ShowAllFolders ? "全部待办" : SelectedFolder?.Name ?? "待办事项";
    public string Summary => $"{VisibleItems.Count} 个事项 · {VisibleItems.Count(item => item.Completed)} 个已完成" + (ShowAllFolders ? " · 所有文件夹" : "");
    public bool IsEmpty => VisibleItems.Count == 0;

    public TodoLibraryViewModel(IFocusStore store, Action<string> report, Func<Guid, bool> canDelete)
    {
        _store = store; _report = report; _canDelete = canDelete;
        _viewMode = store.GetSetting("todos.view") == "四象限" ? "四象限" : "列表";
        _quadrantScope = store.GetSetting("todos.quadrantScope") == "当前文件夹" ? "当前文件夹" : "全部文件夹";
        _listScope = store.GetSetting("todos.listScope") == "全部文件夹" ? "全部文件夹" : "当前文件夹";
        var json = store.GetSetting("todos.order");
        _order = json is null ? new([], [], []) : JsonSerializer.Deserialize<TodoOrder>(json)!;
        var savedFolder = store.GetSetting("todos.folder");
        Reload();
        if (Guid.TryParse(savedFolder, out var id) && Folders.FirstOrDefault(folder => folder.Id == id) is { } selected)
        { SelectedCategory = selected.Category; SelectedFolder = selected; }
    }
    public void Reload()
    {
        var category = _category; var folderId = _folder?.Id;
        Categories.Clear(); Folders.Clear(); Items.Clear();
        foreach (var name in _store.GetTodoCategories()) Categories.Add(name);
        foreach (var folder in _store.GetTodoFolders()) Folders.Add(folder);
        var records = _store.GetRecords();
        foreach (var item in _store.GetTodos()) Items.Add(new(item,
            TimeSpan.FromTicks(records.Where(record => record.TodoId == item.Id).Sum(record => record.FocusDuration.Ticks))));
        CollectionOrder.Apply(Categories, _order.Categories, name => name);
        CollectionOrder.Apply(Folders, _order.Folders, folder => folder.Id);
        CollectionOrder.Apply(Items, _order.Items, item => item.Id);
        _category = Categories.FirstOrDefault(name => name == category) ?? Categories.FirstOrDefault();
        SelectedFolder = Folders.FirstOrDefault(folder => folder.Id == folderId) ?? Folders.FirstOrDefault();
        Notify(nameof(SelectedCategory)); Changed?.Invoke(this, EventArgs.Empty);
    }
    private void RefreshItems()
    {
        VisibleItems.Clear();
        foreach (var item in Items.Where(item => (ShowAllFolders || item.Item.FolderId == _folder?.Id) && (_filter == "全部" || item.StatusText == _filter)
            && (string.IsNullOrWhiteSpace(_search) || item.Title.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase) || item.Notes.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase))).OrderBy(item => item.Completed)) VisibleItems.Add(item);
        FolderGroups.Clear();
        foreach (var folder in Folders)
        {
            var rows = VisibleItems.Where(item => item.Item.FolderId == folder.Id).ToArray();
            if (!string.IsNullOrWhiteSpace(_search) && rows.Length == 0) continue;
            FolderGroups.Add(new(folder, rows, _store, _filter == "已完成"));
        }
        Notify(nameof(Summary)); Notify(nameof(IsEmpty)); Notify(nameof(ShowAllFolders)); Notify(nameof(ShowGroupedList)); Notify(nameof(ShowFlatList));
        foreach (var quadrant in Quadrants)
        {
            quadrant.Items.Clear();
            foreach (var item in VisibleItems.Where(item => item.Item.Important == quadrant.Important && item.Item.Urgent == quadrant.Urgent)) quadrant.Items.Add(item);
            quadrant.Refresh();
        }
    }
    private void SaveSelection()
    {
        if (_folder is null) return;
        try { _store.SaveSetting("todos.folder", _folder.Id.ToString()); }
        catch (Exception ex) { _report($"待办选择保存失败：{ex.Message}"); }
    }
    public void RefreshDeadlines()
    {
        var minute = DateTime.Now.Ticks / TimeSpan.TicksPerMinute;
        if (_deadlineMinute == minute) return;
        _deadlineMinute = minute; foreach (var item in Items) item.RefreshDeadline();
    }
    public bool SaveCategory(string? previous, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || Categories.Any(item => item != previous && Same(item, name))) { _report("请填写不重复的待办分类名称。"); return false; }
        if (previous == name) return true;
        try
        {
            if (previous is null) _store.SaveTodoCategory(name);
            else { _store.RenameTodoCategory(previous, name); _order = _order with { Categories = Categories.Select(item => item == previous ? name : item).ToList() }; }
            Reload(); SelectedCategory = name; SaveOrder(); _report("待办分类已保存。"); return true;
        }
        catch (Exception ex) { _report($"分类保存失败：{ex.Message}"); return false; }
    }
    public TodoFolder? SaveFolder(TodoFolder? previous, string name, string category)
    {
        name = name.Trim();
        if (name.Length == 0 || Folders.Any(folder => folder.Id != previous?.Id && Same(folder.Name, name))) { _report("请填写不重复的文件夹名称。"); return null; }
        try
        {
            var folder = new TodoFolder(previous?.Id ?? Guid.NewGuid(), name, category);
            _store.SaveTodoFolder(folder); Reload(); SelectedCategory = category; SelectedFolder = Folders.Single(item => item.Id == folder.Id);
            SaveOrder(); _report("待办文件夹已保存。"); return folder;
        }
        catch (Exception ex) { _report($"文件夹保存失败：{ex.Message}"); return null; }
    }
    public bool SaveItem(TodoItem item)
    {
        item = item with { Title = item.Title.Trim() };
        if (item.Title.Length == 0 || Items.Any(row => row.Id != item.Id && row.Item.FolderId == item.FolderId && Same(row.Title, item.Title)))
        { _report("请填写该文件夹内不重复的事项名称。"); return false; }
        try
        {
            _store.SaveTodo(item); Reload();
            if (Folders.FirstOrDefault(folder => folder.Id == item.FolderId) is { } folder) { SelectedCategory = folder.Category; SelectedFolder = folder; }
            SaveOrder(); _report("待办事项已保存。"); return true;
        }
        catch (Exception ex) { _report($"待办保存失败：{ex.Message}"); return false; }
    }
    public bool CanDeleteItem(Guid id) => _canDelete(id);
    public bool MoveToFolder(TodoRow row, TodoFolder folder) => SaveMovedItem(row.Item with { FolderId = folder.Id });
    public bool MoveToQuadrant(TodoRow row, TodoQuadrant quadrant) => SaveMovedItem(row.Item with { Important = quadrant.Important, Urgent = quadrant.Urgent });
    private bool SaveMovedItem(TodoItem item)
    {
        var folderId = SelectedFolder?.Id;
        if (!SaveItem(item)) return false;
        if (Folders.FirstOrDefault(folder => folder.Id == folderId) is { } previous) SelectedFolder = previous;
        return true;
    }
    public bool CanDeleteFolder(TodoFolder folder) => Items.Where(item => item.Item.FolderId == folder.Id).All(item => _canDelete(item.Id));
    public bool CanDeleteCategory(string category) => Folders.Where(folder => folder.Category == category).All(CanDeleteFolder);
    public bool DeleteItem(Guid id) => Delete(() => _store.DeleteTodo(id), _canDelete(id));
    public bool DeleteFolder(TodoFolder folder) => Delete(() => _store.DeleteTodoFolder(folder.Id), CanDeleteFolder(folder));
    public bool DeleteCategory(string category) => Delete(() => _store.DeleteTodoCategory(category), CanDeleteCategory(category));
    private bool Delete(Action action, bool allowed)
    {
        if (!allowed) { _report("请先结束相关待办的计时并保存记录，再删除。"); return false; }
        try { action(); Reload(); SaveOrder(); _report("已删除，历史专注记录保留。"); return true; }
        catch (Exception ex) { _report($"删除失败：{ex.Message}"); return false; }
    }
    public void ToggleCompleted(TodoRow row) => SaveItem(row.Item with { Status = row.Completed ? TodoStatus.Pending : TodoStatus.Completed });
    public void MoveCategory(string category, OrderMove direction, string? target = null)
    { if (CollectionOrder.Move(Categories, category, direction, targetItem: target)) SaveOrder(); }
    public void MoveFolder(TodoFolder folder, OrderMove direction, TodoFolder? target = null)
    {
        if (CollectionOrder.Move(Folders, folder, direction, targetItem: target)) { RefreshItems(); SaveOrder(); }
    }
    public void MoveItem(TodoRow item, OrderMove direction, TodoRow? target = null, TodoQuadrant? quadrant = null)
    {
        var shown = (quadrant?.Items ?? VisibleItems).Where(row => row.Completed == item.Completed).ToArray();
        if (CollectionOrder.Move(Items, item, direction, shown, target)) { RefreshItems(); SaveOrder(); Changed?.Invoke(this, EventArgs.Empty); }
    }
    private void SaveOrder()
    {
        _order = new(Categories.ToList(), Folders.Select(folder => folder.Id).ToList(), Items.Select(item => item.Id).ToList());
        try { _store.SaveSetting("todos.order", JsonSerializer.Serialize(_order)); }
        catch (Exception ex) { _report($"待办顺序保存失败：{ex.Message}"); }
    }
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
