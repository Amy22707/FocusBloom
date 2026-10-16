using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class CalendarViewModel(IFocusStore store, bool focusOnly = false, string? settingsPrefix = null) : ObservableObject
{
    private DateOnly _date = DateOnly.FromDateTime(DateTime.Today);
    private string _mode = store.GetSetting((settingsPrefix ?? (focusOnly ? "focus.calendar" : "calendar")) + ".view") is { } saved && (!focusOnly || saved != "课程表") ? saved : focusOnly ? "时间轴" : "月";
    private string _filter = "全部";
    private string _dayLayout = store.GetSetting((settingsPrefix ?? (focusOnly ? "focus.calendar" : "calendar")) + ".dayLayout") ?? "双列";
    public IReadOnlyList<string> DayLayouts { get; } = new[] { "单列", "双列" };
    public string DayLayout { get => _dayLayout; set { if (_dayLayout == value) return; _dayLayout = value; store.SaveSetting((settingsPrefix ?? (focusOnly ? "focus.calendar" : "calendar")) + ".dayLayout", value); Notify(); Changed?.Invoke(this, EventArgs.Empty); } }
    public bool ShowDayLayout => Mode == "日";
    public IFocusStore Store => store;
    public bool FocusOnly => focusOnly;
    public bool TasksCalendar => !focusOnly;
    public string Title => focusOnly ? "专注记录" : "日历与课表";
    public event EventHandler? Changed;
    public IReadOnlyList<string> Modes { get; } = focusOnly ? new[] { "时间轴", "日", "周", "月", "年" } : new[] { "日", "周", "月", "年", "课程表" };
    public IReadOnlyList<string> Filters { get; } = focusOnly ? new[] { "全部", "专注", "安排" } : new[] { "全部", "待办", "日期事项" };
    public IReadOnlyList<int> Years { get; } = Enumerable.Range(DateTime.Today.Year - 100, 201).ToArray();
    public IReadOnlyList<int> Months { get; } = Enumerable.Range(1, 12).ToArray();
    public int SelectedYear { get => Date.Year; set { if (value > 0 && value != Date.Year) Date = new(value, Date.Month, Math.Min(Date.Day, DateTime.DaysInMonth(value, Date.Month))); } }
    public int SelectedMonth { get => Date.Month; set { if (value > 0 && value != Date.Month) Date = new(Date.Year, value, Math.Min(Date.Day, DateTime.DaysInMonth(Date.Year, value))); } }
    public DateOnly Date { get => _date; set { _date = value; Notify(); Notify(nameof(SelectedYear)); Notify(nameof(SelectedMonth)); Notify(nameof(Heading)); Changed?.Invoke(this, EventArgs.Empty); } }
    public string Mode { get => _mode; set { _mode = value; if (value == "课程表") { _filter = "全部"; Notify(nameof(Filter)); } store.SaveSetting((settingsPrefix ?? (focusOnly ? "focus.calendar" : "calendar")) + ".view", value); Notify(); Notify(nameof(Heading)); Notify(nameof(ShowDayLayout)); Changed?.Invoke(this, EventArgs.Empty); } }
    public string Filter { get => _filter; set { _filter = value; Notify(); Changed?.Invoke(this, EventArgs.Empty); } }
    public string Heading => Mode switch
    {
        "日" or "时间轴" => $"{Date:yyyy年M月d日}", "月" => $"{Date:yyyy年M月}", "年" => $"{Date.Year}年",
        _ => $"{CalendarRules.Monday(Date):M月d日} — {CalendarRules.Monday(Date).AddDays(6):M月d日}" + (Mode == "课程表" ? TeachingWeekLabel() : "")
    };
    private string TeachingWeekLabel()
    { var term = store.GetSchoolTerm(); var week = CalendarRules.TeachingWeek(term, Date); return week >= 1 && week <= term.Weeks ? $" · 第{week}教学周" : " · 非教学周"; }
    public void Move(int direction) => Date = Mode switch { "日" or "时间轴" => Date.AddDays(direction), "月" => Date.AddMonths(direction), "年" => Date.AddYears(direction), _ => Date.AddDays(direction * 7) };
    public void Reload() { Notify(nameof(Heading)); Changed?.Invoke(this, EventArgs.Empty); }
    public IReadOnlyList<CalendarEntry> Entries(DateOnly from, DateOnly to)
    {
        var result = new List<CalendarEntry>(); var todos = store.GetTodos(true).ToDictionary(item => item.Id);
        foreach (var item in store.GetTodos().Where(item => !focusOnly && item.DueDate >= from && item.DueDate <= to))
            result.Add(new("待办", item.Id, item.Title, item.DueDate!.Value, item.DueTime, Detail: "截止" + (item.Status == FocusBloom.Core.Todos.TodoStatus.Completed ? " · 已完成" : ""), TodoId: item.Id, Completed: item.Status == FocusBloom.Core.Todos.TodoStatus.Completed));
        foreach (var item in store.GetCalendarPlans().Where(item => item.Date >= from && item.Date <= to && (focusOnly ? item.ProjectId is not null : item.ProjectId is null && item.TodoId is not null)))
        {
            var late = item.TodoId is { } id && todos.TryGetValue(id, out var todo) && todo.DueDate is { } due
                && item.Date.ToDateTime(item.Start).AddMinutes(item.Minutes) > due.ToDateTime(todo.DueTime ?? TimeOnly.MaxValue);
            result.Add(new("安排", item.Id, item.Title, item.Date, item.Start, item.Start.AddMinutes(item.Minutes), item.Notes + (late ? " · 超过截止时间" : ""), TodoId: item.TodoId, ProjectId: item.ProjectId, EndMinutes: item.Start.ToTimeSpan().TotalMinutes + item.Minutes));
        }
        var projects = store.GetProjects(true).ToDictionary(item => item.Id);
        var outcomes = store.GetOutcomes().ToDictionary(item => item.SessionId);
        foreach (var record in focusOnly ? store.GetRecords() : Array.Empty<FocusRecord>())
        {
            foreach (var interval in record.Intervals.Where(interval => interval.Kind == IntervalKind.Focus))
            {
                var start = interval.StartedAt.LocalDateTime; var end = interval.EndedAt.LocalDateTime;
                var first = DateOnly.FromDateTime(start); var last = DateOnly.FromDateTime(end);
                for (var day = first < from ? from : first; day <= last && day <= to; day = day.AddDays(1))
                {
                    var a = start > day.ToDateTime(TimeOnly.MinValue) ? start : day.ToDateTime(TimeOnly.MinValue);
                    var b = end < day.AddDays(1).ToDateTime(TimeOnly.MinValue) ? end : day.AddDays(1).ToDateTime(TimeOnly.MinValue);
                    if (b <= a) continue;
                    result.Add(new("专注", record.Id, projects.GetValueOrDefault(record.ProjectId)?.Name ?? "已删除项目",
                        day, TimeOnly.FromDateTime(a), TimeOnly.FromDateTime(b), $"实际专注 {(b - a).TotalMinutes:0.#} 分钟" + (outcomes.TryGetValue(record.Id, out var outcome) && outcome.Summary.Length > 0 ? $" · {outcome.Summary}" : ""),
                        TodoId: record.TodoId, ProjectId: record.ProjectId, EndMinutes: (b - day.ToDateTime(TimeOnly.MinValue)).TotalMinutes));
                }
            }
        }
        if (!focusOnly && Mode == "课程表") result.AddRange(CalendarRules.CourseEntries(store.GetSchoolTerm(), store.GetCourses(), store.GetCourseChanges(), from, to));
        foreach (var item in focusOnly ? Array.Empty<DateEvent>() : store.GetDateEvents())
            foreach (var day in CalendarRules.Occurrences(item, from, to)) result.Add(new("日期事项", item.Id, item.Title, day, item.Time, Detail: item.Notes, TodoId: item.TodoId, ProjectId: item.ProjectId));
        return result.Where(item => (Mode != "课程表" || item.Kind == "课程") && (Filter == "全部" || item.Kind == Filter))
            .OrderBy(item => item.Date).ThenBy(item => item.Start).ToArray();
    }
}
