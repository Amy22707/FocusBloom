using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Backup;

public sealed partial class LocalBackupService
{
    public const string OptionsKey = "backup.options";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _database;
    public string ProfileDirectory { get; }
    public string DefaultDirectory => Path.Combine(ProfileDirectory, "backups");
    public LocalBackupService(string database)
    { _database = Path.GetFullPath(database); ProfileDirectory = Path.GetDirectoryName(_database)!; }
    private static SqliteConnection Connect(string path, bool readOnly = false)
    { var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString()); connection.Open(); return connection; }
    private string WorkDirectory()
    { var directory = Path.Combine(ProfileDirectory, "backup-work", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory); return directory; }
    private static Dictionary<string, string> Settings(SqliteConnection connection)
    { using var command = connection.CreateCommand(); command.CommandText = "SELECT key,value FROM settings"; using var reader = command.ExecuteReader(); var result = new Dictionary<string, string>(); while (reader.Read()) result.Add(reader.GetString(0), reader.GetString(1)); return result; }
    private static void SetSetting(SqliteConnection connection, string key, string value)
    { using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO settings(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=$value"; command.Parameters.AddWithValue("$key", key); command.Parameters.AddWithValue("$value", value); command.ExecuteNonQuery(); }
    private static readonly (string Table, string Label, string Where)[] CountTables = {
        ("projects", "专注项目", " WHERE deleted=0"), ("todo_folders", "待办文件夹", " WHERE deleted=0"), ("todo_items", "待办", " WHERE deleted=0"),
        ("sessions", "专注记录", ""), ("session_outcomes", "专注成果", ""), ("date_events", "日期事项", ""), ("calendar_plans", "日历安排", ""),
        ("school_courses", "课程", ""), ("focus_goals", "目标", ""), ("habits", "习惯", ""), ("habit_checkins", "打卡", ""), ("daily_reflections", "心情与复盘", "") };
    private static Dictionary<string, int> Counts(SqliteConnection connection)
    { var counts = new Dictionary<string, int>(); foreach (var (table, label, where) in CountTables) { using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM " + table + where; counts[label] = Convert.ToInt32(command.ExecuteScalar()); } return counts; }
    private static IReadOnlyList<(string Id, JsonObject Payload)> DatePayloads(SqliteConnection connection)
    { using var command = connection.CreateCommand(); command.CommandText = "SELECT id,payload FROM date_events"; using var reader = command.ExecuteReader(); var rows = new List<(string, JsonObject)>(); while (reader.Read()) rows.Add((reader.GetString(0), JsonNode.Parse(reader.GetString(1))!.AsObject())); return rows; }

    public Task<BackupResult> CreateAsync(string destination, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default) => Task.Run(() => Create(destination, progress, cancellationToken), cancellationToken);
    private BackupResult Create(string destination, IProgress<BackupProgress>? progress, CancellationToken token)
    {
        destination = Path.GetFullPath(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (!Path.GetExtension(destination).Equals(".fbbak", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("请保存为 .fbbak 备份文件。");
        var work = WorkDirectory(); var partial = Path.Combine(Path.GetDirectoryName(destination)!, ".focusbloom-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            token.ThrowIfCancellationRequested(); progress?.Report(new("正在保存数据快照…", 0, 0)); var snapshot = Path.Combine(work, "database.sqlite");
            using (var source = Connect(_database, true)) using (var target = Connect(snapshot)) source.BackupDatabase(target);
            using var database = Connect(snapshot, true); var settings = Settings(database); var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string Folder(string path) { path = Path.GetFullPath(path); if (!folders.TryGetValue(path, out var entry)) folders.Add(path, entry = "assets/" + folders.Count.ToString("D4")); return entry; }
            void Add(string? path) { if (string.IsNullOrWhiteSpace(path)) return; path = Path.GetFullPath(path); files.TryAdd(path, Folder(Path.GetDirectoryName(path)!) + "/" + Path.GetFileName(path)); }
            if (settings.TryGetValue("focus.media", out var mediaText) && JsonNode.Parse(mediaText) is JsonObject media)
            {
                foreach (var key in new[] { "customImages", "customSounds" }) if (media[key] is JsonArray assets) foreach (var asset in assets) Add(asset?["file"]?.GetValue<string>());
                if (media["imageFolders"] is JsonArray imageFolders) foreach (var folder in imageFolders) if (folder?.GetValue<string>() is { Length: > 0 } path) Folder(path);
            }
            foreach (var row in DatePayloads(database)) Add(row.Payload["coverPath"]?.GetValue<string>());
            foreach (var pair in settings.Where(pair => pair.Key.StartsWith("widgets.", StringComparison.Ordinal) && pair.Value.TrimStart().StartsWith('{'))) if (JsonNode.Parse(pair.Value) is JsonObject widget) Add(widget["BackgroundImage"]?.GetValue<string>());
            var copied = new List<BackupAsset>(); var missing = new List<string>();
            BackupManifest manifest;
            using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(snapshot, "database.sqlite", CompressionLevel.Optimal);
                foreach (var (path, entry) in files)
                {
                    token.ThrowIfCancellationRequested(); progress?.Report(new("正在打包素材：" + Path.GetFileName(path), copied.Count + missing.Count, files.Count));
                    FileStream input;
                    try { input = File.OpenRead(path); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { missing.Add(path); continue; }
                    using (input) using (var output = zip.CreateEntry(entry, CompressionLevel.Fastest).Open()) { input.CopyToAsync(output, token).GetAwaiter().GetResult(); copied.Add(new(path, entry, input.Length)); }
                }
                manifest = new BackupManifest("FocusBloomBackup", 1, DateTimeOffset.Now, Counts(database), copied, folders.Select(pair => new BackupFolder(pair.Key, pair.Value)).ToArray(), missing);
                using (var output = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(output, manifest, Json);
            }
            token.ThrowIfCancellationRequested(); File.Move(partial, destination, true); return new(destination, manifest);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); Directory.Delete(work, true); }
    }
    public static BackupManifest Inspect(string file)
    {
        using var zip = ZipFile.OpenRead(file); var entry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("这不是 FocusBloom 备份文件。");
        using var input = entry.Open(); var manifest = JsonSerializer.Deserialize<BackupManifest>(input, Json) ?? throw new InvalidDataException("备份信息为空。");
        if (manifest.Format != "FocusBloomBackup" || manifest.Version != 1 || zip.GetEntry("database.sqlite") is null) throw new InvalidDataException("无法识别这个备份格式。");
        return manifest;
    }
    public static string NewFileName(string prefix = "FocusBloom") => prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6] + ".fbbak";
    public static bool AutomaticDue(BackupOptions options, DateTimeOffset now) => options.Automatic && (options.LastAutomatic is null || options.LastAutomatic.Value.LocalDateTime.Date < now.LocalDateTime.Date);
    public static void PruneAutomatic(string directory, int keep)
    {
        var files = Directory.GetFiles(directory, "FocusBloom-auto-*.fbbak").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        foreach (var path in files.Skip(keep)) File.Delete(path);
    }
}
