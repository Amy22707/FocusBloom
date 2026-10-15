using System;
using System.Linq;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;

namespace FocusBloom.Desktop.Windows;

public sealed class CalendarPlanWindow : CalendarFormWindow
{
    private sealed record Link(Guid? Id, string Name);
    private readonly CalendarPlan? _item;
    private readonly bool _focusOnly;
    private readonly TextBox _title, _start, _minutes, _notes;
    private readonly DatePicker _date;
    private readonly ComboBox _todo, _project;
    private readonly ReminderSettingsControl _reminder;
    public CalendarPlanWindow(MainViewModel main, DateOnly date, CalendarPlan? item = null, Guid? todoId = null, bool focusOnly = false) : base(main, item is null ? "安排时间" : "编辑安排", item is not null)
    {
        _item = item; _focusOnly = focusOnly; todoId ??= item?.TodoId;
        _title = AddTextInput("TitleInput", "安排名称", item?.Title ?? main.Todos.Items.FirstOrDefault(row => row.Id == todoId)?.Title ?? "");
        _date = DateInput("DateInput", "计划日期", item?.Date ?? date);
        _start = AddTextInput("StartInput", "开始 / HH:mm", item?.Start.ToString("HH:mm") ?? "09:00");
        _minutes = AddTextInput("MinutesInput", "预计持续 / 分钟", (item?.Minutes ?? 30).ToString());
        var todos = new[] { new Link(null, "不关联待办") }.Concat(main.Todos.Items.Select(row => new Link(row.Id, row.Title))).ToArray();
        _todo = Choice("TodoSelector", "关联待办（可选）", todos, todos.FirstOrDefault(link => link.Id == todoId), "Name");
        if (focusOnly) { _todo.Visibility = System.Windows.Visibility.Collapsed; Fields.Children[Fields.Children.Count - 2].Visibility = System.Windows.Visibility.Collapsed; }
        var projects = new[] { new Link(null, "不关联项目") }.Concat(main.Projects.Select(project => new Link(project.Id, $"{project.Category} / {project.Name}"))).ToArray();
        _project = Choice("ProjectSelector", "关联计时项目（可选）", projects, projects.FirstOrDefault(link => link.Id == item?.ProjectId), "Name");
        _notes = AddTextInput("NotesInput", "备注", item?.Notes ?? "", true);
        Caption("开始提醒"); _reminder = new(item?.Reminder ?? new(false)); Fields.Children.Add(_reminder);
        _date.SelectedDateChanged += (_, _) => Preview(); _start.TextChanged += (_, _) => Preview();
        if (item is not null) { var links = FocusLinks(() => ((Link)_todo.SelectedItem).Id, () => ((Link)_project.SelectedItem).Id); _todo.SelectionChanged += (_, _) => links(); _project.SelectionChanged += (_, _) => links(); }
        Preview(); Finish();
    }
    private void Preview() => _reminder.SetOccurrence(_date.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null, ParseTime(_start.Text, out var time) ? time : null);
    protected override void Save()
    {
        if (_title.Text.Trim().Length == 0 || _date.SelectedDate is null || !ParseTime(_start.Text, out var start) || start is null || !int.TryParse(_minutes.Text, out var minutes) || minutes < 1 || start.Value.ToTimeSpan().TotalMinutes + minutes > 1440)
        { Message.Text = "请填写名称、日期、HH:mm开始时间及当天内的持续分钟数。"; return; }
        if (_focusOnly && ((Link)_project.SelectedItem).Id is null) { Message.Text = "请选择这次安排的专注项目。"; return; }
        if (!_focusOnly && ((Link)_project.SelectedItem).Id is null && ((Link)_todo.SelectedItem).Id is null) { Message.Text = "请至少关联一个待办或计时项目；未关联的安排不会显示在日历或专注记录中。"; return; }
        if (!_reminder.TryGetOptions(out var reminder)) { Message.Text = "请填写有效的提醒设置。"; return; }
        var item = new CalendarPlan(_item?.Id ?? Guid.NewGuid(), _title.Text.Trim(), DateOnly.FromDateTime(_date.SelectedDate.Value), start.Value, minutes,
            ((Link)_todo.SelectedItem).Id, ((Link)_project.SelectedItem).Id, _notes.Text.Trim(), reminder);
        Commit(() => Main.Calendar.Store.SaveCalendarPlan(item));
    }
    protected override void Delete()
    { if (_item is not null && ConfirmDeletionWindow.Ask(this, $"删除安排‘{_item.Title}’？")) Commit(() => Main.Calendar.Store.DeleteCalendarPlan(_item.Id)); }
}
