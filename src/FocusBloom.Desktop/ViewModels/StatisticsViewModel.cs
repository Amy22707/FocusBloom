using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record StatisticsChoice(string Name, Guid? Id = null, string? Folder = null);
public sealed record ReviewTask(Guid Id, string Title, string Folder, string Status, string Estimated, string Invested, string Difference, string NextStep);
public sealed record ReviewOutcome(Guid Id, string Project, string Todo, string Date, string Text, string NextStep);
public sealed record StatisticsMetric(string Name, string Value, string Hint) { public string Tooltip => Value + "\n" + Hint; }
public sealed record StatisticsShare(string Name, string Color, TimeSpan Duration, int Count, IReadOnlySet<Guid> Projects);

public sealed class StatisticsViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private IReadOnlyList<FocusRecord> _records = Array.Empty<FocusRecord>();
    private IReadOnlyList<FocusProject> _projects = Array.Empty<FocusProject>();
    private IReadOnlyList<TodoItem> _todos = Array.Empty<TodoItem>();
    private IReadOnlyList<TodoFolder> _folders = Array.Empty<TodoFolder>();
    private Dictionary<Guid, FocusOutcome> _outcomes = new();
    private bool _updatingChoices;
    private string _range;
    private DateTime _anchor = DateTime.Today;
    private DateTime _customFrom = DateTime.Today.AddDays(-6), _customTo = DateTime.Today;
    private StatisticsChoice? _folder, _project, _todo;
    private string _chartMetric = "时长", _shareMode = "项目";
    private int _heatmapYear = DateTime.Today.Year;
    private bool _reviewMode;
    public event EventHandler? Changed;
    public IReadOnlyList<string> Ranges { get; } = new[] { "日", "周", "月", "年", "自定义" };
    public IReadOnlyList<string> ChartMetrics { get; } = new[] { "时长", "次数" };
    public IReadOnlyList<string> ShareModes { get; } = new[] { "项目", "文件夹" };
    public ObservableCollection<StatisticsChoice> FolderChoices { get; } = new();
    public ObservableCollection<StatisticsChoice> ProjectChoices { get; } = new();
    public ObservableCollection<StatisticsChoice> TodoChoices { get; } = new();
    public IReadOnlyList<int> HeatmapYears { get; private set; } = Array.Empty<int>();
    public IReadOnlyList<StatisticsMetric> Metrics { get; private set; } = Array.Empty<StatisticsMetric>();
    public IReadOnlyList<StatisticsShare> Shares { get; private set; } = Array.Empty<StatisticsShare>();
    public IReadOnlyList<StatisticsBucket> Trend { get; private set; } = Array.Empty<StatisticsBucket>();
    public IReadOnlyList<StatisticsBucket> Hours { get; private set; } = Array.Empty<StatisticsBucket>();
    public IReadOnlyList<ReviewTask> Tasks { get; private set; } = Array.Empty<ReviewTask>();
    public IReadOnlyList<ReviewOutcome> Outcomes { get; private set; } = Array.Empty<ReviewOutcome>();
    public IReadOnlyList<GoalRow> GoalProgress { get; private set; } = Array.Empty<GoalRow>();
    public DateOnly GoalDate => Snapshot.Filter.To < DateOnly.FromDateTime(DateTime.Today) ? Snapshot.Filter.To : DateOnly.FromDateTime(DateTime.Today);
    public StatisticsSnapshot Snapshot { get; private set; } = FocusStatistics.Build(Array.Empty<FocusRecord>(), new(DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)));
    public StatisticsSnapshot Annual { get; private set; } = FocusStatistics.Build(Array.Empty<FocusRecord>(), new(new(DateTime.Today.Year, 1, 1), new(DateTime.Today.Year, 12, 31)));
    public string Range { get => _range; set { if (_range == value) return; _range = value; _store.SaveSetting("statistics.range", value); Notify(); Notify(nameof(IsCustom)); Notify(nameof(ShowAnchor)); Refresh(); } }
    public bool IsCustom => Range == "自定义";
    public bool ShowAnchor => !IsCustom;
    public DateTime Anchor { get => _anchor; set { _anchor = value.Date; Notify(); _heatmapYear = value.Year; Refresh(); } }
    public DateTime CustomFrom { get => _customFrom; set { _customFrom = value.Date; Notify(); Refresh(); } }
    public DateTime CustomTo { get => _customTo; set { _customTo = value.Date; _heatmapYear = value.Year; Notify(); Refresh(); } }
    public StatisticsChoice? SelectedFolder { get => _folder; set { if (_folder == value) return; _folder = value; Notify(); UpdateChoices(); Refresh(); } }
    public StatisticsChoice? SelectedProject { get => _project; set { if (_project == value) return; _project = value; Notify(); UpdateChoices(); Refresh(); } }
    public StatisticsChoice? SelectedTodo { get => _todo; set { if (_todo == value) return; _todo = value; Notify(); Refresh(); } }
    public string ChartMetric { get => _chartMetric; set { _chartMetric = value; _store.SaveSetting("statistics.metric", value); Notify(); Changed?.Invoke(this, EventArgs.Empty); } }
    public string ShareMode { get => _shareMode; set { _shareMode = value; _store.SaveSetting("statistics.share", value); Notify(); Refresh(); } }
    public int HeatmapYear { get => _heatmapYear; set { if (value < 1 || _heatmapYear == value) return; _heatmapYear = value; Notify(); Refresh(); } }
    public bool ReviewMode { get => _reviewMode; set { if (_reviewMode == value) return; _reviewMode = value; Notify(); Changed?.Invoke(this, EventArgs.Empty); } }
    public string RangeLabel => $"{Snapshot.Filter.From:yyyy-MM-dd} — {Snapshot.Filter.To:yyyy-MM-dd}";
    public string ValidationText => IsCustom && CustomTo < CustomFrom ? "结束日期不能早于开始日期。" : "";
    public bool IsValid => ValidationText.Length == 0;
    public string SelectionLabel => string.Join(" · ", new[] { SelectedFolder?.Name, SelectedProject?.Name, SelectedTodo?.Name }.Where(name => name is not null));
    public string TaskSummary => $"关联待办当前状态：{Tasks.Count(task => task.Status == "已完成")} / {Tasks.Count} 已完成";
    public string QuantitySummary { get; private set; } = "";
    public string OutcomeSummary => $"本范围内结束的专注 · {Outcomes.Count} 条成果";
    public RelayCommand PreviousCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand TodayCommand { get; }
    public StatisticsViewModel(IFocusStore store)
    {
        _store = store; _range = store.GetSetting("statistics.range") ?? "周"; _chartMetric = store.GetSetting("statistics.metric") ?? "时长"; _shareMode = store.GetSetting("statistics.share") ?? "项目";
        PreviousCommand = new(() => Move(-1)); NextCommand = new(() => Move(1)); TodayCommand = new(() =>
        {
            if (!IsCustom) { Anchor = DateTime.Today; return; }
            _customFrom = _customTo = DateTime.Today; _heatmapYear = DateTime.Today.Year;
            Notify(nameof(CustomFrom)); Notify(nameof(CustomTo)); Refresh();
        });
        Reload();
    }
    public void Move(int direction)
    {
        if (IsCustom) { var days = (CustomTo - CustomFrom).Days + 1; _customFrom = _customFrom.AddDays(days * direction); _customTo = _customTo.AddDays(days * direction); Notify(nameof(CustomFrom)); Notify(nameof(CustomTo)); Refresh(); }
        else Anchor = Range switch { "年" => Anchor.AddYears(direction), "月" => Anchor.AddMonths(direction), "周" => Anchor.AddDays(7 * direction), _ => Anchor.AddDays(direction) };
    }
    public void Reload()
    {
        _records = _store.GetRecords(); _projects = _store.GetProjects(true); _todos = _store.GetTodos(true); _folders = _store.GetTodoFolders(); _outcomes = _store.GetOutcomes().ToDictionary(item => item.SessionId);
        var years = _records.Select(record => record.StartedAt.LocalDateTime.Year).Append(DateTime.Today.Year).Append(HeatmapYear);
        HeatmapYears = Enumerable.Range(years.Min(), years.Max() - years.Min() + 1).ToArray(); Notify(nameof(HeatmapYears));
        UpdateChoices(); Refresh();
    }
    public void RequestRedraw() => Changed?.Invoke(this, EventArgs.Empty);
    private IReadOnlySet<Guid>? ProjectFilter()
    {
        if (SelectedProject?.Id is { } id) return new HashSet<Guid> { id };
        if (SelectedFolder?.Folder is { } name) return _projects.Where(project => project.Category == name).Select(project => project.Id).ToHashSet();
        return null;
    }
    private void UpdateChoices()
    {
        if (_updatingChoices) return; _updatingChoices = true;
        try
        {
            var folder = SelectedFolder?.Folder; var project = SelectedProject?.Id; var todo = SelectedTodo?.Id;
            FolderChoices.Clear(); FolderChoices.Add(new("全部文件夹")); foreach (var name in _projects.Select(item => item.Category).Distinct().Order()) FolderChoices.Add(new(name, Folder: name));
            _folder = FolderChoices.FirstOrDefault(item => item.Folder == folder) ?? FolderChoices[0];
            ProjectChoices.Clear(); ProjectChoices.Add(new("全部项目")); foreach (var item in _projects.Where(item => _folder.Folder is null || item.Category == _folder.Folder)) ProjectChoices.Add(new(_folder.Folder is null ? item.Category + " / " + item.Name : item.Name, item.Id));
            _project = ProjectChoices.FirstOrDefault(item => item.Id == project) ?? ProjectChoices[0];
            var projects = ProjectFilter(); var linked = _records.Where(record => projects is null || projects.Contains(record.ProjectId)).Select(record => record.TodoId).ToHashSet();
            TodoChoices.Clear(); TodoChoices.Add(new("全部待办")); foreach (var item in _todos.Where(item => projects is null || linked.Contains(item.Id) || item.PreferredProjectId is { } id && projects.Contains(id))) TodoChoices.Add(new((_folders.FirstOrDefault(folder => folder.Id == item.FolderId)?.Name ?? "历史文件夹") + " / " + item.Title, item.Id));
            _todo = TodoChoices.FirstOrDefault(item => item.Id == todo) ?? TodoChoices[0];
            Notify(nameof(SelectedFolder)); Notify(nameof(SelectedProject)); Notify(nameof(SelectedTodo));
        }
        finally { _updatingChoices = false; }
    }
    private (DateOnly From, DateOnly To) Dates()
    {
        var day = DateOnly.FromDateTime(Anchor);
        return Range switch { "日" => (day, day), "周" => (CalendarRules.Monday(day), CalendarRules.Monday(day).AddDays(6)), "月" => (new(day.Year, day.Month, 1), new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month))), "年" => (new(day.Year, 1, 1), new(day.Year, 12, 31)), _ => (DateOnly.FromDateTime(CustomFrom), DateOnly.FromDateTime(CustomTo)) };
    }
    public void Refresh()
    {
        if (_updatingChoices) return;
        if (!HeatmapYears.Contains(HeatmapYear)) { var years = HeatmapYears.Append(HeatmapYear); HeatmapYears = Enumerable.Range(years.Min(), years.Max() - years.Min() + 1).ToArray(); Notify(nameof(HeatmapYears)); }
        var dates = Dates(); Snapshot = FocusStatistics.Build(_records, new(dates.From, dates.To, ProjectFilter(), SelectedTodo?.Id));
        Annual = FocusStatistics.Build(_records, new(new(HeatmapYear, 1, 1), new(HeatmapYear, 12, 31), ProjectFilter(), SelectedTodo?.Id));
        var goalProjects = ProjectFilter(); var habits = _store.GetHabits(true).ToDictionary(item => item.Id); var checks = _store.GetHabitCheckIns();
        Guid? GoalProject(FocusGoal goal) => goal.ProjectId ?? (goal.HabitId is { } habit ? habits.GetValueOrDefault(habit)?.ProjectId : null);
        GoalProgress = _store.GetGoals().Where(goal => goal.Start <= dates.To && (goal.Until is null || goal.Until >= dates.From) && (goalProjects is null || GoalProject(goal) is not { } id || goalProjects.Contains(id)))
            .Select(goal => new GoalRow(GoalRules.Progress(goal, _records, checks, GoalDate), goal.Metric == GoalMetric.CheckInDays ? "习惯 · " + habits.GetValueOrDefault(goal.HabitId ?? Guid.Empty)?.Name : goal.ProjectId is { } id ? ProjectName(id) : "全部项目")).ToArray();
        Metrics = new[] { new StatisticsMetric("有效专注", Duration(Snapshot.Duration), "排除暂停和休息，跨天按实际日期分配"), new StatisticsMetric("专注次数", Snapshot.Count.ToString(), "按会话结束日期计次，番茄每轮计一次"), new StatisticsMetric("平均每次", Duration(Snapshot.Average), "范围内结束记录的完整有效时长平均值"), new StatisticsMetric("活跃天数", Snapshot.ActiveDays + " 天", "有实际专注投入的日期数量") };
        Trend = FocusStatistics.Trend(Snapshot, Range == "日", Range == "年"); Hours = FocusStatistics.Hours(Snapshot);
        var projectNames = _projects.ToDictionary(item => item.Id); var colors = new[] { "#7B97A6", "#879F8C", "#AB87A0", "#A99BC3", "#C3A97B", "#60A5A5", "#6173A7", "#CC8E67", "#789D59", "#B46B79", "#908481", "#6BA7C8" };
        Shares = ShareMode == "文件夹" ? Snapshot.Projects.GroupBy(item => projectNames.GetValueOrDefault(item.ProjectId)?.Category ?? "已删除项目").OrderByDescending(group => group.Sum(item => item.Duration.Ticks))
            .Select((group, index) => new StatisticsShare(group.Key, colors[index % colors.Length], TimeSpan.FromTicks(group.Sum(item => item.Duration.Ticks)), group.Sum(item => item.Count), group.Select(item => item.ProjectId).ToHashSet())).ToArray()
            : Snapshot.Projects.Select((item, index) => new StatisticsShare(projectNames.GetValueOrDefault(item.ProjectId)?.Name ?? "已删除项目", colors[index % colors.Length], item.Duration, item.Count, new HashSet<Guid> { item.ProjectId })).ToArray();
        BuildReview();
        foreach (var name in new[] { nameof(Metrics), nameof(RangeLabel), nameof(ValidationText), nameof(IsValid), nameof(SelectionLabel), nameof(HeatmapYear), nameof(TaskSummary), nameof(QuantitySummary), nameof(OutcomeSummary) }) Notify(name);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private void BuildReview()
    {
        var projects = ProjectFilter(); var linked = Snapshot.Sessions.Select(item => item.Record.TodoId).ToHashSet(); var activeTodos = _store.GetTodos().Select(item => item.Id).ToHashSet();
        var folderNames = _folders.ToDictionary(item => item.Id, item => item.Name);
        var invested = _records.Where(record => projects is null || projects.Contains(record.ProjectId)).Where(record => record.TodoId is not null).GroupBy(record => record.TodoId!.Value).ToDictionary(group => group.Key, group => TimeSpan.FromTicks(group.Sum(record => record.FocusDuration.Ticks)));
        Tasks = _todos.Where(todo => (SelectedTodo?.Id is null || todo.Id == SelectedTodo.Id) && (linked.Contains(todo.Id) || todo.Id == SelectedTodo?.Id || projects is not null && todo.PreferredProjectId is { } id && projects.Contains(id)))
            .OrderBy(todo => todo.Status == TodoStatus.Completed).ThenBy(todo => todo.DueDate).Select(todo => {
                var actual = invested.GetValueOrDefault(todo.Id); var difference = todo.EstimatedMinutes is { } estimate ? actual - TimeSpan.FromMinutes(estimate) : (TimeSpan?)null;
                return new ReviewTask(todo.Id, todo.Title, folderNames.GetValueOrDefault(todo.FolderId, "历史文件夹"), !activeTodos.Contains(todo.Id) ? "已删除" : todo.Status switch { TodoStatus.Completed => "已完成", TodoStatus.InProgress => "进行中", _ => "待办" },
                    todo.EstimatedMinutes is { } minutes ? Duration(TimeSpan.FromMinutes(minutes)) : "未设置", Duration(actual), difference is { } diff ? (diff >= TimeSpan.Zero ? "比预估多 " : "比预估少 ") + Duration(diff.Duration()) : "", todo.ResumeNote);
            }).ToArray();
        Outcomes = Snapshot.Sessions.Where(item => item.EndedInRange && _outcomes.TryGetValue(item.Record.Id, out var outcome) && (outcome.Summary.Length > 0 || outcome.Quantity is not null || outcome.NextStep.Length > 0))
            .Select(item => { var outcome = _outcomes[item.Record.Id]; return new ReviewOutcome(item.Record.Id, ProjectName(item.Record.ProjectId), TodoName(item.Record.TodoId), item.Record.EndedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm"), OutcomeText(outcome), outcome.NextStep); }).ToArray();
        var quantities = Snapshot.Sessions.Where(item => item.EndedInRange).Select(item => _outcomes.GetValueOrDefault(item.Record.Id)).Where(outcome => outcome?.Quantity is not null).GroupBy(outcome => outcome!.Unit.Trim());
        QuantitySummary = string.Join(" · ", quantities.Select(group => $"{group.Sum(item => item!.Quantity!.Value):0.##} {group.Key}"));
    }
    public string ProjectName(Guid id) => _projects.FirstOrDefault(item => item.Id == id)?.Name ?? "已删除项目";
    public string TodoName(Guid? id) => id is { } todo ? _todos.FirstOrDefault(item => item.Id == todo)?.Title ?? "已删除待办" : "";
    public string OutcomeText(Guid recordId) => _outcomes.TryGetValue(recordId, out var outcome) ? OutcomeText(outcome) : "";
    private static string OutcomeText(FocusOutcome outcome) => string.Join(" · ", new[] { outcome.Summary, outcome.Quantity is { } quantity ? $"{quantity:0.##} {outcome.Unit}" : "" }.Where(text => text.Length > 0));
    public StatisticsSnapshot Drill(DateOnly from, DateOnly to, IReadOnlySet<Guid>? projects = null, int? hour = null) => FocusStatistics.Build(_records, new(from, to, projects ?? ProjectFilter(), SelectedTodo?.Id, hour));
    public static string Duration(TimeSpan duration) => duration.TotalHours >= 1 ? $"{(int)duration.TotalHours}小时{duration.Minutes}分钟" : duration.TotalMinutes >= 1 ? $"{(int)duration.TotalMinutes}分钟" : $"{duration.TotalSeconds:0}秒";
    public string Export(bool csv)
    {
        if (csv)
        {
            string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            var text = new StringBuilder("项目,待办,开始,结束,本范围有效秒数,完整有效秒数,成果,下一步\r\n");
            foreach (var item in Snapshot.Sessions) text.AppendLine(string.Join(",", new[] { ProjectName(item.Record.ProjectId), TodoName(item.Record.TodoId), item.Record.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), item.Record.EndedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), item.IncludedDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture), item.Record.FocusDuration.TotalSeconds.ToString(CultureInfo.InvariantCulture), OutcomeText(item.Record.Id), _outcomes.GetValueOrDefault(item.Record.Id)?.NextStep ?? "" }.Select(Cell)));
            return text.ToString();
        }
        var result = new StringBuilder($"# 专注复盘\n\n{RangeLabel}\n\n{SelectionLabel}\n\n");
        foreach (var item in Metrics) result.AppendLine($"- {item.Name}：{item.Value}");
        result.AppendLine("\n次数按会话结束日计，时长按实际日期分配，平均按完整会话计算。\n\n## 投入分布\n");
        foreach (var item in Shares) result.AppendLine($"- {item.Name}：{Duration(item.Duration)}，{item.Count}次");
        if (GoalProgress.Count > 0) { result.AppendLine($"\n## 目标进度\n\n截至{GoalDate:yyyy-MM-dd}，按目标自身项目与周期累计。\n"); foreach (var goal in GoalProgress) result.AppendLine($"- {goal.Name}（{goal.Scope}）：{goal.ValueText} · {goal.State}"); }
        result.AppendLine("\n## 关联待办\n\n" + TaskSummary + "\n");
        foreach (var item in Tasks) result.AppendLine($"- {item.Title}（{item.Folder}）：{item.Status}，累计投入{item.Invested}，预估{item.Estimated}；{item.Difference}" + (item.NextStep.Length > 0 ? $"\n  下一步：{item.NextStep}" : ""));
        result.AppendLine("\n## 成果\n\n" + QuantitySummary + "\n");
        foreach (var item in Outcomes) result.AppendLine($"- {item.Date} · {item.Project}" + (item.Todo.Length > 0 ? " / " + item.Todo : "") + $"：{item.Text}" + (item.NextStep.Length > 0 ? $"\n  下一步：{item.NextStep}" : ""));
        return result.ToString();
    }
}
