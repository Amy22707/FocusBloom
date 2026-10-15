using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Text.RegularExpressions;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;

namespace FocusBloom.Desktop.Windows;

public sealed class CourseEditorWindow : CalendarFormWindow
{
    private sealed record ColorChoice(string Color, string Name);
    private sealed record ProjectChoice(Guid? Id, string Name);
    private readonly SchoolCourse? _item;
    private readonly TextBox _name, _teacher, _customColor;
    private readonly ComboBox _color, _project;
    private readonly StackPanel _meetingPanel = new();
    private readonly List<CourseMeetingEditor> _meetings = new();
    private readonly ReminderSettingsControl _reminder;
    private static readonly string[] FirstControlNames = { "WeekdaySelector", "StartPeriodSelector", "EndPeriodSelector", "WeeksInput", "PatternSelector", "RoomInput" };
    public CourseEditorWindow(MainViewModel main, SchoolCourse? item = null, CalendarEntry? occurrence = null) : base(main, item is null ? "添加课程" : "编辑课程", item is not null)
    {
        _item = item; var term = main.Calendar.Store.GetSchoolTerm();
        _name = AddTextInput("NameInput", "课程名称", item?.Name ?? "");
        _teacher = AddTextInput("TeacherInput", "教师", item?.Teacher ?? "");
        Caption("上课时段 · 每个时段分别设置星期、节次与周次"); Fields.Children.Add(_meetingPanel);
        var add = new Button { Content = "＋ 添加上课时段", Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 7, 12, 7) };
        RegisterName("AddMeetingButton", add); add.Click += (_, _) =>
        {
            CourseMeeting meeting;
            try { meeting = _meetings[^1].Read(term.Weeks) with { Id = Guid.NewGuid(), Weekday = _meetings[^1].Read(term.Weeks).Weekday % 7 + 1 }; }
            catch (FormatException) { meeting = new(Guid.NewGuid(), 1, term.Periods[0].Number, term.Periods[Math.Min(1, term.Periods.Count - 1)].Number, LastWeek: term.Weeks); }
            AddMeeting(meeting, true); Preview();
        }; Fields.Children.Add(add);
        var colors = new[] {
            new ColorChoice("#7B97A6", "雾蓝"), new ColorChoice("#879F8C", "鼠尾草绿"), new ColorChoice("#AB87A0", "灰玫瑰"), new ColorChoice("#A99BC3", "淡紫"), new ColorChoice("#C3A97B", "暖杏"), new ColorChoice("#9DAD75", "橄榄绿"),
            new ColorChoice("#5B82B2", "湖蓝"), new ColorChoice("#6173A7", "靛蓝"), new ColorChoice("#6BA7C8", "晴空蓝"), new ColorChoice("#60A5A5", "青瓷"), new ColorChoice("#67AC99", "薄荷绿"), new ColorChoice("#488B70", "松绿"),
            new ColorChoice("#789D59", "苔绿"), new ColorChoice("#B3AC54", "黄绿"), new ColorChoice("#CAB34F", "麦黄"), new ColorChoice("#D3A064", "蜂蜜"), new ColorChoice("#CC8E67", "蜜桃"), new ColorChoice("#B67962", "陶土"),
            new ColorChoice("#C37986", "珊瑚粉"), new ColorChoice("#B46B79", "莓红"), new ColorChoice("#B878B0", "兰粉"), new ColorChoice("#9969A8", "紫藤"), new ColorChoice("#8877AD", "丁香"), new ColorChoice("#908481", "暖灰") };
        _color = Choice("ColorSelector", "课程颜色", colors, colors.FirstOrDefault(color => color.Color.Equals(item?.Color, StringComparison.OrdinalIgnoreCase)), "Name");
        _color.DisplayMemberPath = "";
        _color.ItemTemplate = (DataTemplate)XamlReader.Parse("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal'><Ellipse Width='14' Height='14' Fill='{Binding Color}' Margin='0,0,8,0'/><TextBlock Text='{Binding Name}' VerticalAlignment='Center'/></StackPanel></DataTemplate>");
        _customColor = AddTextInput("CustomColorInput", "颜色值 / #RRGGBB（也可自定义）", item?.Color ?? ((ColorChoice)_color.SelectedItem).Color);
        _color.SelectionChanged += (_, _) => { if (_color.SelectedItem is ColorChoice color) _customColor.Text = color.Color; };
        var projects = new[] { new ProjectChoice(null, "不关联项目") }.Concat(main.Projects.Select(project => new ProjectChoice(project.Id, $"{project.Category} / {project.Name}"))).ToArray();
        _project = Choice("ProjectSelector", "关联专注项目（可选）", projects, projects.FirstOrDefault(project => project.Id == item?.ProjectId), "Name");
        Caption("上课提醒"); _reminder = new(item?.Reminder ?? new(true, 10)); Fields.Children.Add(_reminder);
        foreach (var meeting in item is null ? new[] { new CourseMeeting(Guid.Empty, 1, term.Periods[0].Number, term.Periods[Math.Min(1, term.Periods.Count - 1)].Number, LastWeek: term.Weeks) } : CalendarRules.Meetings(item)) AddMeeting(meeting, _meetings.Count == 0);
        if (occurrence is not null && item is not null)
        {
            var adjust = new Button { Content = "临时调整这一次", Margin = new Thickness(0, 14, 0, 0) };
            adjust.Click += (_, _) => { new CourseChangeWindow(main, item, occurrence.OriginalDate ?? occurrence.Date, occurrence.MeetingId) { Owner = this }.ShowDialog(); Preview(); }; Fields.Children.Add(adjust);
        }
        if (item is not null) { var links = FocusLinks(null, () => ((ProjectChoice)_project.SelectedItem).Id); _project.SelectionChanged += (_, _) => links(); }
        Preview(); Finish();
    }
    private void AddMeeting(CourseMeeting item, bool expanded)
    {
        CourseMeetingEditor? editor = null;
        editor = new(item, Main.Calendar.Store.GetSchoolTerm(), Preview, () =>
        {
            if (_meetings.Count <= 1 || !ConfirmDeletionWindow.Ask(this, "移除这个上课时段及其临时调课？")) return;
            _meetings.Remove(editor!); _meetingPanel.Children.Remove(editor!); UpdateMeetings(); Preview();
        }) { IsExpanded = expanded };
        _meetings.Add(editor); _meetingPanel.Children.Add(editor); UpdateMeetings();
    }
    private void UpdateMeetings()
    {
        foreach (var editor in _meetings) editor.RemoveButton.IsEnabled = _meetings.Count > 1;
        foreach (var name in FirstControlNames) if (FindName(name) is not null) UnregisterName(name);
        var first = _meetings[0]; var controls = new object[] { first.Weekday, first.Start, first.End, first.Weeks, first.Pattern, first.Room };
        for (var index = 0; index < FirstControlNames.Length; index++) RegisterName(FirstControlNames[index], controls[index]);
    }
    private SchoolCourse Draft()
    {
        var meetings = _meetings.Select(editor => editor.Read(Main.Calendar.Store.GetSchoolTerm().Weeks)).ToArray(); var first = meetings[0];
        return new(_item?.Id ?? Guid.NewGuid(), _name.Text.Trim(), first.Weekday, first.StartPeriod, first.EndPeriod, first.FirstWeek, first.LastWeek, first.Pattern, first.Weeks,
            first.Room, _teacher.Text.Trim(), _customColor.Text.Trim().ToUpperInvariant(), ((ProjectChoice)_project.SelectedItem).Id, Meetings: meetings);
    }
    private void Preview()
    {
        if (_reminder is null || _meetings.Count == 0) return;
        try
        {
            var draft = Draft(); var term = Main.Calendar.Store.GetSchoolTerm(); var today = DateOnly.FromDateTime(DateTime.Today);
            var changes = Main.Calendar.Store.GetCourseChanges().Where(change => change.CourseId == draft.Id).ToArray();
            var last = changes.Where(change => change.NewDate is not null).Select(change => change.NewDate!.Value).Append(CalendarRules.Monday(term.Start).AddDays(term.Weeks * 7 - 1)).Max();
            var entry = CalendarRules.CourseEntries(term, new[] { draft }, changes, today, last).Where(entry => entry.Date.ToDateTime(entry.Start!.Value) >= DateTime.Now).OrderBy(entry => entry.Date).ThenBy(entry => entry.Start).FirstOrDefault();
            _reminder.SetOccurrence(entry?.Date, entry?.Start);
        }
        catch (FormatException) { _reminder.SetOccurrence(null, null); }
    }
    protected override void Save()
    {
        try
        {
            if (_name.Text.Trim().Length == 0) { Message.Text = "请填写课程名称。"; return; }
            if (!Regex.IsMatch(_customColor.Text.Trim(), "^#[0-9a-fA-F]{6}$")) { Message.Text = "颜色格式为#RRGGBB，例如#7B97A6。"; return; }
            if (!_reminder.TryGetOptions(out var reminder)) { Message.Text = "请填写有效的提醒设置。"; return; }
            var item = Draft() with { Reminder = reminder }; Commit(() => Main.Calendar.Store.SaveCourse(item));
        }
        catch (FormatException ex) { Message.Text = ex.Message; }
    }
    protected override void Delete()
    { if (_item is not null && ConfirmDeletionWindow.Ask(this, $"删除课程‘{_item.Name}’、全部上课时段及临时调课？")) Commit(() => Main.Calendar.Store.DeleteCourse(_item.Id)); }
}
