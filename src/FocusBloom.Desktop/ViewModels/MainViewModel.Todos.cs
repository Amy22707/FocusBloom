using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;

namespace FocusBloom.Desktop.ViewModels;

public sealed record TodoChoice(Guid? Id, string Title);
public sealed record TodoFolderChoice(Guid? Id, string Name);

public sealed partial class MainViewModel
{
    private TodoChoice _selectedFocusTodo = new(null, "不关联待办");
    private TodoFolderChoice _selectedFocusTodoFolder = new(null, "不关联待办");
    private Guid? _activeTodoId;
    private string _activeTodoTitle = "";
    private string _focusResumeNote = "";
    public TodoLibraryViewModel Todos { get; }
    public ObservableCollection<TodoChoice> FocusTodoChoices { get; } = [];
    public ObservableCollection<TodoFolderChoice> FocusTodoFolderChoices { get; } = [];
    public ObservableCollection<TodoChoice> FolderFocusTodoChoices { get; } = [];
    public bool HasFocusTodoFolder => SelectedFocusTodoFolder.Id is not null;
    public TodoFolderChoice SelectedFocusTodoFolder
    {
        get => _selectedFocusTodoFolder;
        set
        {
            if (value is null) return;
            if (!CanConfigure && value.Id != _selectedFocusTodoFolder.Id)
            { StatusMessage = "请先结束当前计时，再更换关联待办的文件夹。"; Notify(); return; }
            _selectedFocusTodoFolder = value; Notify(); Notify(nameof(HasFocusTodoFolder));
            if (Todos.Items.FirstOrDefault(item => item.Id == _selectedFocusTodo.Id)?.Item.FolderId != value.Id)
                SelectedFocusTodo = FocusTodoChoices[0];
            else { RefreshFolderTodoChoices(); Notify(nameof(SelectedFocusTodo)); }
            SaveFocusTodoFolder();
        }
    }
    public Guid? LastSavedRecordId { get; private set; }
    public bool ShowOutcomeEntry => LastSavedRecordId is not null && LastSavedRecordId == _timer.LastRecord?.Id && (CanConfigure || State == FocusState.Resting);
    public TodoChoice SelectedFocusTodo
    {
        get => _selectedFocusTodo;
        set
        {
            if (value is null) return;
            if (!CanConfigure && value.Id != _selectedFocusTodo.Id) { StatusMessage = "请先结束当前计时，再更换关联待办。"; Notify(); return; }
            _selectedFocusTodo = value;
            if (Todos.Items.FirstOrDefault(item => item.Id == value.Id)?.Item.FolderId is { } folderId)
            {
                _selectedFocusTodoFolder = FocusTodoFolderChoices.Single(folder => folder.Id == folderId);
                Notify(nameof(SelectedFocusTodoFolder)); Notify(nameof(HasFocusTodoFolder));
            }
            RefreshFolderTodoChoices(); Notify(); Notify(nameof(FocusTodoText)); Notify(nameof(HasFocusTodo)); UpdateResumeNote();
            SaveFocusTodoFolder();
            try { _store.SaveSetting("focus.todo", value.Id?.ToString() ?? ""); }
            catch (Exception ex) { StatusMessage = $"待办选择保存失败：{ex.Message}"; }
        }
    }
    public string FocusTodoText => CanConfigure ? Todos?.Items.FirstOrDefault(item => item.Id == SelectedFocusTodo.Id)?.Title ?? "" : _activeTodoTitle;
    public bool HasFocusTodo => FocusTodoText.Length > 0;
    public string FocusResumeNote => _focusResumeNote;
    private void UpdateResumeNote()
    {
        if (SelectedFocusTodo.Id is { } id) _focusResumeNote = Todos?.Items.FirstOrDefault(item => item.Id == id)?.Item.ResumeNote ?? "";
        else if (SelectedProject is not null)
        {
            var record = _store.GetRecords().FirstOrDefault(record => record.ProjectId == SelectedProject.Id && record.TodoId is null);
            _focusResumeNote = record is null ? "" : _store.GetOutcome(record.Id)?.NextStep ?? "";
        }
        else _focusResumeNote = "";
        Notify(nameof(FocusResumeNote)); Notify(nameof(HasResumeNote));
    }
    public bool HasResumeNote => FocusResumeNote.Length > 0;
    private void RefreshTodoChoices(Guid? selectedId = null)
    {
        selectedId ??= _selectedFocusTodo.Id;
        var folderId = Todos.Items.FirstOrDefault(item => item.Id == selectedId)?.Item.FolderId ?? _selectedFocusTodoFolder.Id;
        if (folderId is null && Guid.TryParse(_store.GetSetting("focus.todoFolder"), out var savedFolder)) folderId = savedFolder;
        FocusTodoChoices.Clear(); FocusTodoChoices.Add(new(null, "不关联待办"));
        foreach (var item in Todos.Items.OrderBy(item => item.Completed)) FocusTodoChoices.Add(new(item.Id, item.Title));
        FocusTodoFolderChoices.Clear(); FocusTodoFolderChoices.Add(new(null, "不关联待办"));
        foreach (var folder in Todos.Folders) FocusTodoFolderChoices.Add(new(folder.Id, folder.Name));
        _selectedFocusTodoFolder = FocusTodoFolderChoices.FirstOrDefault(folder => folder.Id == folderId) ?? FocusTodoFolderChoices[0];
        Notify(nameof(SelectedFocusTodoFolder)); Notify(nameof(HasFocusTodoFolder));
        SelectedFocusTodo = FocusTodoChoices.FirstOrDefault(choice => choice.Id == selectedId) ?? FocusTodoChoices[0];
    }
    private void RefreshFolderTodoChoices()
    {
        FolderFocusTodoChoices.Clear(); FolderFocusTodoChoices.Add(FocusTodoChoices[0]);
        foreach (var choice in FocusTodoChoices.Where(choice => choice.Id is not null && Todos.Items.Any(item => item.Id == choice.Id && item.Item.FolderId == SelectedFocusTodoFolder.Id)))
            FolderFocusTodoChoices.Add(choice);
    }
    private void SaveFocusTodoFolder()
    {
        try { _store.SaveSetting("focus.todoFolder", SelectedFocusTodoFolder.Id?.ToString() ?? ""); }
        catch (Exception ex) { StatusMessage = $"待办文件夹选择保存失败：{ex.Message}"; }
    }
    public bool CanDeleteTodo(Guid id) => !(_timer.IsRunning && _activeTodoId == id)
        && _recovery?.Timer.Options.TodoId != id
        && !(HasUnsavedRecord && _timer.LastRecord!.TodoId == id);
    public bool StartFromTodo(Guid id, FocusProject? project = null, int? quickMinutes = null, string? category = null)
    {
        if (!CanConfigure || HasUnsavedRecord) { StatusMessage = "请先结束当前计时并保存记录，再开始另一项待办。"; return false; }
        var item = Todos.Items.FirstOrDefault(item => item.Id == id);
        if (item is null) { StatusMessage = "该待办已删除。"; return false; }
        if (quickMinutes is { } minutes) { SelectedTimerMode = TimerModes.Single(mode => mode.Mode == TimerMode.Countdown); DurationMinutes = minutes.ToString(CultureInfo.CurrentCulture); }
        if (Countdown)
        {
            if (!ValidateFocusDuration(DurationMinutes)) return false;
            if (!TryMinutes(DurationMinutes, false, out _)) { StatusMessage = "请输入有效的倒计时分钟数。"; return false; }
        }
        if (Pomodoro && !TryPomodoro(out _, out _, out _)) return false;
        if (project is null)
        {
            var targetCategory = category ?? "未分类";
            project = Projects.FirstOrDefault(item => item.Category == targetCategory && item.Name == "未分类");
            if (project is null)
            {
                project = new(Guid.NewGuid(), "未分类", targetCategory, "#7B97A6");
                try { _store.SaveFolder(targetCategory); _store.SaveProject(project); ReloadProjects(targetCategory, project.Id); }
                catch (Exception ex) { StatusMessage = $"未分类项目创建失败：{ex.Message}"; return false; }
            }
        }
        SelectedFolder = Folders.Single(folder => folder.Name == project.Category);
        SelectedProject = FolderProjects.Single(item => item.Id == project.Id);
        SelectedFocusTodo = FocusTodoChoices.Single(choice => choice.Id == id);
        StartCommand.Execute(null);
        return IsFocusActive && _activeTodoId == id;
    }
    public FocusOutcome? GetOutcome(Guid id) => _store.GetOutcome(id);
    public FocusRecord? GetRecord(Guid id) => _store.GetRecords().FirstOrDefault(record => record.Id == id);
    public bool SaveOutcome(FocusOutcome outcome)
    {
        if (outcome.Quantity is { } quantity && (!double.IsFinite(quantity) || quantity < 0 || string.IsNullOrWhiteSpace(outcome.Unit)))
        { StatusMessage = "成果数量须为非负数，并填写单位；也可以只记录文字。"; return false; }
        try
        {
            _store.SaveOutcome(outcome with { Summary = outcome.Summary.Trim(), Unit = outcome.Unit.Trim(), NextStep = outcome.NextStep.Trim() });
            if (GetRecord(outcome.SessionId)?.TodoId is { } id && _store.GetRecords().FirstOrDefault(record => record.TodoId == id)?.Id == outcome.SessionId
                && Todos.Items.FirstOrDefault(item => item.Id == id) is { } todo)
                _store.SaveTodo(todo.Item with { ResumeNote = outcome.NextStep.Trim() });
            LoadRecords(); Todos.Reload(); StatusMessage = "成果已保存，专注计时保持原状态。"; return true;
        }
        catch (Exception ex) { StatusMessage = $"成果保存失败：{ex.Message}"; return false; }
    }
    private static string FormatOutcome(FocusOutcome? outcome) => outcome is null ? "" :
        string.Join(" · ", new[] { outcome.Summary, outcome.Quantity is { } quantity ? $"{quantity:0.##} {outcome.Unit}" : "" }.Where(text => text.Length > 0));
}
