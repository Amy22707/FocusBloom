using Microsoft.VisualBasic.FileIO;

namespace FocusBloom.Core.Calendar;

public static class CourseCsv
{
    public const string Header = "课程,星期,开始节,结束节,周次,教室,教师";

    public static IReadOnlyList<SchoolCourse> Read(TextReader input, SchoolTerm term, IEnumerable<SchoolCourse> existing)
    {
        using var csv = new TextFieldParser(input) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
        csv.SetDelimiters(",");
        if (csv.ReadFields() is not { } header || string.Join(',', header) != Header)
            throw new FormatException($"CSV 首行应为：{Header}");
        var rows = new List<(string Name, string Teacher, CourseMeeting Meeting)>();
        var periods = term.Periods.Select(period => period.Number).ToHashSet();
        var known = existing.SelectMany(course => CalendarRules.Meetings(course)
            .Select(meeting => Key(course.Name, course.Teacher, meeting))).ToHashSet();
        while (!csv.EndOfData)
        {
            var line = csv.LineNumber;
            string[] fields;
            try { fields = csv.ReadFields()!; }
            catch (MalformedLineException error) { throw new FormatException($"第 {line} 行的 CSV 引号或字段格式无效。", error); }
            if (fields.Length != 7 || string.IsNullOrWhiteSpace(fields[0])
                || !int.TryParse(fields[1], out var weekday) || weekday is < 1 or > 7
                || !int.TryParse(fields[2], out var start) || !periods.Contains(start)
                || !int.TryParse(fields[3], out var end) || !periods.Contains(end) || end < start)
                throw new FormatException($"第 {line} 行课程、星期或节次无效；星期填 1–7，节次须在当前学期设置中。");
            IReadOnlyList<int> weeks;
            try { weeks = fields[4].Length == 0 ? Enumerable.Range(1, term.Weeks).ToArray() : CalendarRules.ParseWeeks(fields[4]); }
            catch (FormatException error) { throw new FormatException($"第 {line} 行：{error.Message}", error); }
            if (weeks.Count == 0 || weeks[^1] > term.Weeks) throw new FormatException($"第 {line} 行周次超出当前学期。");
            var meeting = new CourseMeeting(Guid.NewGuid(), weekday, start, end, weeks[0], weeks[^1], Weeks: string.Join(',', weeks), Room: fields[5]);
            if (known.Add(Key(fields[0], fields[6], meeting))) rows.Add((fields[0], fields[6], meeting));
        }
        return rows.GroupBy(row => (row.Name, row.Teacher)).Select(group =>
        {
            var meetings = group.Select(row => row.Meeting).ToArray(); var first = meetings[0];
            return new SchoolCourse(Guid.NewGuid(), group.Key.Name, first.Weekday, first.StartPeriod, first.EndPeriod,
                first.FirstWeek, first.LastWeek, Weeks: first.Weeks, Room: first.Room, Teacher: group.Key.Teacher, Meetings: meetings);
        }).ToArray();

        (string, string, int, int, int, string, string) Key(string name, string teacher, CourseMeeting meeting) =>
            (name, teacher, meeting.Weekday, meeting.StartPeriod, meeting.EndPeriod, meeting.Room,
             string.Join(',', Enumerable.Range(1, term.Weeks).Where(week => CalendarRules.Applies(meeting, week))));
    }
}
