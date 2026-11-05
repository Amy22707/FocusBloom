using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FocusBloom.Core.Sync;
using FocusBloom.Infrastructure.Storage;
using System.Runtime.Versioning;

namespace FocusBloom.Infrastructure.Cloud;

[SupportedOSPlatform("windows")]
public sealed class AccountProfiles(string root)
{
    public string Root { get; } = Path.GetFullPath(root);
    private string SessionPath => Path.Combine(Root, "account.session");
    public CloudSession? Session { get; private set; }
    public bool SignedIn => Session is { AccessToken.Length: > 0, RefreshToken.Length: > 0 };
    public bool LoginExpired => Session is not null && !SignedIn;
    public string GuestDatabase => Path.Combine(Root, "guest", "focusbloom.db");
    public string UserDatabase(string id) => Path.Combine(Root, "accounts", Convert.ToHexString(Encoding.UTF8.GetBytes(id)), "focusbloom.db");
    public string CurrentDatabase => Session is { } session ? UserDatabase(session.UserId) : GuestDatabase;
    public void Load()
    {
        if (!File.Exists(SessionPath)) return;
        Session = JsonSerializer.Deserialize<CloudSession>(ProtectedData.Unprotect(File.ReadAllBytes(SessionPath), null, DataProtectionScope.CurrentUser), SyncJson.Options);
    }
    public void Save(CloudSession? session)
    {
        Directory.CreateDirectory(Root);
        if (session is null) File.WriteAllBytes(SessionPath, ProtectedData.Protect("null"u8.ToArray(), null, DataProtectionScope.CurrentUser));
        else File.WriteAllBytes(SessionPath, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session, SyncJson.Options), null, DataProtectionScope.CurrentUser));
        Session = session;
    }
    public void SignIn(CloudSession session, bool mergeGuest)
    {
        var account = new SqliteFocusStore(UserDatabase(session.UserId));
        if (mergeGuest && File.Exists(GuestDatabase)) account.ImportGuestData(new(GuestDatabase));
        Save(session);
    }
    public void ExpireSession()
    {
        if (Session is { } session) Save(session with { AccessToken = "", RefreshToken = "", ExpiresAt = DateTimeOffset.MinValue });
    }
}
