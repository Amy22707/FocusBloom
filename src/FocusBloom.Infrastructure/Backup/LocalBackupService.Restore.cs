using System.IO.Compression;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace FocusBloom.Infrastructure.Backup;

public sealed partial class LocalBackupService
{
    public Task<PreparedRestore> PrepareRestoreAsync(string file, IProgress<BackupProgress>? progress = null, CancellationToken token = default) => Task.Run(() => PrepareRestore(file, progress, token), token);
    private PreparedRestore PrepareRestore(string file, IProgress<BackupProgress>? progress, CancellationToken token)
    {
        var manifest = Inspect(file); var work = WorkDirectory(); var database = Path.Combine(work, "database.sqlite"); var media = Path.Combine(work, "media"); var destination = Path.Combine(ProfileDirectory, "restored-media", Guid.NewGuid().ToString("N"));
        try
        {
            using var zip = ZipFile.OpenRead(file); zip.GetEntry("database.sqlite")!.ExtractToFile(database);
            using (var connection = Connect(database))
            {
                using var check = connection.CreateCommand(); check.CommandText = "PRAGMA quick_check";
                if (!Equals(check.ExecuteScalar(), "ok")) throw new InvalidDataException("备份数据库不完整，无法恢复。");
                _ = Counts(connection);
                Directory.CreateDirectory(media); var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var folderPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 0; index < manifest.Folders.Count; index++)
                {
                    var folder = manifest.Folders[index]; var name = index.ToString("D4"); Directory.CreateDirectory(Path.Combine(media, name)); folderPaths[folder.OriginalPath] = Path.Combine(destination, name);
                }
                for (var index = 0; index < manifest.Assets.Count; index++)
                {
                    token.ThrowIfCancellationRequested(); var asset = manifest.Assets[index]; progress?.Report(new("正在准备素材：" + Path.GetFileName(asset.OriginalPath), index, manifest.Assets.Count));
                    var entry = zip.GetEntry(asset.Entry) ?? throw new InvalidDataException("备份缺少素材：" + Path.GetFileName(asset.OriginalPath));
                    var originalFolder = Path.GetDirectoryName(asset.OriginalPath)!; var folder = manifest.Folders.Select((item, number) => (item, number)).Single(item => item.item.OriginalPath.Equals(originalFolder, StringComparison.OrdinalIgnoreCase)).number.ToString("D4");
                    var localName = Path.Combine(folder, Path.GetFileName(asset.OriginalPath)); var extracted = Path.Combine(media, localName);
                    using (var input = entry.Open()) using (var output = File.Create(extracted)) input.CopyToAsync(output, token).GetAwaiter().GetResult();
                    if (new FileInfo(extracted).Length != asset.Bytes) throw new InvalidDataException("素材未完整读取：" + Path.GetFileName(asset.OriginalPath));
                    paths[asset.OriginalPath] = Path.Combine(destination, localName);
                }
                RewritePaths(connection, paths, folderPaths);
                using (var current = Connect(_database, true)) if (Settings(current).TryGetValue(OptionsKey, out var options)) SetSetting(connection, OptionsKey, options);
                SetSetting(connection, "notifications.pending", "[]"); SetSetting(connection, "notifications.missed", "[]");
            }
            token.ThrowIfCancellationRequested(); return new(work, database, media, destination, manifest);
        }
        catch { Directory.Delete(work, true); throw; }
    }
    private static void RewritePaths(SqliteConnection connection, IReadOnlyDictionary<string, string> paths, IReadOnlyDictionary<string, string> folders)
    {
        var settings = Settings(connection);
        if (settings.TryGetValue("focus.media", out var mediaText) && JsonNode.Parse(mediaText) is JsonObject media)
        {
            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "customImages", "customSounds" })
            {
                if (media[key] is not JsonArray assets) continue;
                foreach (var asset in assets.OfType<JsonObject>())
                {
                    if (asset["file"]?.GetValue<string>() is not { } old || !paths.TryGetValue(old, out var path)) continue;
                    asset["file"] = path;
                    if (asset["id"]?.GetValue<string>() is { } id && id.StartsWith("local:", StringComparison.Ordinal)) { ids[id] = "local:" + path; asset["id"] = ids[id]; }
                }
            }
            foreach (var key in new[] { "backgroundId", "soundId" }) if (media[key]?.GetValue<string>() is { } id && ids.TryGetValue(id, out var replacement)) media[key] = replacement;
            foreach (var key in new[] { "favoriteIds", "favoriteSoundIds", "excludedIds", "removedImageIds", "removedSoundIds", "imageOrder", "soundOrder" })
                if (media[key] is JsonArray values) for (var index = 0; index < values.Count; index++) if (values[index]?.GetValue<string>() is { } id && ids.TryGetValue(id, out var replacement)) values[index] = replacement;
            if (media["imageFolders"] is JsonArray imageFolders) for (var index = 0; index < imageFolders.Count; index++) if (imageFolders[index]?.GetValue<string>() is { } old && folders.TryGetValue(old, out var path)) imageFolders[index] = path;
            SetSetting(connection, "focus.media", media.ToJsonString());
        }
        foreach (var row in DatePayloads(connection))
        {
            if (row.Payload["coverPath"]?.GetValue<string>() is not { } old || !paths.TryGetValue(old, out var path)) continue;
            row.Payload["coverPath"] = path; using var command = connection.CreateCommand(); command.CommandText = "UPDATE date_events SET payload=$payload WHERE id=$id"; command.Parameters.AddWithValue("$payload", row.Payload.ToJsonString()); command.Parameters.AddWithValue("$id", row.Id); command.ExecuteNonQuery();
        }
        foreach (var row in ReflectionPayloads(connection))
        {
            if (row.Payload["photos"] is not JsonArray photos) continue;
            for (var index = 0; index < photos.Count; index++) if (photos[index]?.GetValue<string>() is { } old && paths.TryGetValue(old, out var path)) photos[index] = path;
            using var command = connection.CreateCommand(); command.CommandText = "UPDATE daily_reflections SET payload=$payload WHERE date=$date"; command.Parameters.AddWithValue("$payload", row.Payload.ToJsonString()); command.Parameters.AddWithValue("$date", row.Date); command.ExecuteNonQuery();
        }
        foreach (var pair in settings.Where(pair => pair.Key.StartsWith("widgets.", StringComparison.Ordinal) && pair.Value.TrimStart().StartsWith('{')))
        {
            if (JsonNode.Parse(pair.Value) is not JsonObject widget || widget["BackgroundImage"]?.GetValue<string>() is not { } old || !paths.TryGetValue(old, out var path)) continue;
            widget["BackgroundImage"] = path; SetSetting(connection, pair.Key, widget.ToJsonString());
        }
    }
    public void ApplyRestore(PreparedRestore prepared)
    {
        var mediaRoot = Path.GetFullPath(Path.Combine(ProfileDirectory, "restored-media")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(prepared.DestinationMedia).StartsWith(mediaRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("素材恢复目录无效。");
        Directory.CreateDirectory(prepared.DestinationMedia);
        try
        {
            using (var current = Connect(_database, true)) using (var staged = Connect(prepared.Database)) if (Settings(current).TryGetValue(OptionsKey, out var options)) SetSetting(staged, OptionsKey, options);
            foreach (var folder in Directory.GetDirectories(prepared.Media)) Directory.CreateDirectory(Path.Combine(prepared.DestinationMedia, Path.GetFileName(folder)));
            foreach (var file in Directory.GetFiles(prepared.Media, "*", SearchOption.AllDirectories)) File.Move(file, Path.Combine(prepared.DestinationMedia, Path.GetRelativePath(prepared.Media, file)));
            SqliteConnection.ClearAllPools(); using var source = Connect(prepared.Database, true); using var target = Connect(_database); source.BackupDatabase(target);
        }
        catch { Directory.Delete(prepared.DestinationMedia, true); throw; }
    }
}
