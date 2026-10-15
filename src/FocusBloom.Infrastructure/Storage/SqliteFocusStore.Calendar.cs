using System.Text.Json;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    private static readonly JsonSerializerOptions CalendarJson = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<DateEvent> GetDateEvents() => ReadCalendarRows<DateEvent>("date_events");
    public void SaveDateEvent(DateEvent item) => WriteCalendarRow("date_events", item.Id, item);
    public void DeleteDateEvent(Guid id) => DeleteCalendarRow("date_events", id);
    public IReadOnlyList<CalendarPlan> GetCalendarPlans() => ReadCalendarRows<CalendarPlan>("calendar_plans");
    public void SaveCalendarPlan(CalendarPlan item) => WriteCalendarRow("calendar_plans", item.Id, item);
    public void DeleteCalendarPlan(Guid id) => DeleteCalendarRow("calendar_plans", id);
    public IReadOnlyList<SchoolCourse> GetCourses() => ReadCalendarRows<SchoolCourse>("school_courses");
    public void SaveCourse(SchoolCourse item)
    {
        if (GetSetting("school.term") is null) SaveSchoolTerm(GetSchoolTerm());
        var ids = CalendarRules.Meetings(item).Select(meeting => meeting.Id).ToHashSet();
        foreach (var change in GetCourseChanges().Where(change => change.CourseId == item.Id && !ids.Contains(change.MeetingId ?? Guid.Empty))) DeleteCourseChange(change.Id);
        WriteCalendarRow("school_courses", item.Id, item);
    }
    public void DeleteCourse(Guid id)
    {
        foreach (var change in GetCourseChanges().Where(change => change.CourseId == id)) DeleteCourseChange(change.Id);
        DeleteCalendarRow("school_courses", id);
    }
    public IReadOnlyList<CourseChange> GetCourseChanges() => ReadCalendarRows<CourseChange>("course_changes");
    public void SaveCourseChange(CourseChange item)
    {
        foreach (var change in GetCourseChanges().Where(change => change.CourseId == item.CourseId && change.OriginalDate == item.OriginalDate && (change.MeetingId ?? Guid.Empty) == (item.MeetingId ?? Guid.Empty) && change.Id != item.Id)) DeleteCourseChange(change.Id);
        WriteCalendarRow("course_changes", item.Id, item);
    }
    public void DeleteCourseChange(Guid id) => DeleteCalendarRow("course_changes", id);
    public SchoolTerm GetSchoolTerm() => GetSetting("school.term") is { } json ? JsonSerializer.Deserialize<SchoolTerm>(json, CalendarJson)! : SchoolTerm.Default(DateOnly.FromDateTime(DateTime.Today));
    public void SaveSchoolTerm(SchoolTerm term) => SaveSetting("school.term", JsonSerializer.Serialize(term, CalendarJson));
    public ReminderOptions GetReminderOptions(string kind, Guid id) => GetSetting($"reminder.{kind}.{id}") is { } json ? JsonSerializer.Deserialize<ReminderOptions>(json, CalendarJson)! : new();
    public void SaveReminderOptions(string kind, Guid id, ReminderOptions options) => SaveSetting($"reminder.{kind}.{id}", JsonSerializer.Serialize(options, CalendarJson));
    public bool IsReminderHandled(string key) => GetSetting("reminder.handled." + key) is not null;
    public void MarkReminderHandled(string key) => SaveSetting("reminder.handled." + key, "True");
    private IReadOnlyList<T> ReadCalendarRows<T>(string table)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} ORDER BY rowid";
        using var reader = command.ExecuteReader(); var rows = new List<T>();
        while (reader.Read()) rows.Add(JsonSerializer.Deserialize<T>(reader.GetString(0), CalendarJson)!);
        return rows;
    }
    private void WriteCalendarRow<T>(string table, Guid id, T item)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table}(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=$payload";
        command.Parameters.AddWithValue("$id", id.ToString()); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item, CalendarJson)); command.ExecuteNonQuery();
    }
    private void DeleteCalendarRow(string table, Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {table} WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery();
    }
}
