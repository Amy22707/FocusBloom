using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class ReflectionViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly TimeProvider _clock;
    private DateTime _date;
    private DateOnly _lastToday;
    private IReadOnlyList<FocusRecord> _records = Array.Empty<FocusRecord>();
    private IReadOnlyList<TodoItem> _todos = Array.Empty<TodoItem>();
    private IReadOnlyList<Habit> _habits = Array.Empty<Habit>();
    private IReadOnlyList<HabitCheckIn> _checks = Array.Empty<HabitCheckIn>();
    private IReadOnlyList<FocusOutcome> _outcomes = Array.Empty<FocusOutcome>();
    private bool _summaryExpanded;
    public DateOnly Today => DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
    public DateTime Date { get => _date; set { if (_date == value.Date) return; _date = value.Date; Notify(); RefreshSelection(); } }
    public DateOnly Day => DateOnly.FromDateTime(Date);
    public DateOnly MonthFirst => new(Day.Year, Day.Month, 1);
    public IReadOnlyList<DailyReflection> Entries { get; private set; } = Array.Empty<DailyReflection>();
    public IReadOnlyList<DailyReflection> MonthEntries => Entries.Where(item => item.Date.Year == Day.Year && item.Date.Month == Day.Month).ToArray();
    public DailyReflection? SelectedEntry => Entries.FirstOrDefault(item => item.Date == Day);
    public ReflectionDaySummary Summary { get; private set; } = new(TimeSpan.Zero, 0, Array.Empty<TodoItem>(), 0, 0, Array.Empty<FocusOutcome>());
    public string DateLabel => $"{Day:yyyy年M月d日}";
    public string MonthLabel => $"{Day:yyyy年M月}";
    public bool CanEdit => Day <= Today;
    public string EditLabel => SelectedEntry is not null ? "编辑这一天" : Day == Today ? "记录今天" : "补记这一天";
    public string MoodTitle => SelectedEntry?.Mood is { } value ? ReflectionRules.Moods[value - 1].Emoji + " " + ReflectionRules.Moods[value - 1].Name : SelectedEntry is not null ? "这一天的复盘" : CanEdit ? "这一天，还没有留下记录" : "未来的心情，留到那天再写";
    public string Tags => SelectedEntry is { Tags.Length: > 0 } entry ? string.Join(" · ", entry.Tags) : "";
    public string Feeling => SelectedEntry?.Feeling ?? "";
    public string Highlights => SelectedEntry?.Highlights ?? "";
    public string NextStep => SelectedEntry?.NextStep ?? "";
    public string FocusText => StatisticsViewModel.Duration(Summary.Duration);
    public string SessionText => $"{Summary.Sessions} 次专注结束";
    public string TodoText => $"{Summary.CompletedTodos.Count} 项待办完成";
    public string HabitText => $"{Summary.CheckedHabits} / {Summary.PlannedHabits} 项习惯打卡";
    public IReadOnlyList<string> OutcomeLines => Summary.Outcomes.Select(item => string.Join(" · ", new[] { item.Summary, item.Quantity is { } quantity ? $"{quantity:0.##} {item.Unit}" : "", item.NextStep.Length > 0 ? "下一步：" + item.NextStep : "" }.Where(text => text.Length > 0))).ToArray();
    public string MonthSummary
    {
        get
        {
            var groups = MonthEntries.Where(item => item.Mood is not null).GroupBy(item => item.Mood!.Value).ToArray();
            return $"本月记录 {MonthEntries.Count} 天" + (groups.Length == 0 ? "" : " · 较多出现 " + string.Join("、", groups.Where(group => group.Count() == groups.Max(value => value.Count())).Select(group => ReflectionRules.Moods.Single(mood => mood.Value == group.Key).Name)));
        }
    }
    public ReflectionReminder Reminder { get; private set; }
    public string ReminderLabel => Reminder.Enabled ? $"提醒 {(Reminder.Time ?? new TimeOnly(21, 0)):HH:mm}" : "每日提醒 · 关闭";
    public bool SummaryExpanded { get => _summaryExpanded; set { if (_summaryExpanded == value) return; _summaryExpanded = value; _store.SaveSetting("reflection.summary.expanded", value.ToString()); Notify(); } }
    public RelayCommand PreviousMonthCommand { get; }
    public RelayCommand NextMonthCommand { get; }
    public RelayCommand TodayCommand { get; }
    public event EventHandler? Changed;
    public event EventHandler? RedrawRequested;
    public ReflectionViewModel(IFocusStore store, TimeProvider? clock = null)
    {
        _store = store; _clock = clock ?? TimeProvider.System; _lastToday = Today; _date = Today.ToDateTime(TimeOnly.MinValue);
        Reminder = store.GetSetting("reflection.reminder") is { } json ? JsonSerializer.Deserialize<ReflectionReminder>(json)! : new(); _summaryExpanded = store.GetSetting("reflection.summary.expanded") == "True";
        PreviousMonthCommand = new(() => Date = Date.AddMonths(-1)); NextMonthCommand = new(() => Date = Date.AddMonths(1)); TodayCommand = new(() => Date = Today.ToDateTime(TimeOnly.MinValue)); Reload();
    }
    public void Reload()
    {
        if (Today != _lastToday && Day == _lastToday) { _date = Today.ToDateTime(TimeOnly.MinValue); Notify(nameof(Date)); } _lastToday = Today;
        Entries = _store.GetDailyReflections(); _records = _store.GetRecords(); _todos = _store.GetTodos(true); _habits = _store.GetHabits(true); _checks = _store.GetHabitCheckIns(); _outcomes = _store.GetOutcomes(); RefreshSelection();
    }
    private void RefreshSelection()
    {
        Summary = ReflectionRules.Summary(Day, _records, _todos, _habits, _checks, _outcomes);
        foreach (var name in new[] { nameof(DateLabel), nameof(MonthLabel), nameof(CanEdit), nameof(EditLabel), nameof(MoodTitle), nameof(Tags), nameof(Feeling), nameof(Highlights), nameof(NextStep), nameof(FocusText), nameof(SessionText), nameof(TodoText), nameof(HabitText), nameof(Summary), nameof(OutcomeLines), nameof(MonthSummary) }) Notify(name);
        RequestRedraw();
    }
    public void RequestRedraw() => RedrawRequested?.Invoke(this, EventArgs.Empty);
    public void Save(DailyReflection item) { _store.SaveDailyReflection(ReflectionRules.Normalize(item, Today)); Reload(); Changed?.Invoke(this, EventArgs.Empty); }
    public void Delete(DateOnly date) { _store.DeleteDailyReflection(date); Reload(); Changed?.Invoke(this, EventArgs.Empty); }
    public void SaveReminder(ReflectionReminder options) { Reminder = options; _store.SaveSetting("reflection.reminder", JsonSerializer.Serialize(options)); Notify(nameof(ReminderLabel)); Changed?.Invoke(this, EventArgs.Empty); }
}
