using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    // Only these business columns are shared. Paths and device settings stay local.
    private sealed record WorkspaceTable(string Name, string Keys, string Values = "", string Where = "");
    private static readonly WorkspaceTable[] WorkspaceTables =
    [
        new("project_folders", "name"),
        new("todo_categories", "name"),
        new("todo_folders", "id", "name,category,deleted"),
        new("todo_items", "id", "folder_id,title,status,priority,due_date,due_time,notes,estimated_minutes,resume_note,preferred_project_id,deleted,important,urgent,completed_at"),
        new("session_outcomes", "session_id", "summary,quantity,unit,next_step"),
        new("session_todos", "id", "todo_id", "todo_id IS NOT NULL"),
        new("date_groups", "name"),
        new("date_events", "id", "payload"),
        new("calendar_plans", "id", "payload"),
        new("school_courses", "id", "payload"),
        new("course_changes", "id", "payload"),
        new("focus_goals", "id", "payload"),
        new("habits", "id", "payload"),
        new("habit_checkins", "habit_id,date"),
        new("daily_reflections", "date", "payload"),
        new("settings", "key", "value", "key = 'school.term' OR key GLOB 'reminder.待办.*' OR key GLOB 'project.plan.*'")
    ];

    private static string ActualTable(string name) => name == "session_todos" ? "sessions" : name;
    private static int WorkspaceOrder(string id) => Array.FindIndex(WorkspaceTables, table => id.StartsWith(table.Name + "/", StringComparison.Ordinal));
    private static string[] Columns(WorkspaceTable table) => (table.Keys + (table.Values.Length == 0 ? "" : "," + table.Values)).Split(',');

    private static IEnumerable<(string Kind, string Id, JsonElement Payload)> WorkspaceDocuments(SqliteConnection connection, SqliteTransaction transaction)
    {
        var records = new HashSet<string>();
        using (var query = connection.CreateCommand())
        {
            query.Transaction = transaction;
            query.CommandText = "SELECT id FROM sync_documents WHERE kind='record' AND version>0";
            using var rows = query.ExecuteReader();
            while (rows.Read()) records.Add(rows.GetString(0));
        }
        foreach (var table in WorkspaceTables)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            var columns = Columns(table);
            command.CommandText = $"SELECT {string.Join(',', columns)} FROM {ActualTable(table.Name)}" + (table.Where.Length == 0 ? "" : $" WHERE {table.Where}");
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if ((table.Name is "session_todos" or "session_outcomes") && !records.Contains(reader.GetString(0))) continue;
                var row = new JsonObject();
                for (var i = 0; i < columns.Length; i++)
                    row[columns[i]] = reader.IsDBNull(i) ? null : columns[i] == "payload" ? JsonNode.Parse(reader.GetString(i)) : JsonSerializer.SerializeToNode(reader.GetValue(i));
                if (row["payload"] is JsonObject body)
                {
                    if (table.Name == "date_events") body["coverPath"] = null;
                    if (table.Name == "daily_reflections")
                    {
                        body["photos"] = null;
                        if (!HasReflectionText(body)) continue;
                    }
                }
                var id = table.Name + "/" + string.Join('/', table.Keys.Split(',').Select(key => row[key]!.GetValue<string>()));
                yield return ("workspace", id, JsonSerializer.SerializeToElement(row));
            }
        }
    }

    private static bool HasReflectionText(JsonObject body) => body["mood"] is not null || body["tags"] is JsonArray { Count: > 0 }
        || new[] { "feeling", "highlights", "nextStep" }.Any(key => !string.IsNullOrWhiteSpace(body[key]?.GetValue<string>()));

    private static string WorkspaceJson(JsonElement value)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output)) Write(writer, value);
        return System.Text.Encoding.UTF8.GetString(output.ToArray());

        static void Write(Utf8JsonWriter writer, JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in item.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(property.Name); Write(writer, property.Value); }
                writer.WriteEndObject();
            }
            else if (item.ValueKind == JsonValueKind.Array)
            {
                writer.WriteStartArray(); foreach (var child in item.EnumerateArray()) Write(writer, child); writer.WriteEndArray();
            }
            else if (item.ValueKind == JsonValueKind.Number && item.TryGetDecimal(out var number))
                writer.WriteRawValue(number.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
            else item.WriteTo(writer);
        }
    }

    private static void ApplyWorkspaceRow(SqliteConnection connection, SqliteTransaction transaction, string id, JsonElement payload)
    {
        var index = WorkspaceOrder(id);
        if (index < 0) throw new InvalidOperationException("未知的工作区数据。");
        var table = WorkspaceTables[index]; var keys = table.Keys.Split(',');
        var values = id[(table.Name.Length + 1)..].Split('/', keys.Length);
        if (values.Length != keys.Length) throw new InvalidOperationException("工作区同步标识无效。");
        if (table.Name == "settings" && values[0] != "school.term" && !values[0].StartsWith("reminder.待办.", StringComparison.Ordinal)
            && !values[0].StartsWith("project.plan.", StringComparison.Ordinal))
            throw new InvalidOperationException("这项设置不参与工作区同步。");
        var where = string.Join(" AND ", keys.Select((key, i) => $"{key}=$k{i}"));
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        for (var i = 0; i < keys.Length; i++) command.Parameters.AddWithValue($"$k{i}", values[i]);
        if (table.Name == "session_todos")
        {
            command.CommandText = $"UPDATE sessions SET todo_id=$todo WHERE {where}";
            command.Parameters.AddWithValue("$todo", payload.ValueKind == JsonValueKind.Null ? DBNull.Value : payload.GetProperty("todo_id").GetString()!);
            if (command.ExecuteNonQuery() == 0) throw new InvalidOperationException("请先同步对应的专注记录。");
            return;
        }
        JsonObject? localBody = null;
        if (table.Name is "date_events" or "daily_reflections")
        {
            command.CommandText = $"SELECT payload FROM {table.Name} WHERE {where}";
            if (command.ExecuteScalar() is string old) localBody = JsonNode.Parse(old)!.AsObject();
        }
        var row = JsonNode.Parse(payload.GetRawText())?.AsObject();
        if (row is null && table.Name == "daily_reflections" && localBody?["photos"] is JsonArray { Count: > 0 } photos)
        {
            row = new JsonObject { ["date"] = values[0], ["payload"] = JsonSerializer.SerializeToNode(
                new FocusBloom.Core.Reflection.DailyReflection(DateOnly.ParseExact(values[0], "yyyy-MM-dd"), null, [], Photos: photos.Deserialize<string[]>()), CalendarJson) };
        }
        if (row is null)
        {
            command.CommandText = $"DELETE FROM {table.Name} WHERE {where}";
            command.ExecuteNonQuery(); return;
        }
        if (row["payload"] is JsonObject body)
        {
            if (table.Name == "date_events") body["coverPath"] = localBody?["coverPath"]?.DeepClone();
            if (table.Name == "daily_reflections") body["photos"] = localBody?["photos"]?.DeepClone();
        }
        var columns = Columns(table);
        var assignments = string.Join(',', columns.Skip(keys.Length).Select(column => $"{column}=excluded.{column}"));
        command.CommandText = $"INSERT INTO {table.Name} ({string.Join(',', columns)}) VALUES ({string.Join(',', columns.Select((_, i) => $"$v{i}"))})"
            + $" ON CONFLICT({table.Keys}) " + (assignments.Length == 0 ? "DO NOTHING" : $"DO UPDATE SET {assignments}");
        for (var i = 0; i < columns.Length; i++)
        {
            var node = row[columns[i]];
            object value = i < keys.Length ? values[i] : node?.GetValueKind() switch
            {
                null => DBNull.Value,
                JsonValueKind.Number => columns[i] == "quantity" ? (object)node.GetValue<double>() : node.GetValue<long>(),
                JsonValueKind.String => node.GetValue<string>(),
                _ => node.ToJsonString()
            };
            command.Parameters.AddWithValue($"$v{i}", value);
        }
        command.ExecuteNonQuery();
    }
}
