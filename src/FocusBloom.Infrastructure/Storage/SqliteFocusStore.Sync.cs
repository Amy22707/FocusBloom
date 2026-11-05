using System.Text.Json;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Sync;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Storage;

public sealed partial class SqliteFocusStore
{
    public void InitializeSync()
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS sync_documents (
              kind TEXT NOT NULL, id TEXT NOT NULL, payload TEXT NOT NULL, version INTEGER NOT NULL DEFAULT 0,
              revision INTEGER NOT NULL DEFAULT 1, pending INTEGER NOT NULL DEFAULT 1, conflict TEXT,
              PRIMARY KEY(kind,id));
            """;
        command.ExecuteNonQuery();
    }
    private IEnumerable<(string Kind, string Id, JsonElement Payload)> SharedDocuments()
    {
        var deleted = new HashSet<Guid>();
        using (var connection = Open()) using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id FROM projects WHERE deleted=1"; using var reader = command.ExecuteReader();
            while (reader.Read()) deleted.Add(Guid.Parse(reader.GetString(0)));
        }
        foreach (var project in GetProjects(true))
            yield return ("project", project.Id.ToString(), JsonSerializer.SerializeToElement(new SharedProject(project.Id, project.Name, project.Category, project.Color, deleted.Contains(project.Id)), SyncJson.Options));
        foreach (var record in GetRecords().Where(record => record.FocusDuration >= TimeSpan.FromSeconds(10)))
            yield return ("record", record.Id.ToString(), JsonSerializer.SerializeToElement(record with { TodoId = null }, SyncJson.Options));
        foreach (var key in SyncJson.SettingKeys)
            if (GetSetting(key) is { } value) yield return ("setting", key, JsonSerializer.SerializeToElement(value, SyncJson.Options));
    }
    private void CaptureSyncChanges(SqliteConnection connection, SqliteTransaction transaction, bool workspace)
    {
        var changed = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_documents(kind,id,payload) VALUES($kind,$id,$payload)
            ON CONFLICT(kind,id) DO UPDATE SET payload=$payload,revision=revision+1,pending=1,conflict=NULL
            WHERE sync_documents.payload<>$payload;
            """;
        var kindParam = command.Parameters.Add("$kind", SqliteType.Text);
        var idParam = command.Parameters.Add("$id", SqliteType.Text);
        var payloadParam = command.Parameters.Add("$payload", SqliteType.Text);
        foreach (var (kind, id, payload) in workspace ? WorkspaceDocuments(connection, transaction).ToList() : SharedDocuments())
        {
            if (workspace) seen.Add(id);
            kindParam.Value = kind; idParam.Value = id;
            payloadParam.Value = workspace ? WorkspaceJson(payload) : payload.GetRawText();
            changed += command.ExecuteNonQuery();
        }
        if (!workspace)
        {
            if (changed > 0) MarkSyncChange(connection, transaction);
            return;
        }
        using var find = connection.CreateCommand(); find.Transaction = transaction;
        find.CommandText = "SELECT id FROM sync_documents WHERE kind='workspace' AND payload<>'null'";
        var removed = new List<string>();
        using (var reader = find.ExecuteReader()) while (reader.Read())
        {
            var id = reader.GetString(0);
            if (!seen.Contains(id)) removed.Add(id);
        }
        foreach (var id in removed)
        {
            kindParam.Value = "workspace"; idParam.Value = id; payloadParam.Value = "null"; changed += command.ExecuteNonQuery();
        }
        if (changed > 0) MarkSyncChange(connection, transaction);
    }
    private static void MarkSyncChange(SqliteConnection connection, SqliteTransaction transaction)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO settings(key,value) VALUES('sync.localRevision','1')
            ON CONFLICT(key) DO UPDATE SET value=CAST(settings.value AS INTEGER)+1;
            INSERT INTO settings(key,value) VALUES('sync.changedAt',$now)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value;
            """;
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O")); command.ExecuteNonQuery();
    }
    public SyncSchedule ObserveSyncSchedule()
    {
        InitializeSync();
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        CaptureSyncChanges(connection, transaction, false);
        CaptureSyncChanges(connection, transaction, true);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            SELECT (SELECT value FROM settings WHERE key='sync.localRevision'),
                (SELECT value FROM settings WHERE key='sync.attemptedRevision'),
                EXISTS(SELECT 1 FROM sync_documents WHERE pending=1 AND conflict IS NULL),
                (SELECT value FROM settings WHERE key='sync.changedAt'),
                COALESCE((SELECT value FROM settings WHERE key='sync.lastAttempt'),
                         (SELECT value FROM settings WHERE key='sync.lastSuccess'))
            """;
        SyncSchedule schedule;
        using (var row = command.ExecuteReader())
        {
            row.Read();
            string? Value(int index) => row.IsDBNull(index) ? null : row.GetString(index);
            static DateTimeOffset? ParseAt(string? value) => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var at) ? at : null;
            schedule = new(long.TryParse(Value(0), out var revision) ? revision : 0,
                long.TryParse(Value(1), out var attempted) ? attempted : -1, row.GetBoolean(2), ParseAt(Value(3)), ParseAt(Value(4)));
        }
        transaction.Commit(); return schedule;
    }
    public void RecordSyncAttempt(long revision, DateTimeOffset now)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings(key,value) VALUES('sync.lastAttempt',$at),('sync.attemptedRevision',$revision)
            ON CONFLICT(key) DO UPDATE SET value=excluded.value
            """;
        command.Parameters.AddWithValue("$at", now.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$revision", revision);
        command.ExecuteNonQuery();
    }
    public SyncRequest PrepareSync(bool workspace = false)
    {
        InitializeSync();
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: !workspace);
        CaptureSyncChanges(connection, transaction, workspace);
        var changes = new List<SyncChange>();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT kind,id,version,revision,payload FROM sync_documents WHERE pending=1 AND conflict IS NULL AND (kind='workspace')=$workspace";
        command.Parameters.AddWithValue("$workspace", workspace ? 1 : 0);
        using (var reader = command.ExecuteReader())
            while (reader.Read()) changes.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3), JsonSerializer.Deserialize<JsonElement>(reader.GetString(4))));
        command.CommandText = "SELECT value FROM settings WHERE key=$cursorKey";
        command.Parameters.Clear(); command.Parameters.AddWithValue("$cursorKey", workspace ? "sync.workspace.cursor" : "sync.cursor");
        var cursor = long.TryParse(command.ExecuteScalar() as string, out var value) ? value : 0;
        transaction.Commit();
        return new(cursor, changes.OrderBy(change => workspace ? WorkspaceOrder(change.Id) : change.Kind == "project" ? 0 : change.Kind == "record" ? 1 : 2)
            .ThenBy(change => change.Id, StringComparer.Ordinal).Take(50).ToArray());
    }
    public int PendingSyncCount
    {
        get { InitializeSync(); using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sync_documents WHERE pending=1"; return Convert.ToInt32(command.ExecuteScalar()); }
    }
    public IReadOnlyList<SyncDocument> SyncConflicts()
    {
        InitializeSync(); var result = new List<SyncDocument>();
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT conflict FROM sync_documents WHERE conflict IS NOT NULL";
        using var reader = command.ExecuteReader(); while (reader.Read()) result.Add(JsonSerializer.Deserialize<SyncDocument>(reader.GetString(0), SyncJson.Options)!);
        return result;
    }
    public SyncDocument GetSyncLocalDocument(string kind, string id)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT version,payload FROM sync_documents WHERE kind=$kind AND id=$id"; Bind(command, kind, id);
        using var reader = command.ExecuteReader(); if (!reader.Read()) throw new InvalidOperationException("本机同步内容不存在。");
        return new(kind, id, reader.GetInt64(0), JsonSerializer.Deserialize<JsonElement>(reader.GetString(1)));
    }
    public void ApplySync(SyncRequest sent, SyncResponse response, bool workspace = false)
    {
        // 请求期间仍可编辑本机数据；先捕获这些改动，再确认已上传的版本。
        InitializeSync();
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: !workspace);
        CaptureSyncChanges(connection, transaction, workspace);
        foreach (var accepted in response.Accepted)
        {
            var change = sent.Changes.First(item => item.Kind == accepted.Kind && item.Id == accepted.Id);
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE sync_documents SET version=$version,pending=CASE WHEN revision=$revision THEN 0 ELSE 1 END,conflict=NULL WHERE kind=$kind AND id=$id";
            Bind(command, accepted.Kind, accepted.Id); command.Parameters.AddWithValue("$version", accepted.Version); command.Parameters.AddWithValue("$revision", change.LocalRevision); command.ExecuteNonQuery();
        }
        foreach (var document in response.Documents.Concat(response.Conflicts).OrderBy(item => workspace ? WorkspaceOrder(item.Id) : item.Kind == "project" ? 0 : item.Kind == "record" ? 1 : 2))
        {
            using var query = connection.CreateCommand(); query.Transaction = transaction;
            query.CommandText = "SELECT version,pending FROM sync_documents WHERE kind=$kind AND id=$id"; Bind(query, document.Kind, document.Id);
            long version = -1; var pending = false;
            using (var reader = query.ExecuteReader()) if (reader.Read()) { version = reader.GetInt64(0); pending = reader.GetBoolean(1); }
            if (document.Version <= version) continue;
            if (pending && document.Kind != "record")
            {
                using var conflict = connection.CreateCommand(); conflict.Transaction = transaction;
                conflict.CommandText = "UPDATE sync_documents SET conflict=$conflict WHERE kind=$kind AND id=$id"; Bind(conflict, document.Kind, document.Id);
                conflict.Parameters.AddWithValue("$conflict", JsonSerializer.Serialize(document, SyncJson.Options)); conflict.ExecuteNonQuery();
            }
            else ApplySharedDocument(connection, transaction, document);
        }
        using (var cursor = connection.CreateCommand())
        {
            cursor.Transaction = transaction; cursor.CommandText = "INSERT INTO settings(key,value) VALUES($key,$cursor) ON CONFLICT(key) DO UPDATE SET value=$cursor";
            cursor.Parameters.AddWithValue("$key", workspace ? "sync.workspace.cursor" : "sync.cursor");
            cursor.Parameters.AddWithValue("$cursor", response.Cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)); cursor.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    private static void Bind(SqliteCommand command, string kind, string id)
    { command.Parameters.AddWithValue("$kind", kind); command.Parameters.AddWithValue("$id", id); }
    private static string Canonical(SyncDocument document) => document.Kind switch
    {
        "workspace" => WorkspaceJson(document.Payload),
        "project" => JsonSerializer.Serialize(document.Payload.Deserialize<SharedProject>(SyncJson.Options), SyncJson.Options),
        "record" => JsonSerializer.Serialize(document.Payload.Deserialize<FocusRecord>(SyncJson.Options)! with { TodoId = null }, SyncJson.Options),
        "setting" => JsonSerializer.Serialize(document.Payload.GetString(), SyncJson.Options),
        _ => throw new InvalidOperationException("同步数据类型不受支持。")
    };
    private static void ApplySharedDocument(SqliteConnection connection, SqliteTransaction transaction, SyncDocument document)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        switch (document.Kind)
        {
            case "workspace":
                ApplyWorkspaceRow(connection, transaction, document.Id, document.Payload); break;
            case "project":
                var project = document.Payload.Deserialize<SharedProject>(SyncJson.Options)!;
                command.CommandText = """
                    INSERT INTO projects(id,name,category,color,deleted) VALUES($id,$name,$category,$color,$deleted)
                    ON CONFLICT(id) DO UPDATE SET name=$name,category=$category,color=$color,deleted=$deleted;
                    INSERT OR IGNORE INTO project_folders(name) VALUES($category);
                    """;
                command.Parameters.AddWithValue("$id", project.Id.ToString()); command.Parameters.AddWithValue("$name", project.Name); command.Parameters.AddWithValue("$category", project.Category);
                command.Parameters.AddWithValue("$color", project.Color); command.Parameters.AddWithValue("$deleted", project.Deleted ? 1 : 0); command.ExecuteNonQuery(); break;
            case "record":
                var record = document.Payload.Deserialize<FocusRecord>(SyncJson.Options)!;
                if (record.FocusDuration < TimeSpan.FromSeconds(10)) throw new InvalidOperationException("同步记录有效时长不足10秒。");
                command.CommandText = "INSERT OR IGNORE INTO sessions(id,project_id,mode,started_at,ended_at,end_reason,strict,todo_id,interceptions) VALUES($id,$project,$mode,$start,$end,$reason,$strict,NULL,$count)";
                command.Parameters.AddWithValue("$id", record.Id.ToString()); command.Parameters.AddWithValue("$project", record.ProjectId.ToString()); command.Parameters.AddWithValue("$mode", (int)record.Mode);
                command.Parameters.AddWithValue("$start", Format(record.StartedAt)); command.Parameters.AddWithValue("$end", Format(record.EndedAt)); command.Parameters.AddWithValue("$reason", (int)record.EndReason);
                command.Parameters.AddWithValue("$strict", record.Strict ? 1 : 0); command.Parameters.AddWithValue("$count", (object?)record.InterceptionCount ?? DBNull.Value);
                if (command.ExecuteNonQuery() > 0)
                    for (var index = 0; index < record.Intervals.Count; index++)
                    {
                        var interval = record.Intervals[index]; using var insert = connection.CreateCommand(); insert.Transaction = transaction;
                        insert.CommandText = "INSERT INTO intervals VALUES($id,$index,$kind,$start,$end)"; insert.Parameters.AddWithValue("$id", record.Id.ToString()); insert.Parameters.AddWithValue("$index", index);
                        insert.Parameters.AddWithValue("$kind", (int)interval.Kind); insert.Parameters.AddWithValue("$start", Format(interval.StartedAt)); insert.Parameters.AddWithValue("$end", Format(interval.EndedAt)); insert.ExecuteNonQuery();
                    }
                break;
            case "setting":
                if (!SyncJson.SettingKeys.Contains(document.Id)) throw new InvalidOperationException("这项设置不参与同步。");
                command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value";
                command.Parameters.AddWithValue("$key", document.Id); command.Parameters.AddWithValue("$value", document.Payload.GetString()); command.ExecuteNonQuery(); break;
            default: throw new InvalidOperationException("同步数据类型不受支持。");
        }
        using var metadata = connection.CreateCommand(); metadata.Transaction = transaction;
        metadata.CommandText = "INSERT INTO sync_documents(kind,id,payload,version,pending) VALUES($kind,$id,$payload,$version,0) ON CONFLICT(kind,id) DO UPDATE SET payload=$payload,version=$version,pending=0,conflict=NULL";
        Bind(metadata, document.Kind, document.Id); metadata.Parameters.AddWithValue("$payload", Canonical(document)); metadata.Parameters.AddWithValue("$version", document.Version); metadata.ExecuteNonQuery();
    }
    public void ResolveSyncConflict(SyncDocument document, bool keepLocal)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        if (keepLocal)
        {
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "UPDATE sync_documents SET version=$version,pending=1,conflict=NULL WHERE kind=$kind AND id=$id"; Bind(command, document.Kind, document.Id);
            command.Parameters.AddWithValue("$version", document.Version); command.ExecuteNonQuery();
            MarkSyncChange(connection, transaction);
        }
        else ApplySharedDocument(connection, transaction, document);
        transaction.Commit();
    }
    public void ImportGuestData(SqliteFocusStore guest, bool replacePreferences = false)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS guest";
        command.Parameters.AddWithValue("$path", guest.DatabasePath); command.ExecuteNonQuery(); command.Parameters.Clear();
        try
        {
            using var transaction = connection.BeginTransaction(); command.Transaction = transaction;
            foreach (var table in new[] { "projects", "project_folders", "todo_categories", "todo_folders", "todo_items",
                "sessions", "intervals", "session_outcomes", "date_groups", "date_events", "calendar_plans",
                "school_courses", "course_changes", "focus_goals", "habits", "habit_checkins", "daily_reflections" })
            {
                command.CommandText = $"PRAGMA main.table_info(\"{table}\")";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader()) while (reader.Read()) columns.Add('"' + reader.GetString(1) + '"');
                var names = string.Join(',', columns);
                command.CommandText = $"INSERT OR IGNORE INTO main.\"{table}\" ({names}) SELECT {names} FROM guest.\"{table}\"";
                command.ExecuteNonQuery();
            }
            command.CommandText = """
                UPDATE main.sessions SET todo_id=(SELECT todo_id FROM guest.sessions WHERE guest.sessions.id=main.sessions.id)
                WHERE todo_id IS NULL AND EXISTS(SELECT 1 FROM guest.sessions WHERE guest.sessions.id=main.sessions.id AND todo_id IS NOT NULL);
                """;
            command.ExecuteNonQuery();
            command.CommandText = """
                INSERT INTO main.settings(key,value) SELECT key,value FROM guest.settings
                WHERE key NOT LIKE 'sync.%' AND key NOT LIKE 'account.%' AND key NOT IN ('focus.checkpoint','notifications.pending')
                ON CONFLICT(key) DO UPDATE SET value=excluded.value WHERE $replace=1;
                """;
            command.Parameters.AddWithValue("$replace", replacePreferences ? 1 : 0); command.ExecuteNonQuery();
            transaction.Commit(); command.Transaction = null;
        }
        finally { command.Parameters.Clear(); command.Transaction = null; command.CommandText = "DETACH DATABASE guest"; command.ExecuteNonQuery(); }
    }
}
