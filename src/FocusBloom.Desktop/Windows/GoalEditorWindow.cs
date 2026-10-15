using System;
using System.Globalization;
using System.Linq;
using System.Windows.Controls;
using FocusBloom.Core.Goals;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class GoalEditorWindow : CalendarFormWindow
{
    private sealed record Link(Guid? Id, string Name);
    private readonly FocusGoal? _item;
    private readonly TextBox _name, _target;
    private readonly ComboBox _metric, _unit, _cycle, _project, _habit;
    private readonly DatePicker _start, _until;
    private readonly CheckBox _hasUntil;
    public GoalEditorWindow(MainViewModel main, FocusGoal? item = null) : base(main, item is null ? "新建目标" : "编辑目标", item is not null)
    {
        _item = item; _name = AddTextInput("NameInput", "目标名称", item?.Name ?? "");
        _metric = Choice("MetricSelector", "累计方式", new[] { "专注时长", "专注次数", "习惯打卡天数" }); _metric.SelectedIndex = (int)(item?.Metric ?? GoalMetric.Minutes);
        _target = AddTextInput("TargetInput", "目标数量", (item is null ? 2 : item.Metric == GoalMetric.Minutes ? item.Target / 60 : item.Target).ToString(CultureInfo.CurrentCulture));
        _unit = Choice("UnitSelector", "时长单位", new[] { "小时", "分钟" });
        _cycle = Choice("CycleSelector", "目标周期", new[] { "每天", "每周（周一开始）", "每月", "累计目标" }); _cycle.SelectedIndex = (int)(item?.Cycle ?? GoalCycle.Daily);
        _start = DateInput("StartDateInput", "开始日期", item?.Start ?? main.Goals.Today);
        _hasUntil = Check("HasUntilInput", "设置结束日期", item?.Until is not null); _until = DateInput("UntilDateInput", "结束日期", item?.Until ?? main.Goals.Today.AddDays(30));
        _until.IsEnabled = _hasUntil.IsChecked == true; _hasUntil.Checked += (_, _) => _until.IsEnabled = true; _hasUntil.Unchecked += (_, _) => _until.IsEnabled = false;
        var projects = new[] { new Link(null, "全部项目") }.Concat(main.Projects.Select(project => new Link(project.Id, project.Category + " / " + project.Name))).ToArray();
        _project = Choice("ProjectSelector", "关联项目（时长与次数）", projects, projects.FirstOrDefault(link => link.Id == item?.ProjectId), "Name");
        var habits = new[] { new Link(null, "请选择习惯") }.Concat(main.Calendar.Store.GetHabits(true).Where(habit => !habit.Deleted || habit.Id == item?.HabitId).Select(habit => new Link(habit.Id, habit.Name + (habit.Deleted ? "（已删除）" : "")))).ToArray();
        _habit = Choice("HabitSelector", "关联习惯（打卡天数）", habits, habits.FirstOrDefault(link => link.Id == item?.HabitId), "Name");
        void MetricChanged() { _unit.IsEnabled = _metric.SelectedIndex == 0; _project.IsEnabled = _metric.SelectedIndex != 2; _habit.IsEnabled = _metric.SelectedIndex == 2; }
        _metric.SelectionChanged += (_, _) => { if (_metric.SelectedIndex == 2 && _cycle.SelectedIndex == 0) _cycle.SelectedIndex = 3; MetricChanged(); }; MetricChanged(); Finish();
    }
    protected override void Save()
    {
        if (_name.Text.Trim().Length == 0 || !double.TryParse(_target.Text, out var target) || !double.IsFinite(target) || target <= 0 || target > 10000000 || _metric.SelectedIndex != 0 && target != Math.Truncate(target)) { Message.Text = "请填写名称和有效目标数量，次数与天数使用正整数。"; return; }
        if (_start.SelectedDate is not { } start || _hasUntil.IsChecked == true && (_until.SelectedDate is null || _until.SelectedDate < start)) { Message.Text = "请填写有效的开始、结束日期。"; return; }
        if (_metric.SelectedIndex == 2 && ((Link)_habit.SelectedItem).Id is null) { Message.Text = "请先创建习惯并选择关联习惯。"; return; }
        if (_metric.SelectedIndex == 2 && (_cycle.SelectedIndex == 0 && target > 1 || _cycle.SelectedIndex == 1 && target > 7 || _cycle.SelectedIndex == 2 && target > 31)) { Message.Text = "打卡天数超过所选周期天数，可改用累计目标。"; return; }
        if (_metric.SelectedIndex == 0 && _unit.SelectedIndex == 0) target *= 60;
        Commit(() => Main.Goals.SaveGoal(new(_item?.Id ?? Guid.NewGuid(), _name.Text.Trim(), (GoalMetric)_metric.SelectedIndex, target, (GoalCycle)_cycle.SelectedIndex, DateOnly.FromDateTime(start), _hasUntil.IsChecked == true ? DateOnly.FromDateTime(_until.SelectedDate!.Value) : null, _metric.SelectedIndex != 2 ? ((Link)_project.SelectedItem).Id : null, _metric.SelectedIndex == 2 ? ((Link)_habit.SelectedItem).Id : null)));
    }
    protected override void Delete() { if (_item is not null && ConfirmDeletionWindow.Ask(this, $"删除目标‘{_item.Name}’？专注与打卡记录仍保留。")) Commit(() => Main.Goals.DeleteGoal(_item.Id)); }
}
