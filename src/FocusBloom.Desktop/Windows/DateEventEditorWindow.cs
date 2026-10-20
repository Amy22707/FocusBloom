using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Windows;

public sealed class DateEventEditorWindow : CalendarFormWindow
{
    private sealed record Link(Guid? Id, string Name);
    private readonly DateEvent? _item;
    private readonly TextBox _title, _time, _notes, _cover;
    private readonly DatePicker _date, _until;
    private readonly ComboBox _mode, _repeat, _project, _todo, _group;
    private readonly CheckBox _pinned;
    private readonly ReminderSettingsControl _reminder;
    public DateEventEditorWindow(MainViewModel main, DateEvent? item = null, string? initialGroup = null) : base(main, item is null ? "新建日期事项" : "编辑日期事项", item is not null)
    {
        _item = item;
        _title = AddTextInput("TitleInput", "名称", item?.Title ?? "");
        var group = item?.Group ?? initialGroup ?? main.DateEvents.SelectedGroup?.Name ?? "未分组";
        _group = Choice("DateGroupSelector", "分组", main.DateEvents.GroupNames, group);
        _mode = Choice("ModeSelector", "计数方式", new[] { "倒数日", "正数日" }, item?.Mode == DateCountMode.CountUp ? "正数日" : "倒数日");
        _date = DateInput("DateInput", "日期", item?.Date ?? DateOnly.FromDateTime(DateTime.Today));
        _time = AddTextInput("TimeInput", "具体时间 / HH:mm（可选）", item?.Time?.ToString("HH:mm") ?? "");
        _repeat = Choice("RepeatSelector", "重复", new[] { "不重复", "每日", "每月", "每年" }, new[] { "不重复", "每日", "每月", "每年" }[(int)(item?.Repeat ?? DateRepeat.None)]);
        _until = DateInput("RepeatUntilInput", "重复结束日期（留空则持续重复）", item?.RepeatUntil);
        _notes = AddTextInput("NotesInput", "备注", item?.Notes ?? "", true);
        _pinned = Check("PinnedInput", "置顶", item?.Pinned ?? false);
        _cover = AddTextInput("CoverInput", "封面图片（可选）", item?.CoverPath ?? ""); _cover.IsReadOnly = true;
        var images = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var browse = new Button { Content = "选择图片", Padding = new Thickness(10, 5, 10, 5) }; browse.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "图片|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif" }; if (dialog.ShowDialog(this) == true) _cover.Text = dialog.FileName; }; images.Children.Add(browse);
        var clear = new Button { Content = "清除封面", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) }; clear.Click += (_, _) => _cover.Text = ""; images.Children.Add(clear); Fields.Children.Add(images);
        var projects = new[] { new Link(null, "不关联项目") }.Concat(main.Projects.Select(project => new Link(project.Id, $"{project.Category} / {project.Name}"))).ToArray();
        _project = Choice("ProjectSelector", "关联计时项目（可选）", projects, projects.FirstOrDefault(link => link.Id == item?.ProjectId), "Name");
        var todos = new[] { new Link(null, "不关联待办") }.Concat(main.Todos.Items.Select(row => new Link(row.Id, row.Title))).ToArray();
        _todo = Choice("TodoSelector", "关联待办（可选）", todos, todos.FirstOrDefault(link => link.Id == item?.TodoId), "Name");
        Caption("提醒"); _reminder = new(item?.Reminder ?? new()); Fields.Children.Add(_reminder);
        _date.SelectedDateChanged += (_, _) => Preview(); _time.TextChanged += (_, _) => Preview(); _mode.SelectionChanged += (_, _) => Preview(); _repeat.SelectionChanged += (_, _) => Preview(); _until.SelectedDateChanged += (_, _) => Preview();
        if (item is not null)
        {
            var links = FocusLinks(() => ((Link)_todo.SelectedItem).Id, () => ((Link)_project.SelectedItem).Id); _todo.SelectionChanged += (_, _) => links(); _project.SelectionChanged += (_, _) => links();
            var export = new Button { Content = "导出已保存事项为ICS", Margin = new Thickness(0, 12, 0, 0) };
            export.Click += (_, _) => { var dialog = new SaveFileDialog { Filter = "iCalendar|*.ics", FileName = $"FocusBloom-{item.Date:yyyyMMdd}.ics" }; if (dialog.ShowDialog(this) == true) { try { File.WriteAllText(dialog.FileName, IcsExporter.DateEvent(item), new System.Text.UTF8Encoding(false)); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Message.Text = "导出失败：" + ex.Message; } } }; Fields.Children.Add(export);
        }
        Preview(); Finish();
    }
    private void Preview()
    {
        if (_date.SelectedDate is not { } date || !ParseTime(_time.Text, out var time)) { _reminder.SetOccurrence(null, null); return; }
        var item = new DateEvent(Guid.Empty, "", DateOnly.FromDateTime(date), time, (DateCountMode)_mode.SelectedIndex, (DateRepeat)_repeat.SelectedIndex, _until.SelectedDate is { } until ? DateOnly.FromDateTime(until) : null);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var next = CalendarRules.NextDate(item, today);
        if (item.Mode == DateCountMode.CountUp && item.Repeat == DateRepeat.None && item.Date < today) next = null;
        _reminder.SetOccurrence(next, time);
    }
    protected override void Save()
    {
        if (_title.Text.Trim().Length == 0 || _date.SelectedDate is null || !ParseTime(_time.Text, out var time)) { Message.Text = "请填写名称、日期及有效的HH:mm时间。"; return; }
        var date = DateOnly.FromDateTime(_date.SelectedDate.Value); var until = _repeat.SelectedIndex == 0 ? null : _until.SelectedDate is { } end ? (DateOnly?)DateOnly.FromDateTime(end) : null;
        if (until < date) { Message.Text = "重复结束日期不能早于起始日期。"; return; }
        if (!_reminder.TryGetOptions(out var reminder)) { Message.Text = "请填写有效的提醒设置。"; return; }
        var item = new DateEvent(_item?.Id ?? Guid.NewGuid(), _title.Text.Trim(), date, time, (DateCountMode)_mode.SelectedIndex,
            (DateRepeat)_repeat.SelectedIndex, until, _notes.Text.Trim(), string.IsNullOrWhiteSpace(_cover.Text) ? null : _cover.Text,
            _pinned.IsChecked == true, ((Link)_project.SelectedItem).Id, ((Link)_todo.SelectedItem).Id, reminder, (string)_group.SelectedItem);
        Commit(() => Main.Calendar.Store.SaveDateEvent(item));
    }
    protected override void Delete()
    { if (_item is not null && ConfirmDeletionWindow.Ask(this, $"删除日期事项‘{_item.Title}’？")) Commit(() => Main.Calendar.Store.DeleteDateEvent(_item.Id)); }
}
