using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Restrictions;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record RecordRow(string Project, string Started, string Ended, string Duration, string Mode, string Reason, string RestrictionMode,
    Guid Id, string Todo, string Outcome, string NextStep, string Category, string Interceptions)
{
    public string OutcomeAction => string.IsNullOrEmpty(Outcome) && string.IsNullOrEmpty(NextStep) ? "补记成果" : "编辑成果";
}

public sealed partial class MainViewModel : ObservableObject
{
    private sealed record ProjectSelection(string Folder, Guid? ProjectId);
    private sealed record ProjectOrder(List<string> Folders, List<Guid> Projects);
    private ProjectOrder _projectOrder;
    private readonly IFocusStore _store;
    private readonly FocusTimer _timer;
    private Guid? _savedRecordId;
    private Guid? _attemptedRecordId;
    private FocusProject? _selectedProject;
    private Guid? _activeProjectId;
    private string _durationMinutes = "25";
    private string _pauseMinutes;
    private string _restMinutes;
    private string _pomodoroFocusMinutes;
    private string _pomodoroShortRestMinutes;
    private string _pomodoroLongRestMinutes;
    private string _pomodoroLongRestEvery;
    private string _pomodoroRounds;
    private Guid? _mediaSessionId;
    private TimerChoice _selectedTimerMode;
    private ProjectFolder? _selectedFolder;
    private string _newFolderName = "";
    private ThemePalette _selectedPalette;
    private AppearanceChoice _selectedAppearanceMode;
    private string _statusMessage = "";
    private string _newProjectName = "";
    private RestrictionChoice _selectedRestrictionMode;
    private TimerChoice _defaultTimerMode;
    private RestrictionChoice _defaultRestrictionMode;
    private TimerChoice _savedTimerMode;
    private RestrictionChoice _savedRestrictionMode;
    private bool _wasConfigurable = true;
    private bool _keepScreenOn;
    private bool _timerFloatVisible;
    private bool _timerFloatUseBackground;
    private double _timerFloatTransparency;
    private bool _lastRestrictionsActive;
    private AllowedApplication[] _sessionApplications = [];
    private WebsiteRule[] _sessionWebsites = [];

