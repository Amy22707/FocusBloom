using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class HabitEditorWindow : CalendarFormWindow
{
    private sealed record Link(Guid? Id, string Name);
    private readonly Habit? _item;
    private readonly TextBox _name, _time;
    private readonly ComboBox _frequency, _project;
    private readonly CheckBox[] _days;
    private readonly CheckBox _hasUntil, _reminder;
    private readonly DatePicker _start, _until;
    public HabitEditorWindow(MainViewModel main, Habit? item = null) : base(main, item is null ? "新建习惯" : "编辑习惯", item is not null)
    {
        _item = item; _name = AddTextInput("NameInput", "习惯名称", item?.Name ?? "");
        _frequency = Choice("FrequencySelector", "执行频率", new[] { "每天", "工作日", "指定星期" }); _frequency.SelectedIndex = item is null || item.Weekdays.Count == 7 ? 0 : item.Weekdays.SequenceEqual(new[] { 1, 2, 3, 4, 5 }) ? 1 : 2;
        var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        _days = Enumerable.Range(1, 7).Select(day => new CheckBox { Content = "周" + "一二三四五六日"[day - 1], IsChecked = item?.Weekdays.Contains(day) ?? true, Margin = new Thickness(0, 0, 12, 8), Tag = day }).ToArray(); foreach (var day in _days) row.Children.Add(day); Fields.Children.Add(row); RegisterName("WeekdayOptions", row);
        void FrequencyChanged() => row.Visibility = _frequency.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _frequency.SelectionChanged += (_, _) => FrequencyChanged(); FrequencyChanged();
        _start = DateInput("StartDateInput", "开始日期", item?.Start ?? main.Goals.Today);
        _hasUntil = Check("HasUntilInput", "设置结束日期", item?.Until is not null); _until = DateInput("UntilDateInput", "结束日期", item?.Until ?? main.Goals.Today.AddDays(90)); _until.IsEnabled = _hasUntil.IsChecked == true;
        _hasUntil.Checked += (_, _) => _until.IsEnabled = true; _hasUntil.Unchecked += (_, _) => _until.IsEnabled = false;
        var projects = new[] { new Link(null, "不关联项目") }.Concat(main.Projects.Select(project => new Link(project.Id, project.Category + " / " + project.Name))).ToArray();
        _project = Choice("ProjectSelector", "关联项目（可选）", projects, projects.FirstOrDefault(link => link.Id == item?.ProjectId), "Name");
        _reminder = Check("ReminderToggle", "开启执行日提醒", item?.Reminder ?? false); _time = AddTextInput("ReminderTimeInput", "提醒时间 / HH:mm", (item?.ReminderTime ?? new TimeOnly(20, 0)).ToString("HH:mm")); _time.IsEnabled = _reminder.IsChecked == true;
        _reminder.Checked += (_, _) => _time.IsEnabled = true; _reminder.Unchecked += (_, _) => _time.IsEnabled = false;
        Caption("习惯提醒受软件提醒总开关控制。打卡限当天计划内，历史日历可查看。"); Finish();
    }
    protected override void Save()
    {
        var days = _frequency.SelectedIndex == 0 ? Enumerable.Range(1, 7).ToArray() : _frequency.SelectedIndex == 1 ? Enumerable.Range(1, 5).ToArray() : _days.Where(day => day.IsChecked == true).Select(day => (int)day.Tag).ToArray();
        if (_name.Text.Trim().Length == 0 || days.Length == 0) { Message.Text = "请填写习惯名称并选择至少一个执行日。"; return; }
        if (_start.SelectedDate is not { } start || _hasUntil.IsChecked == true && (_until.SelectedDate is null || _until.SelectedDate < start)) { Message.Text = "请填写有效的开始、结束日期。"; return; }
        if (_reminder.IsChecked == true && !TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) { Message.Text = "提醒时间格式为HH:mm。"; return; }
        var time = TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : new TimeOnly(20, 0);
        Commit(() => Main.Goals.SaveHabit(new(_item?.Id ?? Guid.NewGuid(), _name.Text.Trim(), days, DateOnly.FromDateTime(start), _hasUntil.IsChecked == true ? DateOnly.FromDateTime(_until.SelectedDate!.Value) : null, _reminder.IsChecked == true, time, ((Link)_project.SelectedItem).Id)));
    }
    protected override void Delete() { if (_item is not null && ConfirmDeletionWindow.Ask(this, $"删除习惯‘{_item.Name}’？将停止提醒和打卡，已有打卡记录及关联目标的历史进度保留。")) Commit(() => Main.Goals.DeleteHabit(_item.Id)); }
}
