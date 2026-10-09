using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Restrictions;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record RecordRow(string Project, string Started, string Ended, string Duration, string Mode, string Reason, string RestrictionMode);

public sealed class MainViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly FocusTimer _timer;
    private Guid? _savedRecordId;
    private Guid? _attemptedRecordId;
    private FocusProject? _selectedProject;
    private string _durationMinutes = "25";
    private string _pauseMinutes;
    private string _restMinutes;
    private TimerChoice _selectedTimerMode;
    private ProjectFolder? _selectedFolder;
    private string _newFolderName = "";
    private ThemePalette _selectedPalette;
    private AppearanceChoice _selectedAppearanceMode;
    private string _statusMessage = "选择一个项目，开始这一刻。";
    private string _newProjectName = "";
    private RestrictionChoice _selectedRestrictionMode;
    private TimerChoice _defaultTimerMode;
    private RestrictionChoice _defaultRestrictionMode;
    private TimerChoice _savedTimerMode;
    private RestrictionChoice _savedRestrictionMode;
    private bool _wasConfigurable = true;
    private bool _keepScreenOn;
    private bool _lastRestrictionsActive;
    private AllowedApplication[] _sessionApplications = [];
    private WebsiteRule[] _sessionWebsites = [];

    public MainViewModel(IFocusStore store, FocusTimer? timer = null)
    {
        _store = store;
        _timer = timer ?? new FocusTimer();
        _pauseMinutes = store.GetSetting("pauseMinutes") ?? "5";
        _restMinutes = store.GetSetting("restMinutes") ?? "0";
        _keepScreenOn = store.GetSetting("power.keepScreenOn") == "True";
        Projects = new ObservableCollection<FocusProject>(store.GetProjects());
        foreach (var name in store.GetFolders())
        {
            var folder = new ProjectFolder(name);
            foreach (var project in Projects.Where(p => p.Category == name)) folder.Add(project);
            Folders.Add(folder);
        }
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
        SelectedFolder = Folders.FirstOrDefault(f => f.Projects.Count > 0) ?? Folders.FirstOrDefault();
        StartCommand = new RelayCommand(Start, () => CanConfigure && !HasUnsavedRecord);
        PauseCommand = new RelayCommand(() => { _timer.Pause(); Refresh(); }, () => _timer.State == FocusState.Focusing);
        ResumeCommand = new RelayCommand(() => { _timer.Resume(); Refresh(); }, () => _timer.State == FocusState.Paused);
        EndCommand = new RelayCommand(() => { _timer.End(); Refresh(); }, () => _timer.IsActive);
        SkipRestCommand = new RelayCommand(() => { _timer.SkipRest(); Refresh(); }, () => _timer.State == FocusState.Resting);
        AddProjectCommand = new RelayCommand(AddProject);
        AddFolderCommand = new RelayCommand(AddFolder);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        RetrySaveCommand = new RelayCommand(() => { PersistRecord(); Refresh(); }, () => HasUnsavedRecord);
        LoadRecords();
    }

    public ObservableCollection<FocusProject> Projects { get; }
    public RestrictionSettingsViewModel Whitelist { get; }
    public FocusMediaViewModel Media { get; }
    public FocusTypographyViewModel Typography { get; }
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
        set { _defaultTimerMode = value; Notify(); }
    }
    public RestrictionChoice DefaultRestrictionMode
    {
        get => _defaultRestrictionMode;
        set { _defaultRestrictionMode = value; Notify(); }
    }
    public bool RestrictionsActive => _timer.RestrictionsActive;
    public bool IsFocusActive => _timer.IsActive;
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
        new TimerChoice(TimerMode.CountUp, "正计时"), new TimerChoice(TimerMode.Countdown, "倒计时")
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
            Notify(nameof(ProjectPath));
            Notify(nameof(FocusHeading));
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
        }
    }
    public string DurationMinutes { get => _durationMinutes; set { _durationMinutes = value; Notify(); } }
    public string PauseMinutes { get => _pauseMinutes; set { _pauseMinutes = value; Notify(); } }
    public string RestMinutes { get => _restMinutes; set { _restMinutes = value; Notify(); } }
    public TimerChoice SelectedTimerMode
    {
        get => _selectedTimerMode;
        set { _selectedTimerMode = value; Notify(); Notify(nameof(Countdown)); }
    }
    public bool Countdown => SelectedTimerMode.Mode == TimerMode.Countdown;
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
    public bool ShowEnd => _timer.IsActive;
    public bool ShowSkipRest => _timer.State == FocusState.Resting;
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; Notify(); } }
    public bool CanConfigure => _timer.State is FocusState.Ready or FocusState.Ended;
    public bool HasUnsavedRecord => _timer.LastRecord is { } record && record.Id != _savedRecordId;
    public string TimerText => Format(_timer.DisplayTime);
    public string StateText => _timer.State switch
    {
        FocusState.Focusing => "专注中",
        FocusState.Paused => "已暂停",
        FocusState.Resting => "休息中",
        FocusState.Ended => "本次已结束",
        _ => "准备开始"
    };
    public FocusState State => _timer.State;
    public string PhaseText => _timer.State switch
    {
        FocusState.Paused => $"{Format(_timer.PhaseRemaining)} 后自动继续",
        FocusState.Resting => $"休息剩余 {Format(_timer.PhaseRemaining)}",
        _ => $"有效专注 {Format(_timer.Elapsed)}"
    };
    public string ActiveProjectName { get; private set; } = "一刻专注";
    public string RecordSummary => $"共 {Records.Count} 次专注 · 数据保存在本机";

    public void Tick()
    {
        _timer.Update();
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
        if (!TryMinutes(PauseMinutes, false, out var pause) || !TryMinutes(RestMinutes, true, out var rest)
            || (Countdown && !TryMinutes(DurationMinutes, false, out _)))
        {
            StatusMessage = "请输入有效分钟数：倒计时与暂停大于 0，休息可设为 0。";
            return;
        }
        var duration = Countdown ? TimeSpan.FromMinutes(double.Parse(DurationMinutes, CultureInfo.CurrentCulture)) : TimeSpan.Zero;
        _timer.Start(new FocusOptions(SelectedProject.Id, Countdown ? TimerMode.Countdown : TimerMode.CountUp,
            duration, pause, rest, SelectedRestrictionMode.Strict));
        _sessionApplications = Whitelist.Applications.ToArray();
        _sessionWebsites = Whitelist.Websites.ToArray();
        SessionApplicationMode = Whitelist.ApplicationMode.Mode;
        SessionWebsiteMode = Whitelist.WebsiteMode.Mode;
        ActiveProjectName = SelectedProject.Name;
        StatusMessage = $"{SelectedRestrictionMode.Name} · 可以随时结束";
        Media.BeginSession();
        Refresh();
    }

    private void AddProject()
    {
        if (string.IsNullOrWhiteSpace(NewProjectName)) { StatusMessage = "请填写项目名称。"; return; }
        if (SelectedFolder is null) { StatusMessage = "请先选择或新建一个文件夹。"; return; }
        var project = new FocusProject(Guid.NewGuid(), NewProjectName.Trim(), SelectedFolder.Name, "#9683BE");
        try
        {
            _store.SaveProject(project);
            Projects.Add(project);
            SelectedFolder.Add(project);
            FolderProjects.Add(project);
            if (CanConfigure) SelectedProject = project;
            NewProjectName = "";
            StatusMessage = "项目已保存。";
            Notify(nameof(FolderSummary));
            Notify(nameof(IsFolderEmpty));
        }
        catch (Exception ex) { StatusMessage = $"项目保存失败：{ex.Message}"; }
    }

    private void AddFolder()
    {
        var name = NewFolderName.Trim();
        if (name.Length == 0) { StatusMessage = "请填写文件夹名称。"; return; }
        var existing = Folders.FirstOrDefault(f => f.Name == name);
        if (existing is not null)
        {
            SelectedFolder = existing;
            StatusMessage = "已打开同名文件夹。";
            return;
        }
        try
        {
            _store.SaveFolder(name);
            var folder = new ProjectFolder(name);
            Folders.Add(folder);
            SelectedFolder = folder;
            NewFolderName = "";
            StatusMessage = "文件夹已创建，可以添加项目。";
        }
        catch (Exception ex) { StatusMessage = $"文件夹保存失败：{ex.Message}"; }
    }

    private void SaveSettings()
    {
        if (!TryMinutes(PauseMinutes, false, out _) || !TryMinutes(RestMinutes, true, out _))
        {
            StatusMessage = "暂停分钟数须大于 0，休息分钟数可为 0。";
            return;
        }
        try
        {
            _store.SaveSetting("pauseMinutes", PauseMinutes);
            _store.SaveSetting("restMinutes", RestMinutes);
            _store.SaveSetting("timer.mode", DefaultTimerMode.Mode.ToString());
            _store.SaveSetting("timer.strict", DefaultRestrictionMode.Strict.ToString());
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
        try
        {
            _store.SaveRecord(_timer.LastRecord!);
            _savedRecordId = _timer.LastRecord!.Id;
            LoadRecords();
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
        var projects = Projects.ToDictionary(p => p.Id, p => p.Name);
        foreach (var record in _store.GetRecords())
            Records.Add(new RecordRow(projects.GetValueOrDefault(record.ProjectId, "未分类"),
                record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"),
                record.EndedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), Format(record.FocusDuration),
                record.Mode == TimerMode.CountUp ? "正计时" : "倒计时",
                record.EndReason switch { EndReason.Manual => "手动结束", EndReason.CountdownElapsed => "到点结束", _ => "退出软件" },
                record.Strict ? "严格" : "普通"));
        Notify(nameof(RecordSummary));
    }

    private void Refresh()
    {
        if (!_timer.IsActive) Media.ReleaseSession();
        if (CanConfigure && !_wasConfigurable) ApplyDefaultModes();
        _wasConfigurable = CanConfigure;
        if (HasUnsavedRecord && _timer.LastRecord!.Id != _attemptedRecordId) PersistRecord();
        foreach (var name in new[] { nameof(TimerText), nameof(StateText), nameof(State), nameof(PhaseText), nameof(CanConfigure),
            nameof(ActiveProjectName), nameof(FocusHeading), nameof(HasUnsavedRecord), nameof(ShowStart),
            nameof(ShowPause), nameof(ShowResume), nameof(ShowEnd), nameof(ShowSkipRest) }) Notify(name);
        foreach (var name in new[] { nameof(IsFocusActive), nameof(RestrictionsActive), nameof(CanEditWhitelist), nameof(SessionModeText), nameof(RestrictionStatus), nameof(BrowserWebsites), nameof(WebsiteBlockedMessage) }) Notify(name);
        if (_lastRestrictionsActive != RestrictionsActive)
        {
            _lastRestrictionsActive = RestrictionsActive;
            RestrictionsChanged?.Invoke(this, EventArgs.Empty);
        }
        foreach (var command in new[] { StartCommand, PauseCommand, ResumeCommand, EndCommand, SkipRestCommand, RetrySaveCommand }) command.Refresh();
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
