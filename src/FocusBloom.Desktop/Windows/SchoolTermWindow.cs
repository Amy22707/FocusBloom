using System;
using System.Linq;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class SchoolTermWindow : CalendarFormWindow
{
    private readonly DatePicker _date;
    private readonly TextBox _weeks, _periods;
    public SchoolTermWindow(MainViewModel main) : base(main, "学期与节次设置")
    {
        var term = main.Calendar.Store.GetSchoolTerm();
        _date = DateInput("TermStartInput", "第1教学周的周一", CalendarRules.Monday(term.Start));
        _weeks = AddTextInput("TermWeeksInput", "教学周数", term.Weeks.ToString());
        _periods = AddTextInput("PeriodsInput", "每行一个节次：节次 HH:mm-HH:mm", string.Join(Environment.NewLine, term.Periods.Select(period => $"{period.Number} {period.Start:HH:mm}-{period.End:HH:mm}")), true); _periods.Height = 230;
        Finish();
    }
    protected override void Save()
    {
        if (_date.SelectedDate is null || _date.SelectedDate.Value.DayOfWeek != DayOfWeek.Monday || !int.TryParse(_weeks.Text, out var weeks) || weeks is < 1 or > 60)
        { Message.Text = "请选择第1周周一，并填写1至60的教学周数。"; return; }
        try
        {
            var periods = _periods.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line =>
            {
                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var times = parts.Length == 2 ? parts[1].Split('-') : Array.Empty<string>();
                if (parts.Length != 2 || !int.TryParse(parts[0], out var number) || number is < 1 or > 30 || times.Length != 2 || !ParseTime(times[0], out var start) || start is null || !ParseTime(times[1], out var end) || end is null || end <= start)
                    throw new FormatException("节次格式例如：1 08:00-08:50，结束须晚于开始。");
                return new ClassPeriod(number, start.Value, end.Value);
            }).OrderBy(period => period.Number).ToArray();
            if (periods.Length == 0 || periods.Select(period => period.Number).Distinct().Count() != periods.Length || periods.Zip(periods.Skip(1)).Any(pair => pair.First.End > pair.Second.Start))
            { Message.Text = "节次不能重复，时间须按节次先后排列且不能重叠。"; return; }
            var required = Main.Calendar.Store.GetCourses().SelectMany(CalendarRules.Meetings).SelectMany(meeting => new[] { meeting.StartPeriod, meeting.EndPeriod })
                .Concat(Main.Calendar.Store.GetCourseChanges().SelectMany(change => new[] { change.StartPeriod, change.EndPeriod }).Where(number => number.HasValue).Select(number => number!.Value));
            if (required.Any(number => periods.All(period => period.Number != number))) { Message.Text = "请保留已有课程和调课使用的节次，或先调整对应课程。"; return; }
            if (Main.Calendar.Store.GetCourses().SelectMany(CalendarRules.Meetings).Any(meeting => meeting.LastWeek > weeks)) { Message.Text = "请先调整结束周超过新学期范围的课程。"; return; }
            Commit(() => Main.Calendar.Store.SaveSchoolTerm(new(DateOnly.FromDateTime(_date.SelectedDate.Value), weeks, periods)));
        }
        catch (FormatException ex) { Message.Text = ex.Message; }
    }
}
