using System.Globalization;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Todos;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    public IReadOnlyList<string> GetTodoCategories()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM todo_categories ORDER BY rowid";
        using var reader = command.ExecuteReader(); List<string> names = [];
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }
    public void SaveTodoCategory(string name)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO todo_categories VALUES ($name)";
        command.Parameters.AddWithValue("$name", name); command.ExecuteNonQuery();
    }
    public void RenameTodoCategory(string name, string newName)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO todo_categories VALUES ($new);
            UPDATE todo_folders SET category=$new WHERE category=$name AND deleted=0;
            DELETE FROM todo_categories WHERE name=$name;
            """;
        command.Parameters.AddWithValue("$name", name); command.Parameters.AddWithValue("$new", newName);
        command.ExecuteNonQuery(); transaction.Commit();
    }
    public void DeleteTodoCategory(string name)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            UPDATE todo_items SET deleted=1 WHERE folder_id IN (SELECT id FROM todo_folders WHERE category=$name);
            UPDATE todo_folders SET deleted=1 WHERE category=$name;
            DELETE FROM todo_categories WHERE name=$name;
            """;
        command.Parameters.AddWithValue("$name", name); command.ExecuteNonQuery(); transaction.Commit();
    }
    public IReadOnlyList<TodoFolder> GetTodoFolders()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,name,category FROM todo_folders WHERE deleted=0 ORDER BY rowid";
        using var reader = command.ExecuteReader(); List<TodoFolder> folders = [];
        while (reader.Read()) folders.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2)));
        return folders;
    }
    public void SaveTodoFolder(TodoFolder folder)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO todo_folders (id,name,category) VALUES ($id,$name,$category)
            ON CONFLICT(id) DO UPDATE SET name=$name,category=$category;
            """;
        command.Parameters.AddWithValue("$id", folder.Id.ToString()); command.Parameters.AddWithValue("$name", folder.Name);
        command.Parameters.AddWithValue("$category", folder.Category); command.ExecuteNonQuery();
    }
    public void DeleteTodoFolder(Guid id)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE todo_items SET deleted=1 WHERE folder_id=$id; UPDATE todo_folders SET deleted=1 WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery(); transaction.Commit();
    }
    public IReadOnlyList<TodoItem> GetTodos(bool includeDeleted = false)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id,folder_id,title,status,priority,due_date,due_time,notes,estimated_minutes,resume_note,preferred_project_id,important,urgent,completed_at
            FROM todo_items WHERE deleted=0 OR $all=1 ORDER BY rowid
            """;
        command.Parameters.AddWithValue("$all", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader(); List<TodoItem> todos = [];
        while (reader.Read()) todos.Add(new(Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), reader.GetString(2),
            (TodoStatus)reader.GetInt32(3), (TodoPriority)reader.GetInt32(4),
            reader.IsDBNull(5) ? null : DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.IsDBNull(6) ? null : TimeOnly.ParseExact(reader.GetString(6), "HH:mm", CultureInfo.InvariantCulture), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8), reader.GetString(9),
            reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)), reader.GetInt32(11) != 0, reader.GetInt32(12) != 0,
            reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture)));
        return todos;
    }
    public void SaveTodo(TodoItem todo)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO todo_items (id,folder_id,title,status,priority,due_date,due_time,notes,estimated_minutes,resume_note,preferred_project_id,important,urgent,completed_at)
            VALUES ($id,$folder,$title,$status,$priority,$date,$time,$notes,$estimate,$resume,$project,$important,$urgent,$completed)
            ON CONFLICT(id) DO UPDATE SET folder_id=$folder,title=$title,status=$status,priority=$priority,
                due_date=$date,due_time=$time,notes=$notes,estimated_minutes=$estimate,resume_note=$resume,preferred_project_id=$project,important=$important,urgent=$urgent,
                completed_at=CASE WHEN $status<>2 THEN NULL WHEN todo_items.status=2 THEN todo_items.completed_at ELSE $completed END;
            """;
        command.Parameters.AddWithValue("$id", todo.Id.ToString()); command.Parameters.AddWithValue("$folder", todo.FolderId.ToString());
        command.Parameters.AddWithValue("$title", todo.Title); command.Parameters.AddWithValue("$status", (int)todo.Status);
        command.Parameters.AddWithValue("$priority", (int)todo.Priority);
        command.Parameters.AddWithValue("$date", (object?)todo.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$time", (object?)todo.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? DBNull.Value);
        command.Parameters.AddWithValue("$notes", todo.Notes); command.Parameters.AddWithValue("$estimate", (object?)todo.EstimatedMinutes ?? DBNull.Value);
        command.Parameters.AddWithValue("$resume", todo.ResumeNote);
        command.Parameters.AddWithValue("$project", (object?)todo.PreferredProjectId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$important", todo.Important ? 1 : 0); command.Parameters.AddWithValue("$urgent", todo.Urgent ? 1 : 0);
        command.Parameters.AddWithValue("$completed", todo.Status == TodoStatus.Completed ? Format(todo.CompletedAt ?? DateTimeOffset.Now) : DBNull.Value);
        command.ExecuteNonQuery();
    }
    public void DeleteTodo(Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE todo_items SET deleted=1 WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString()); command.ExecuteNonQuery();
    }
    public FocusOutcome? GetOutcome(Guid sessionId)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT summary,quantity,unit,next_step FROM session_outcomes WHERE session_id=$id";
        command.Parameters.AddWithValue("$id", sessionId.ToString()); using var reader = command.ExecuteReader();
        return reader.Read() ? new(sessionId, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetDouble(1), reader.GetString(2), reader.GetString(3)) : null;
    }
    public void SaveOutcome(FocusOutcome outcome)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO session_outcomes VALUES ($id,$summary,$quantity,$unit,$next)
            ON CONFLICT(session_id) DO UPDATE SET summary=$summary,quantity=$quantity,unit=$unit,next_step=$next;
            """;
        command.Parameters.AddWithValue("$id", outcome.SessionId.ToString()); command.Parameters.AddWithValue("$summary", outcome.Summary);
        command.Parameters.AddWithValue("$quantity", (object?)outcome.Quantity ?? DBNull.Value);
        command.Parameters.AddWithValue("$unit", outcome.Unit); command.Parameters.AddWithValue("$next", outcome.NextStep);
        command.ExecuteNonQuery();
    }
    public IReadOnlyList<FocusOutcome> GetOutcomes()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_id,summary,quantity,unit,next_step FROM session_outcomes";
        using var reader = command.ExecuteReader(); List<FocusOutcome> outcomes = [];
        while (reader.Read()) outcomes.Add(new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.GetString(3), reader.GetString(4)));
        return outcomes;
    }
}
