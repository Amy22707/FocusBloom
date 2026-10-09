using System.Globalization;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Storage;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed class SqliteFocusStore : IFocusStore
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
                id TEXT PRIMARY KEY, name TEXT NOT NULL, category TEXT NOT NULL, color TEXT NOT NULL
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
            CREATE TABLE IF NOT EXISTS project_folders (name TEXT PRIMARY KEY);
            INSERT OR IGNORE INTO projects VALUES ($id, '未分类', '未分类', '#7B97A6');
            """;
        command.Parameters.AddWithValue("$id", DefaultProjectId.ToString());
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(sessions)";
        var hasStrict = false;
        using (var reader = columns.ExecuteReader())
            while (reader.Read()) if (reader.GetString(1) == "strict") hasStrict = true;
        if (!hasStrict)
        {
            columns.CommandText = "ALTER TABLE sessions ADD COLUMN strict INTEGER NOT NULL DEFAULT 0";
            columns.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<FocusProject> GetProjects()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, name, category, color FROM projects ORDER BY rowid";
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
            INSERT INTO projects VALUES ($id, $name, $category, $color)
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
        command.CommandText = "SELECT name FROM project_folders UNION SELECT category FROM projects ORDER BY name";
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

    public void SaveRecord(FocusRecord record)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT OR IGNORE INTO sessions (id, project_id, mode, started_at, ended_at, end_reason, strict)
            VALUES ($id, $project, $mode, $start, $end, $reason, $strict)
            """;
        command.Parameters.AddWithValue("$id", record.Id.ToString());
        command.Parameters.AddWithValue("$project", record.ProjectId.ToString());
        command.Parameters.AddWithValue("$mode", (int)record.Mode);
        command.Parameters.AddWithValue("$start", Format(record.StartedAt));
        command.Parameters.AddWithValue("$end", Format(record.EndedAt));
        command.Parameters.AddWithValue("$reason", (int)record.EndReason);
        command.Parameters.AddWithValue("$strict", record.Strict ? 1 : 0);
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
                   i.kind, i.started_at, i.ended_at
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
                    (EndReason)reader.GetInt32(5), intervals, reader.GetInt32(6) != 0));
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