    public MainViewModel(IFocusStore store, FocusTimer? timer = null)
    {
        _store = store;
        _timer = timer ?? new FocusTimer();
        _pauseMinutes = store.GetSetting("pauseMinutes") ?? "5";
        _restMinutes = store.GetSetting("restMinutes") ?? "0";
        _pomodoroFocusMinutes = store.GetSetting("pomodoro.focusMinutes") ?? "25";
        _pomodoroShortRestMinutes = store.GetSetting("pomodoro.shortRestMinutes") ?? "5";
        _pomodoroLongRestMinutes = store.GetSetting("pomodoro.longRestMinutes") ?? "15";
        _pomodoroLongRestEvery = store.GetSetting("pomodoro.longRestEvery") ?? "4";
        _pomodoroRounds = store.GetSetting("pomodoro.rounds") ?? "4";
        _keepScreenOn = store.GetSetting("power.keepScreenOn") == "True";
        _timerFloatVisible = store.GetSetting("widget.timer.visible") == "True";
        _timerFloatUseBackground = store.GetSetting("widget.timer.background") == "True";
        _timerFloatTransparency = double.TryParse(store.GetSetting("widget.timer.transparency"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var transparency) ? Math.Clamp(transparency, 0, 70) : 0;
        var orderJson = store.GetSetting("projects.order");
        _projectOrder = orderJson is null ? new ProjectOrder([], []) : JsonSerializer.Deserialize<ProjectOrder>(orderJson)!;
        Projects = new ObservableCollection<FocusProject>(store.GetProjects());
        CollectionOrder.Apply(Projects, _projectOrder.Projects, project => project.Id);
        foreach (var name in store.GetFolders())
        {
            var folder = new ProjectFolder(name);
            foreach (var project in Projects.Where(p => p.Category == name)) folder.Add(project);
            Folders.Add(folder);
        }
        CollectionOrder.Apply(Folders, _projectOrder.Folders, folder => folder.Name);
        _defaultTimerMode = TimerModes.FirstOrDefault(choice => choice.Mode.ToString() == store.GetSetting("timer.mode")) ?? TimerModes[0];
        _defaultRestrictionMode = RestrictionModes.First(choice => choice.Strict == (store.GetSetting("timer.strict") == "True"));
        _selectedTimerMode = _defaultTimerMode;
        _selectedRestrictionMode = _defaultRestrictionMode;
        _savedTimerMode = _defaultTimerMode;
        _savedRestrictionMode = _defaultRestrictionMode;
        Whitelist = new RestrictionSettingsViewModel(store, message => StatusMessage = message);
        Media = new FocusMediaViewModel(store, message => StatusMessage = message);
        Whitelist.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(RestrictionSettingsViewModel.Websites) or nameof(RestrictionSettingsViewModel.WebsiteMode))
            { Notify(nameof(BrowserWebsites)); Notify(nameof(WebsiteBlockedMessage)); }
        };
        _selectedPalette = Appearance.Palettes.FirstOrDefault(p => p.Id == store.GetSetting("appearance.palette")) ?? Appearance.Palettes[0];
        _selectedAppearanceMode = Appearance.Modes.FirstOrDefault(m => m.Mode.ToString() == store.GetSetting("appearance.mode")) ?? Appearance.Modes[0];
        Appearance.Apply(_selectedPalette, _selectedAppearanceMode.Mode);
        Typography = new FocusTypographyViewModel(store, message => StatusMessage = message);
        var selectionJson = store.GetSetting("focus.selection");
        var selection = selectionJson is null ? null : JsonSerializer.Deserialize<ProjectSelection>(selectionJson);
        var selectedId = selection?.ProjectId ?? (selection is null ? store.GetRecords().FirstOrDefault()?.ProjectId : null);
        var selectedProject = Projects.FirstOrDefault(project => project.Id == selectedId);
        _selectedFolder = Folders.FirstOrDefault(folder => folder.Name == (selectedProject?.Category ?? selection?.Folder))
            ?? Folders.FirstOrDefault(folder => folder.Projects.Count > 0) ?? Folders.FirstOrDefault();
        if (_selectedFolder is not null) foreach (var project in _selectedFolder.Projects) FolderProjects.Add(project);
        _selectedProject = selectedProject ?? FolderProjects.FirstOrDefault();
        StartCommand = new RelayCommand(Start, () => CanConfigure && !HasUnsavedRecord);
        PauseCommand = new RelayCommand(() => { _timer.Pause(); Refresh(); }, () => _timer.State == FocusState.Focusing);
        ResumeCommand = new RelayCommand(() => { _timer.Resume(); Refresh(); }, () => _timer.State == FocusState.Paused);
        EndCommand = new RelayCommand(() => { _timer.End(); Refresh(); }, () => _timer.IsRunning);
        SkipRestCommand = new RelayCommand(() => { _timer.SkipRest(); Refresh(); }, () => _timer.State == FocusState.Resting && !HasUnsavedRecord);
        AddProjectCommand = new RelayCommand(AddProject);
        AddFolderCommand = new RelayCommand(AddFolder);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        RetrySaveCommand = new RelayCommand(() => { PersistRecord(); Refresh(); }, () => HasUnsavedRecord);
        LoadRecords();
        Todos = new TodoLibraryViewModel(store, ReportStatus, CanDeleteTodo);
        Calendar = new CalendarViewModel(store);
        FocusCalendar = new CalendarViewModel(store, true);
        Navigation = new NavigationViewModel(store);
        Statistics = new StatisticsViewModel(store);
        Goals = new GoalsViewModel(store, ReportStatus);
        Reflection = new ReflectionViewModel(store);
        Reflection.Changed += (_, _) => CalendarDataChanged?.Invoke(this, EventArgs.Empty);
        Goals.Changed += (_, _) => { Statistics.Reload(); Reflection.Reload(); CalendarDataChanged?.Invoke(this, EventArgs.Empty); };
        _recordPage = store.GetSetting("records.page") ?? "时间轴"; Statistics.ReviewMode = _recordPage == "复盘";
        _softwareRemindersEnabled = store.GetSetting("reminders.enabled") != "False";
        _outlookSyncRequested = store.GetSetting("outlook.sync.requested") == "True";
        DateEvents = new DateEventsViewModel(store);
        DateEvents.Changed += (_, _) => CalendarDataChanged?.Invoke(this, EventArgs.Empty);
        CardSizing = new CardSizingViewModel(store, () => { Calendar.Reload(); FocusCalendar.Reload(); Statistics.RequestRedraw(); Reflection.RequestRedraw(); });
        Todos.Changed += (_, _) => { RefreshTodoChoices(); LoadRecords(); ReloadCalendarData(); };
        var savedTodo = store.GetSetting("focus.todo");
        RefreshTodoChoices(Guid.TryParse(savedTodo, out var todoId) ? todoId : null);
        SaveProjectSelection();
        Widgets = new DesktopWidgetsViewModel(store);
    }

    public ObservableCollection<FocusProject> Projects { get; }
    public RestrictionSettingsViewModel Whitelist { get; }
    public FocusMediaViewModel Media { get; }
    public FocusTypographyViewModel Typography { get; }
    public CardSizingViewModel CardSizing { get; }
    public IReadOnlyList<RestrictionChoice> RestrictionModes { get; } = new[]
    {
        new RestrictionChoice(false, "普通模式"), new RestrictionChoice(true, "严格模式")
    };
    public RestrictionChoice SelectedRestrictionMode
    {
        get => _selectedRestrictionMode;
        set { _selectedRestrictionMode = value; Notify(); Notify(nameof(SessionModeText)); }
    }
    public TimerChoice DefaultTimerMode
    {
        get => _defaultTimerMode;
        set { _defaultTimerMode = value; Notify(); Notify(nameof(DefaultPomodoro)); }
    }
    public bool DefaultPomodoro => DefaultTimerMode.Mode == TimerMode.Pomodoro;
    public RestrictionChoice DefaultRestrictionMode
    {
        get => _defaultRestrictionMode;
        set { _defaultRestrictionMode = value; Notify(); }
    }
    public bool RestrictionsActive => _timer.RestrictionsActive;
    public bool IsFocusActive => _timer.IsActive;
    public bool TimerFloatVisible
    {
        get => _timerFloatVisible;
        set
        {
            if (_timerFloatVisible == value) return;
            _timerFloatVisible = value; Notify();
            try { _store.SaveSetting("widget.timer.visible", value.ToString()); }
            catch (Exception ex) { StatusMessage = $"浮窗偏好保存失败：{ex.Message}"; }
        }
    }
    public bool TimerFloatUseBackground
    {
        get => _timerFloatUseBackground;
        set
        {
            if (_timerFloatUseBackground == value) return;
            _timerFloatUseBackground = value; Notify();
            try { _store.SaveSetting("widget.timer.background", value.ToString()); }
            catch (Exception ex) { StatusMessage = $"浮窗背景偏好保存失败：{ex.Message}"; }
        }
    }
    public double TimerFloatTransparency
    {
        get => _timerFloatTransparency;
        set
        {
            value = Math.Clamp(value, 0, 70);
            if (_timerFloatTransparency == value) return;
            _timerFloatTransparency = value; Notify();
            try { _store.SaveSetting("widget.timer.transparency", value.ToString(CultureInfo.InvariantCulture)); }
            catch (Exception ex) { StatusMessage = $"浮窗透明度保存失败：{ex.Message}"; }
        }
    }
    public bool KeepScreenOn
    {
        get => _keepScreenOn;
        set
        {
            _keepScreenOn = value;
            Notify();
            try { _store.SaveSetting("power.keepScreenOn", value.ToString()); }
            catch (Exception ex) { StatusMessage = $"常亮偏好保存失败：{ex.Message}"; }
        }
    }
    public bool CanEditWhitelist => !_timer.IsActive;
    public IReadOnlyList<AllowedApplication> SessionApplications => _sessionApplications;
    public IReadOnlyList<WebsiteRule> SessionWebsites => _sessionWebsites;
    public AccessListMode SessionApplicationMode { get; private set; }
    public AccessListMode SessionWebsiteMode { get; private set; }
    public IEnumerable<WebsiteRule> BrowserWebsites => (RestrictionsActive ? SessionWebsiteMode : Whitelist.WebsiteMode.Mode) == AccessListMode.Blacklist
        ? Array.Empty<WebsiteRule>() : RestrictionsActive ? SessionWebsites : Whitelist.Websites;
    public string WebsiteBlockedMessage => SessionWebsiteMode == AccessListMode.Blacklist ? "此网站在本次黑名单中" : "此网站不在本次白名单中";
    public string SessionModeText => CanConfigure
        ? SelectedRestrictionMode.Name : _timer.IsStrictSession ? "严格模式" : "普通模式";
    public string RestrictionStatus => RestrictionsActive ? "严格限制生效 · 暂停期间保持限制" : "应用与网站限制已解除";
    public bool CanNavigateWebsite(string address) => WebsiteAccess.IsAllowed(address, RestrictionsActive, SessionWebsites, SessionWebsiteMode);
    public event EventHandler? RestrictionsChanged;
    public event EventHandler? ShortFocusDiscarded;
    public event EventHandler? FocusDurationRejected;
    public void RegisterInterception() => _timer.RegisterInterception();
    public ObservableCollection<ProjectFolder> Folders { get; } = [];
    public ObservableCollection<FocusProject> FolderProjects { get; } = [];
    public IReadOnlyList<ThemePalette> Palettes => Appearance.Palettes;
    public IReadOnlyList<AppearanceChoice> AppearanceModes => Appearance.Modes;
    public ThemePalette SelectedPalette
    {
        get => _selectedPalette;
        set { if (value is null || value == _selectedPalette) return; _selectedPalette = value; Notify(); UpdateAppearance(); }
    }
    public AppearanceChoice SelectedAppearanceMode
    {
        get => _selectedAppearanceMode;
        set { if (value is null || value == _selectedAppearanceMode) return; _selectedAppearanceMode = value; Notify(); UpdateAppearance(); }
    }
    public IReadOnlyList<TimerChoice> TimerModes { get; } = new[]
    {
        new TimerChoice(TimerMode.CountUp, "正计时"), new TimerChoice(TimerMode.Countdown, "倒计时"),
        new TimerChoice(TimerMode.Pomodoro, "番茄循环")
    };
    public ObservableCollection<RecordRow> Records { get; } = [];
    public RelayCommand StartCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand EndCommand { get; }
    public RelayCommand SkipRestCommand { get; }
    public RelayCommand AddProjectCommand { get; }
    public RelayCommand AddFolderCommand { get; }
    public RelayCommand SaveSettingsCommand { get; }
    public RelayCommand RetrySaveCommand { get; }
    public FocusProject? SelectedProject
    {
        get => _selectedProject;
        set
        {
            _selectedProject = value;
            Notify();
            Notify(nameof(SelectedLibraryProject));
            Notify(nameof(ProjectPath));
            Notify(nameof(FocusHeading));
            UpdateResumeNote();
            SaveProjectSelection();
        }
    }
    public ProjectFolder? SelectedFolder
    {
        get => _selectedFolder;
        set
        {
            if (_selectedFolder == value) return;
            _selectedFolder = value;
            FolderProjects.Clear();
            if (value is not null) foreach (var project in value.Projects) FolderProjects.Add(project);
            SelectedProject = FolderProjects.FirstOrDefault();
            Notify();
            Notify(nameof(FolderTitle));
            Notify(nameof(FolderSummary));
            Notify(nameof(IsFolderEmpty));
            RefreshProjectLibrary();
        }
    }
    private void SaveProjectSelection()
    {
        if (_selectedFolder is null) return;
        if (_selectedProject is null && _selectedFolder.Projects.Count > 0) return;
        if (_selectedProject is not null && !_selectedFolder.Projects.Any(project => project.Id == _selectedProject.Id)) return;
        try { _store.SaveSetting("focus.selection", JsonSerializer.Serialize(new ProjectSelection(_selectedFolder.Name, _selectedProject?.Id))); }
        catch (Exception ex) { StatusMessage = $"项目选择保存失败：{ex.Message}"; }
    }
    public string DurationMinutes { get => _durationMinutes; set { _durationMinutes = value; Notify(); } }
    public string PauseMinutes { get => _pauseMinutes; set { _pauseMinutes = value; Notify(); } }
    public string RestMinutes { get => _restMinutes; set { _restMinutes = value; Notify(); } }
    public string PomodoroFocusMinutes { get => _pomodoroFocusMinutes; set { _pomodoroFocusMinutes = value; Notify(); } }
    public string PomodoroShortRestMinutes { get => _pomodoroShortRestMinutes; set { _pomodoroShortRestMinutes = value; Notify(); } }
    public string PomodoroLongRestMinutes { get => _pomodoroLongRestMinutes; set { _pomodoroLongRestMinutes = value; Notify(); } }
    public string PomodoroLongRestEvery { get => _pomodoroLongRestEvery; set { _pomodoroLongRestEvery = value; Notify(); } }
    public string PomodoroRounds { get => _pomodoroRounds; set { _pomodoroRounds = value; Notify(); } }
    public TimerChoice SelectedTimerMode
    {
        get => _selectedTimerMode;
        set { _selectedTimerMode = value; Notify(); Notify(nameof(Countdown)); Notify(nameof(Pomodoro)); Notify(nameof(SingleTimer)); }
    }
    public bool Countdown => SelectedTimerMode.Mode == TimerMode.Countdown;
    public bool Pomodoro => SelectedTimerMode.Mode == TimerMode.Pomodoro;
    public bool SingleTimer => !Pomodoro;
    public string NewProjectName { get => _newProjectName; set { _newProjectName = value; Notify(); } }
    public string NewFolderName { get => _newFolderName; set { _newFolderName = value; Notify(); } }
    public string FolderTitle => SelectedFolder?.Name ?? "项目库";
    public string FolderSummary => $"{FolderProjects.Count} 个项目 · 选择后可在专注页开始计时";
    public bool IsFolderEmpty => FolderProjects.Count == 0;
    public string ProjectPath => SelectedProject is { } project ? $"{project.Category} / {project.Name}" : "请选择文件夹中的项目";
    public string FocusHeading => _timer.State == FocusState.Ready ? SelectedProject?.Name ?? "选择一个项目" : ActiveProjectName;
    public bool ShowStart => CanConfigure;
    public bool ShowPause => _timer.State == FocusState.Focusing;
    public bool ShowResume => _timer.State == FocusState.Paused;
    public bool ShowEnd => _timer.IsRunning;
    public bool ShowSkipRest => _timer.State == FocusState.Resting;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; Notify(); } }
    public bool CanConfigure => _timer.State is FocusState.Ready or FocusState.Ended;
    public bool HasUnsavedRecord => _timer.LastRecord is { } record && record.Id != _savedRecordId;
    public string TimerText => Format(_timer.DisplayTime);
    public string StateText => _timer.State switch
    {
        FocusState.Focusing => "专注中",
        FocusState.Paused => "已暂停",
        FocusState.Resting => _timer.IsPomodoro ? _timer.IsLongRest ? "长休息" : "短休息" : "休息中",
        FocusState.Ended => "本次已结束",
        _ => "准备开始"
    };
    public FocusState State => _timer.State;
    public string RoundText => _timer.IsPomodoro && !CanConfigure ? $"第 {_timer.Round} / {_timer.TotalRounds} 轮 · " : "";
    public string PhaseText => RoundText + (_timer.State switch
    {
        FocusState.Paused => $"{Format(_timer.PhaseRemaining)} 后自动继续",
        FocusState.Resting => $"休息剩余 {Format(_timer.PhaseRemaining)}",
        _ => $"有效专注 {Format(_timer.Elapsed)}"
    });
    public string ActiveProjectName { get; private set; } = "一刻专注";
    public string RecordSummary => $"共 {Records.Count} 次专注 · 数据保存在本机";

    public void Tick()
    {
        _timer.Update(allowNextRound: !HasUnsavedRecord);
        Todos.RefreshDeadlines();
        RefreshCalendarDay();
        Refresh();
    }

    public void RefreshSystemTheme() => Appearance.Apply(SelectedPalette, SelectedAppearanceMode.Mode);
    public void ReportStatus(string message) => StatusMessage = message;

    private void UpdateAppearance()
    {
        Appearance.Apply(SelectedPalette, SelectedAppearanceMode.Mode);
        try
        {
            _store.SaveSetting("appearance.palette", SelectedPalette.Id);
            _store.SaveSetting("appearance.mode", SelectedAppearanceMode.Mode.ToString());
            StatusMessage = "外观已更新并保存。";
        }
        catch (Exception ex) { StatusMessage = $"外观保存失败：{ex.Message}"; }
    }

    public bool PrepareExit()
    {
        _timer.End(EndReason.ApplicationExit, false);
        var saved = PersistRecord();
        Refresh();
        return saved;
    }

    private void Start()
    {
        if (SelectedProject is null) { StatusMessage = "请选择专注项目。"; return; }
        if (Countdown && !ValidateFocusDuration(DurationMinutes)) return;
        if (!TryMinutes(PauseMinutes, false, out var pause) || !TryMinutes(RestMinutes, true, out var rest)
            || (Countdown && !TryMinutes(DurationMinutes, false, out _)))
        {
            StatusMessage = "请输入有效分钟数：倒计时与暂停大于 0，休息可设为 0。";
            return;
        }
        PomodoroOptions? cycle = null;
        var duration = Countdown ? TimeSpan.FromMinutes(double.Parse(DurationMinutes, CultureInfo.CurrentCulture)) : TimeSpan.Zero;
        if (Pomodoro && !TryPomodoro(out duration, out rest, out cycle)) return;
        _timer.Start(new FocusOptions(SelectedProject.Id, SelectedTimerMode.Mode,
            duration, pause, rest, SelectedRestrictionMode.Strict, cycle, SelectedFocusTodo.Id));
        ActiveProjectName = SelectedProject.Name;
        _activeProjectId = SelectedProject.Id;
        _activeTodoId = SelectedFocusTodo.Id;
        _activeTodoTitle = Todos.Items.FirstOrDefault(item => item.Id == _activeTodoId)?.Title ?? "";
        var todoStateSaved = true;
        if (_activeTodoId is { } activeId && Todos.Items.FirstOrDefault(item => item.Id == activeId) is { } active)
        {
            try
            {
                _store.SaveTodo(active.Item with { PreferredProjectId = SelectedProject.Id,
                    Status = active.Item.Status == TodoStatus.Pending ? TodoStatus.InProgress : active.Item.Status });
                Todos.Reload();
            }
            catch (Exception ex) { todoStateSaved = false; StatusMessage = $"待办状态保存失败：{ex.Message}"; }
        }
        if (todoStateSaved) StatusMessage = $"{SelectedRestrictionMode.Name} · 可以随时结束";
        Refresh();
    }

    private void AddProject()
    {
        if (CreateProject(NewProjectName, SelectedFolder)) NewProjectName = "";
    }

    public bool CreateProject(string name, ProjectFolder? folder)
    {
        name = name.Trim();
        if (name.Length == 0) { StatusMessage = "请填写项目名称。"; return false; }
        if (folder is null) { StatusMessage = "请先选择或新建一个文件夹。"; return false; }
        if (folder.Projects.Any(project => string.Equals(project.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        { StatusMessage = "这个文件夹中已有同名项目，请使用其他名称。"; return false; }
        var project = new FocusProject(Guid.NewGuid(), name, folder.Name, "#9683BE");
        try
        {
            _store.SaveProject(project);
            Projects.Add(project);
            folder.Add(project);
            if (SelectedFolder == folder) FolderProjects.Add(project);
            SaveProjectOrder();
            if (CanConfigure) { SelectedFolder = folder; SelectedProject = project; }
            StatusMessage = "项目已保存。";
            Notify(nameof(FolderSummary));
            Notify(nameof(IsFolderEmpty));
            RefreshProjectLibrary();
            return true;
        }
        catch (Exception ex) { StatusMessage = $"项目保存失败：{ex.Message}"; return false; }
    }

    private void AddFolder()
    {
        if (CreateFolder(NewFolderName)) NewFolderName = "";
    }

    public bool CreateFolder(string name)
    {
        name = name.Trim();
        if (name.Length == 0) { StatusMessage = "请填写文件夹名称。"; return false; }
        var existing = Folders.FirstOrDefault(f => string.Equals(f.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            StatusMessage = "已有同名文件夹，请使用其他名称。";
            return false;
        }
        try
        {
            _store.SaveFolder(name);
            var folder = new ProjectFolder(name);
            Folders.Add(folder);
            SaveProjectOrder();
            SelectedFolder = folder;
            StatusMessage = "文件夹已创建，可以添加项目。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"文件夹保存失败：{ex.Message}"; return false; }
    }

    public bool RenameFolder(ProjectFolder folder, string name)
    {
        name = name.Trim();
        if (name.Length == 0) { StatusMessage = "请填写文件夹名称。"; return false; }
        if (name == folder.Name) return true;
        if (Folders.Any(item => item != folder && string.Equals(item.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))) { StatusMessage = "已有同名文件夹，请使用其他名称。"; return false; }
        try
        {
            var selectedId = SelectedProject?.Id;
            _store.RenameFolder(folder.Name, name);
            _projectOrder = _projectOrder with { Folders = Folders.Select(item => item == folder ? name : item.Name).ToList() };
            ReloadProjects(name, selectedId);
            StatusMessage = "文件夹名称已保存。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"文件夹修改失败：{ex.Message}"; return false; }
    }

    public bool UpdateProject(FocusProject project, string name, ProjectFolder? folder)
    {
        name = name.Trim();
        if (name.Length == 0) { StatusMessage = "请填写项目名称。"; return false; }
        if (folder is null) { StatusMessage = "请选择文件夹。"; return false; }
        if (folder.Projects.Any(item => item.Id != project.Id && string.Equals(item.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        { StatusMessage = "这个文件夹中已有同名项目，请使用其他名称。"; return false; }
        try
        {
            if (project.Category != folder.Name) _store.SaveFolder(project.Category);
            _store.SaveProject(project with { Name = name, Category = folder.Name });
            if (project.Category != folder.Name)
                _projectOrder = _projectOrder with { Projects = Projects.Where(item => item.Id != project.Id).Select(item => item.Id).Append(project.Id).ToList() };
            ReloadProjects(folder.Name, project.Id);
            if (_activeProjectId == project.Id)
            {
                ActiveProjectName = name;
                Notify(nameof(ActiveProjectName)); Notify(nameof(FocusHeading));
            }
            StatusMessage = "项目已修改并保存。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"项目修改失败：{ex.Message}"; return false; }
    }

    public bool CanDeleteProject(FocusProject project) => !(_timer.IsRunning && _activeProjectId == project.Id)
        && !(HasUnsavedRecord && _timer.LastRecord!.ProjectId == project.Id);
    public bool CanDeleteFolder(ProjectFolder folder) => folder.Projects.All(CanDeleteProject);

    public bool DeleteProject(FocusProject project)
    {
        if (!CanDeleteProject(project)) { StatusMessage = "请先结束该项目的专注并保存记录，再删除。"; return false; }
        try
        {
            _store.DeleteProject(project.Id);
            ReloadProjects(SelectedFolder?.Name, SelectedProject?.Id);
            StatusMessage = "项目已删除，历史专注记录保留。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"项目删除失败：{ex.Message}"; return false; }
    }

    public bool DeleteFolder(ProjectFolder folder)
    {
        if (!CanDeleteFolder(folder)) { StatusMessage = "文件夹中有正在专注或记录未保存的项目，请先处理后再删除。"; return false; }
        try
        {
            _store.DeleteFolder(folder.Name);
            ReloadProjects(SelectedFolder?.Name, SelectedProject?.Id);
            StatusMessage = "文件夹及其中的项目已删除，历史专注记录保留。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"文件夹删除失败：{ex.Message}"; return false; }
    }

    private void ReloadProjects(string? folderName, Guid? projectId)
    {
        SelectedFolder = null;
        Projects.Clear(); Folders.Clear();
        foreach (var project in _store.GetProjects()) Projects.Add(project);
        CollectionOrder.Apply(Projects, _projectOrder.Projects, project => project.Id);
        foreach (var name in _store.GetFolders())
        {
            var folder = new ProjectFolder(name);
            foreach (var project in Projects.Where(project => project.Category == name)) folder.Add(project);
            Folders.Add(folder);
        }
        CollectionOrder.Apply(Folders, _projectOrder.Folders, folder => folder.Name);
        SelectedFolder = Folders.FirstOrDefault(folder => folder.Name == folderName)
            ?? Folders.FirstOrDefault(folder => folder.Projects.Count > 0) ?? Folders.FirstOrDefault();
        SelectedProject = FolderProjects.FirstOrDefault(project => project.Id == projectId) ?? FolderProjects.FirstOrDefault();
        LoadRecords();
        SaveProjectOrder();
    }

    public void MoveFolder(ProjectFolder folder, OrderMove direction, ProjectFolder? target = null)
    {
        var selected = SelectedFolder;
        var selectedProject = SelectedProject;
        if (!CollectionOrder.Move(Folders, folder, direction, targetItem: target)) return;
        SelectedFolder = selected;
        SelectedProject = selectedProject;
        SaveProjectOrder();
    }
    public void MoveProject(FocusProject project, OrderMove direction, FocusProject? target = null)
    {
        var selected = SelectedProject;
        if (!CollectionOrder.Move(Projects, project, direction, ProjectLibraryItems.ToArray(), target)) return;
        var order = Projects.Select(item => item.Id).ToArray();
        CollectionOrder.Apply(FolderProjects, order, item => item.Id);
        foreach (var folder in Folders) CollectionOrder.Apply(folder.Projects, order, item => item.Id);
        SelectedProject = selected;
        SaveProjectOrder();
    }
    private void SaveProjectOrder()
    {
        _projectOrder = new ProjectOrder(Folders.Select(folder => folder.Name).ToList(), Projects.Select(project => project.Id).ToList());
        try { _store.SaveSetting("projects.order", JsonSerializer.Serialize(_projectOrder)); }
        catch (Exception ex) { StatusMessage = $"顺序保存失败：{ex.Message}"; }
    }

    private void SaveSettings()
    {
        if (!TryMinutes(PauseMinutes, false, out _) || !TryMinutes(RestMinutes, true, out _))
        {
            StatusMessage = "暂停分钟数须大于 0，休息分钟数可为 0。";
            return;
        }
        if (!TryPomodoro(out _, out _, out _)) return;
        try
        {
            _store.SaveSetting("pauseMinutes", PauseMinutes);
            _store.SaveSetting("restMinutes", RestMinutes);
            _store.SaveSetting("timer.mode", DefaultTimerMode.Mode.ToString());
            _store.SaveSetting("timer.strict", DefaultRestrictionMode.Strict.ToString());
            _store.SaveSetting("pomodoro.focusMinutes", PomodoroFocusMinutes);
            _store.SaveSetting("pomodoro.shortRestMinutes", PomodoroShortRestMinutes);
            _store.SaveSetting("pomodoro.longRestMinutes", PomodoroLongRestMinutes);
            _store.SaveSetting("pomodoro.longRestEvery", PomodoroLongRestEvery);
            _store.SaveSetting("pomodoro.rounds", PomodoroRounds);
            _savedTimerMode = DefaultTimerMode;
            _savedRestrictionMode = DefaultRestrictionMode;
            if (CanConfigure) ApplyDefaultModes();
            StatusMessage = "偏好已保存，下次专注生效。";
        }
        catch (Exception ex) { StatusMessage = $"偏好保存失败：{ex.Message}"; }
    }

    private void ApplyDefaultModes()
    {
        SelectedTimerMode = _savedTimerMode;
        SelectedRestrictionMode = _savedRestrictionMode;
    }

    private bool PersistRecord()
    {
        if (!HasUnsavedRecord) return true;
        _attemptedRecordId = _timer.LastRecord!.Id;
        if (_timer.LastRecord.FocusDuration < TimeSpan.FromSeconds(10))
        {
            _savedRecordId = _timer.LastRecord.Id;
            StatusMessage = "本次有效专注不足10秒，未计入专注记录。";
            ShortFocusDiscarded?.Invoke(this, EventArgs.Empty);
            return true;
        }
        try
        {
            _store.SaveRecord(_timer.LastRecord!);
            _savedRecordId = _timer.LastRecord!.Id;
            LastSavedRecordId = _savedRecordId;
            LoadRecords();
            Todos?.Reload();
            StatusMessage = "本次专注已保存。";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"记录保存失败，请重试保存：{ex.Message}";
            return false;
        }
    }

    private void LoadRecords()
    {
        Records.Clear();
        var projectItems = _store.GetProjects(includeDeleted: true);
        var projects = projectItems.ToDictionary(p => p.Id, p => p.Name);
        var categories = projectItems.ToDictionary(p => p.Id, p => p.Category);
        var todos = _store.GetTodos(includeDeleted: true).ToDictionary(item => item.Id, item => item.Title);
        var outcomes = _store.GetOutcomes().ToDictionary(outcome => outcome.SessionId);
        foreach (var record in _store.GetRecords())
        {
            var outcome = outcomes.GetValueOrDefault(record.Id);
            Records.Add(new RecordRow(projects.GetValueOrDefault(record.ProjectId, "未分类"),
                record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                record.EndedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), Format(record.FocusDuration),
                record.Mode switch { TimerMode.CountUp => "正计时", TimerMode.Pomodoro => "番茄循环", _ => "倒计时" },
                record.EndReason switch { EndReason.Manual => "手动结束", EndReason.CountdownElapsed => "到点结束", _ => "退出软件" },
                record.Strict ? "严格" : "普通", record.Id,
                record.TodoId is { } todoId ? todos.GetValueOrDefault(todoId, "") : "",
                FormatOutcome(outcome), outcome?.NextStep ?? "", categories.GetValueOrDefault(record.ProjectId, "未分类"), record.Strict ? record.InterceptionCount?.ToString(CultureInfo.CurrentCulture) ?? "未统计" : "—"));
        }
        Notify(nameof(RecordSummary));
        Calendar?.Reload(); FocusCalendar?.Reload(); Statistics?.Reload(); Goals?.Reload(); Reflection?.Reload();
    }

    private void Refresh()
    {
        if (HasUnsavedRecord && _timer.LastRecord!.Id != _attemptedRecordId) PersistRecord();
        if (_timer.IsActive && _mediaSessionId != _timer.SessionId)
        {
            _mediaSessionId = _timer.SessionId;
            _sessionApplications = Whitelist.Applications.ToArray();
            _sessionWebsites = Whitelist.Websites.ToArray();
            SessionApplicationMode = Whitelist.ApplicationMode.Mode;
            SessionWebsiteMode = Whitelist.WebsiteMode.Mode;
            Media.BeginSession();
        }
        if (!_timer.IsActive) Media.ReleaseSession();
        if (CanConfigure && !_wasConfigurable) ApplyDefaultModes();
        _wasConfigurable = CanConfigure;
        foreach (var name in new[] { nameof(TimerText), nameof(StateText), nameof(State), nameof(PhaseText), nameof(CanConfigure),
            nameof(ActiveProjectName), nameof(FocusHeading), nameof(HasUnsavedRecord), nameof(ShowStart),
            nameof(ShowPause), nameof(ShowResume), nameof(ShowEnd), nameof(ShowSkipRest) }) Notify(name);
        foreach (var name in new[] { nameof(FocusTodoText), nameof(HasFocusTodo), nameof(ShowOutcomeEntry) }) Notify(name);
        foreach (var name in new[] { nameof(IsFocusActive), nameof(RestrictionsActive), nameof(CanEditWhitelist), nameof(SessionModeText), nameof(RestrictionStatus), nameof(BrowserWebsites), nameof(WebsiteBlockedMessage) }) Notify(name);
        if (_lastRestrictionsActive != RestrictionsActive)
        {
            _lastRestrictionsActive = RestrictionsActive;
            RestrictionsChanged?.Invoke(this, EventArgs.Empty);
        }
        foreach (var command in new[] { StartCommand, PauseCommand, ResumeCommand, EndCommand, SkipRestCommand, RetrySaveCommand }) command.Refresh();
    }

    private bool TryPomodoro(out TimeSpan focus, out TimeSpan rest, out PomodoroOptions? cycle)
    {
        cycle = null;
        focus = rest = TimeSpan.Zero;
        if (!ValidateFocusDuration(PomodoroFocusMinutes)) return false;
        if (!TryMinutes(PomodoroFocusMinutes, false, out focus)
            || !TryMinutes(PomodoroShortRestMinutes, true, out rest)
            || !TryMinutes(PomodoroLongRestMinutes, true, out var longRest)
            || !int.TryParse(PomodoroLongRestEvery, out var every) || every is < 1 or > 100
            || !int.TryParse(PomodoroRounds, out var rounds) || rounds is < 1 or > 100)
        {
            StatusMessage = "番茄专注须大于0分钟，休息可为0；轮数和长休息间隔须为1至100的整数。";
            return false;
        }
        cycle = new PomodoroOptions(longRest, every, rounds);
        return true;
    }

    private bool ValidateFocusDuration(string text)
    {
        if (!double.TryParse(text, out var minutes) || !double.IsFinite(minutes) || minutes >= 10d / 60) return true;
        StatusMessage = "倒计时和番茄循环的专注时长不能少于10秒，请重新设置。";
        FocusDurationRejected?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private static bool TryMinutes(string text, bool allowZero, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (!double.TryParse(text, out var minutes) || !double.IsFinite(minutes)
            || minutes < 0 || (!allowZero && minutes == 0) || minutes > 1440) return false;
        duration = TimeSpan.FromMinutes(minutes);
        return true;
    }

    private static string Format(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
}
