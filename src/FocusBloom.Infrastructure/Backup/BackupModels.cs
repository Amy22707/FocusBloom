namespace FocusBloom.Infrastructure.Backup;

public sealed record BackupOptions(bool Automatic = false, string Directory = "", int Keep = 7, DateTimeOffset? LastAutomatic = null, string? LastFile = null, DateTimeOffset? LastBackup = null);
public sealed record BackupAsset(string OriginalPath, string Entry, long Bytes);
public sealed record BackupFolder(string OriginalPath, string Entry);
public sealed record BackupManifest(string Format, int Version, DateTimeOffset CreatedAt, IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<BackupAsset> Assets, IReadOnlyList<BackupFolder> Folders, IReadOnlyList<string> Missing);
public sealed record BackupResult(string File, BackupManifest Manifest);
public sealed record BackupProgress(string Message, int Completed, int Total);

public sealed class PreparedRestore(string directory, string database, string media, string destinationMedia, BackupManifest manifest) : IDisposable
{
    public string Directory { get; } = directory;
    public string Database { get; } = database;
    public string Media { get; } = media;
    public string DestinationMedia { get; } = destinationMedia;
    public BackupManifest Manifest { get; } = manifest;
    public void Dispose() { if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true); }
}
