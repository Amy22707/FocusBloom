using System.Globalization;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Todos;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore : IFocusStore
{
    private readonly string _connectionString;
    public static readonly Guid DefaultProjectId = Guid.Parse("6c02cad7-dd19-4f50-9c75-567b3eac38d9");

    public SqliteFocusStore(string databasePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS projects (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL, color TEXT NOT NULL,
                deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY, project_id TEXT NOT NULL REFERENCES projects(id),
                mode INTEGER NOT NULL, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                end_reason INTEGER NOT NULL, strict INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS intervals (
                session_id TEXT NOT NULL REFERENCES sessions(id), ordinal INTEGER NOT NULL,
                kind INTEGER NOT NULL, started_at TEXT NOT NULL, ended_at TEXT NOT NULL,
                PRIMARY KEY(session_id, ordinal)
            );
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS date_events (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS calendar_plans (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS school_courses (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS course_changes (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS focus_goals (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS habits (id TEXT PRIMARY KEY, payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS habit_checkins (habit_id TEXT NOT NULL REFERENCES habits(id), date TEXT NOT NULL, PRIMARY KEY(habit_id,date));
            CREATE TABLE IF NOT EXISTS project_folders (name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS todo_categories (name TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS todo_folders (
                id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL, deleted INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS todo_items (
                id TEXT PRIMARY KEY, folder_id TEXT NOT NULL REFERENCES todo_folders(id), title TEXT NOT NULL,
                status INTEGER NOT NULL, priority INTEGER NOT NULL, due_date TEXT, due_time TEXT, notes TEXT NOT NULL,
                estimated_minutes INTEGER, resume_note TEXT NOT NULL, preferred_project_id TEXT REFERENCES projects(id),
                deleted INTEGER NOT NULL DEFAULT 0, important INTEGER NOT NULL DEFAULT 0, urgent INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS session_outcomes (
                session_id TEXT PRIMARY KEY REFERENCES sessions(id), summary TEXT NOT NULL, quantity REAL,
                unit TEXT NOT NULL, next_step TEXT NOT NULL
            );
            INSERT OR IGNORE INTO projects (id, name, category, color) VALUES ($id, '未分类', '未分类', '#7B97A6');
            """;
        command.Parameters.AddWithValue("$id", DefaultProjectId.ToString());
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(sessions)";
        var hasStrict = false;
        var hasTodo = false;
        var hasInterceptions = false;
        using (var reader = columns.ExecuteReader())
            while (reader.Read())
            {
                if (reader.GetString(1) == "strict") hasStrict = true;
                if (reader.GetString(1) == "todo_id") hasTodo = true;
                if (reader.GetString(1) == "interceptions") hasInterceptions = true;
            }
        if (!hasStrict)
        {
            columns.CommandText = "ALTER TABLE sessions ADD COLUMN strict INTEGER NOT NULL DEFAULT 0";
            columns.ExecuteNonQuery();
        }
        if (!hasTodo)
        {
            columns.CommandText = "ALTER TABLE sessions ADD COLUMN todo_id TEXT REFERENCES todo_items(id)";
            columns.ExecuteNonQuery();
        }
        if (!hasInterceptions)
        {
            columns.CommandText = "ALTER TABLE sessions ADD COLUMN interceptions INTEGER";
            columns.ExecuteNonQuery();
        }
        columns.CommandText = "PRAGMA table_info(todo_items)";
        var todoColumns = new HashSet<string>();
        using (var reader = columns.ExecuteReader()) while (reader.Read()) todoColumns.Add(reader.GetString(1));
        foreach (var name in new[] { "important", "urgent" })
        {
            if (todoColumns.Contains(name)) continue;
            columns.CommandText = $"ALTER TABLE todo_items ADD COLUMN {name} INTEGER NOT NULL DEFAULT 0";
            columns.ExecuteNonQuery();
        }
        columns.CommandText = "PRAGMA table_info(projects)";
        var hasDeleted = false;
        using (var reader = columns.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) == "deleted") hasDeleted = true;
        if (!hasDeleted)
        {
            columns.CommandText = "ALTER TABLE projects ADD COLUMN deleted INTEGER NOT NULL DEFAULT 0";
            columns.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<FocusProject> GetProjects(bool includeDeleted = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, category, color FROM projects WHERE deleted=0 OR $all=1 ORDER BY rowid";
        command.Parameters.AddWithValue("$all", includeDeleted ? 1 : 0);
        using var reader = command.ExecuteReader();
        List<FocusProject> projects = [];
        while (reader.Read()) projects.Add(new FocusProject(Guid.Parse(reader.GetString(0)),
            reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return projects;
    }

    public void SaveProject(FocusProject project)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO projects (id, name, category, color) VALUES ($id, $name, $category, $color)
            ON CONFLICT(id) DO UPDATE SET name=$name, category=$category, color=$color
            """;
        command.Parameters.AddWithValue("$id", project.Id.ToString());
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$category", project.Category);
        command.Parameters.AddWithValue("$color", project.Color);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> GetFolders()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM project_folders UNION SELECT category FROM projects WHERE deleted=0 ORDER BY name";
        using var reader = command.ExecuteReader();
        List<string> folders = [];
        while (reader.Read()) folders.Add(reader.GetString(0));
        return folders;
    }

    public void SaveFolder(string name)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO project_folders VALUES ($name)";
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
    }

    public void RenameFolder(string name, string newName)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO project_folders (name) VALUES ($newName);
            UPDATE projects SET category=$newName WHERE category=$name AND deleted=0;
            DELETE FROM project_folders WHERE name=$name;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$newName", newName);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteFolder(string name)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE projects SET deleted=1 WHERE category=$name;
            DELETE FROM project_folders WHERE name=$name;
            """;
        command.Parameters.AddWithValue("$name", name);
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void DeleteProject(Guid id)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO project_folders (name) SELECT category FROM projects WHERE id=$id AND deleted=0;
            UPDATE projects SET deleted=1 WHERE id=$id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    public void SaveRecord(FocusRecord record)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO sessions (id, project_id, mode, started_at, ended_at, end_reason, strict, todo_id, interceptions)
            VALUES ($id, $project, $mode, $start, $end, $reason, $strict, $todo, $interceptions)
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$project", record.ProjectId.ToString());
        command.Parameters.AddWithValue("$mode", (int)record.Mode);
        command.Parameters.AddWithValue("$start", Format(record.StartedAt));
        command.Parameters.AddWithValue("$end", Format(record.EndedAt));
        command.Parameters.AddWithValue("$reason", (int)record.EndReason);
        command.Parameters.AddWithValue("$strict", record.Strict ? 1 : 0);
        command.Parameters.AddWithValue("$todo", (object?)record.TodoId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$interceptions", (object?)record.InterceptionCount ?? DBNull.Value);
        if (command.ExecuteNonQuery() > 0)
        {
            for (var i = 0; i < record.Intervals.Count; i++)
            {
                var interval = record.Intervals[i];
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = "INSERT INTO intervals VALUES ($id, $ordinal, $kind, $start, $end)";
                insert.Parameters.AddWithValue("$id", record.Id.ToString());
                insert.Parameters.AddWithValue("$ordinal", i);
                insert.Parameters.AddWithValue("$kind", (int)interval.Kind);
                insert.Parameters.AddWithValue("$start", Format(interval.StartedAt));
                insert.Parameters.AddWithValue("$end", Format(interval.EndedAt));
                insert.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    public IReadOnlyList<FocusRecord> GetRecords()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.id, s.project_id, s.mode, s.started_at, s.ended_at, s.end_reason, s.strict,
                   i.kind, i.started_at, i.ended_at, s.todo_id, s.interceptions
            FROM sessions s LEFT JOIN intervals i ON s.id = i.session_id
            ORDER BY s.started_at DESC, s.id, i.ordinal
            """;
        using var reader = command.ExecuteReader();
        List<FocusRecord> records = [];
        List<FocusInterval>? intervals = null;
        Guid? currentId = null;
        while (reader.Read())
        {
            var id = Guid.Parse(reader.GetString(0));
            if (id != currentId)
            {
                intervals = [];
                records.Add(new FocusRecord(id, Guid.Parse(reader.GetString(1)),
                    (TimerMode)reader.GetInt32(2), Parse(reader.GetString(3)), Parse(reader.GetString(4)),
                    (EndReason)reader.GetInt32(5), intervals, reader.GetInt32(6) != 0,
                    reader.IsDBNull(10) ? null : Guid.Parse(reader.GetString(10)), reader.IsDBNull(11) ? null : reader.GetInt32(11)));
                currentId = id;
            }
            if (!reader.IsDBNull(7)) intervals!.Add(new FocusInterval((IntervalKind)reader.GetInt32(7),
                Parse(reader.GetString(8)), Parse(reader.GetString(9))));
        }
        return records;
    }

    public string? GetSetting(string key)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key=$key";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    public void SaveSetting(string key, string value)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value=$value
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static string Format(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static DateTimeOffset Parse(string time) => DateTimeOffset.Parse(time, CultureInfo.InvariantCulture);
}
