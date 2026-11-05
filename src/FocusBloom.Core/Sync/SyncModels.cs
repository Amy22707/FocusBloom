using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusBloom.Core.Sync;

public sealed record SyncDocument(string Kind, string Id, long Version, JsonElement Payload);
public sealed record SyncChange(string Kind, string Id, long BaseVersion, long LocalRevision, JsonElement Payload);
public sealed record SyncAccepted(string Kind, string Id, long Version);
public sealed record SyncRequest(long Cursor, IReadOnlyList<SyncChange> Changes);
public sealed record SyncResponse(long Cursor, bool HasMore, IReadOnlyList<SyncAccepted> Accepted,
    IReadOnlyList<SyncDocument> Documents, IReadOnlyList<SyncDocument> Conflicts);
public sealed record SharedProject(Guid Id, string Name, string Category, string Color, bool Deleted = false);

public static class SyncJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static readonly string[] SettingKeys = ["timer.mode", "timer.durationMinutes", "pauseMinutes", "restMinutes",
        "restrictions.websites", "restrictions.websites.blacklist", "restrictions.websiteMode"];
}
