using System.Text.Json;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    private static readonly JsonSerializerOptions CalendarJson = new(JsonSerializerDefaults.Web);
    public IReadOnlyList<DateEvent> GetDateEvents() => ReadCalendarRows<DateEvent>("date_events");
    public void SaveDateEvent(DateEvent item) { SaveDateGroup(item.Group); WriteCalendarRow("date_events", item.Id, item); }
    public IReadOnlyList<string> GetDateGroups()
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT name FROM date_groups ORDER BY rowid";
        using var reader = command.ExecuteReader(); var names = new List<string>(); while (reader.Read()) names.Add(reader.GetString(0));
        return names.Concat(GetDateEvents().Select(item => item.Group)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void SaveDateGroup(string name)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "INSERT OR IGNORE INTO date_groups(name) VALUES($name)"; command.Parameters.AddWithValue("$name", name); command.ExecuteNonQuery();
    }
    public void RenameDateGroup(string name, string newName) { if (name != newName) MoveDateGroup(name, newName); }
    public void DeleteDateGroup(string name) => MoveDateGroup(name, "未分组");
    private void MoveDateGroup(string name, string destination)
    {
        if (name == "未分组") throw new InvalidOperationException("默认分组保留，事项可以移入其他分组。");
        var items = GetDateEvents().Where(item => string.Equals(item.Group, name, StringComparison.OrdinalIgnoreCase)).ToArray();
        using var connection = Open(); using var transaction = connection.BeginTransaction(); using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "INSERT OR IGNORE INTO date_groups(name) VALUES($destination)"; command.Parameters.AddWithValue("$destination", destination); command.ExecuteNonQuery(); command.Parameters.Clear();
        foreach (var item in items) { command.CommandText = "UPDATE date_events SET payload=$payload WHERE id=$id"; command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(item with { Group = destination }, CalendarJson)); command.Parameters.AddWithValue("$id", item.Id.ToString()); command.ExecuteNonQuery(); command.Parameters.Clear(); }
        command.CommandText = "DELETE FROM date_groups WHERE name=$name"; command.Parameters.AddWithValue("$name", name); command.ExecuteNonQuery(); transaction.Commit();
    }
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
