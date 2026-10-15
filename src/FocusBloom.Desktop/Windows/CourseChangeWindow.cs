using System;
using System.Linq;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class CourseChangeWindow : CalendarFormWindow
{
    private sealed record PeriodChoice(int Number, string Name);
    private readonly SchoolCourse _course;
    private readonly DateOnly _original;
    private readonly CourseChange? _item;
    private readonly Guid? _meetingId;
    private readonly CheckBox _cancel;
    private readonly DatePicker _date;
    private readonly ComboBox _start, _end;
    private readonly TextBox _room;
    public CourseChangeWindow(MainViewModel main, SchoolCourse course, DateOnly original, Guid? meetingId = null) : base(main, "临时调课", main.Calendar.Store.GetCourseChanges().Any(change => change.CourseId == course.Id && change.OriginalDate == original && (change.MeetingId ?? Guid.Empty) == (meetingId ?? Guid.Empty)))
    {
        _course = course; _original = original; _meetingId = meetingId;
        var meeting = CalendarRules.Meetings(course).Single(item => item.Id == (meetingId ?? Guid.Empty));
        _item = main.Calendar.Store.GetCourseChanges().FirstOrDefault(change => change.CourseId == course.Id && change.OriginalDate == original && (change.MeetingId ?? Guid.Empty) == (meetingId ?? Guid.Empty));
        Caption($"{course.Name} · 原上课日期 {original:yyyy-MM-dd}");
        _cancel = Check("CancelledInput", "这一次停课", _item?.Cancelled ?? false);
        _date = DateInput("NewDateInput", "调整后日期", _item?.NewDate ?? original);
        var periods = main.Calendar.Store.GetSchoolTerm().Periods.Select(period => new PeriodChoice(period.Number, $"第{period.Number}节 · {period.Start:HH:mm}—{period.End:HH:mm}")).ToArray();
        _start = Choice("StartPeriodSelector", "开始节次", periods, periods.FirstOrDefault(period => period.Number == (_item?.StartPeriod ?? meeting.StartPeriod)), "Name");
        _end = Choice("EndPeriodSelector", "结束节次", periods, periods.FirstOrDefault(period => period.Number == (_item?.EndPeriod ?? meeting.EndPeriod)), "Name");
        _room = AddTextInput("RoomInput", "这一次的教室", _item?.Room ?? meeting.Room); Finish();
    }
    protected override void Save()
    {
        if (_date.SelectedDate is null || _start.SelectedItem is not PeriodChoice start || _end.SelectedItem is not PeriodChoice end || end.Number < start.Number)
        { Message.Text = "请选择日期及有效开始、结束节次。"; return; }
        Commit(() => Main.Calendar.Store.SaveCourseChange(new(_item?.Id ?? Guid.NewGuid(), _course.Id, _original, _cancel.IsChecked == true,
            DateOnly.FromDateTime(_date.SelectedDate.Value), start.Number, end.Number, _room.Text.Trim(), _meetingId)));
    }
    protected override void Delete()
    { if (_item is not null && ConfirmDeletionWindow.Ask(this, "删除这次临时调整，恢复原来的上课安排？")) Commit(() => Main.Calendar.Store.DeleteCourseChange(_item.Id)); }
}
