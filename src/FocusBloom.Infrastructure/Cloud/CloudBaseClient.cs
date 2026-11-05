using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FocusBloom.Core.Sync;

namespace FocusBloom.Infrastructure.Cloud;

public sealed record CloudBaseOptions(string EnvironmentId = "", string Region = "ap-shanghai", string PublishableKey = "", string SyncRpc = "focusbloom_sync")
{
    public bool Configured => !string.IsNullOrWhiteSpace(EnvironmentId) && !string.IsNullOrWhiteSpace(PublishableKey);
    public string Endpoint => $"https://{EnvironmentId}.api.tcloudbasegateway.com/";
    public static CloudBaseOptions Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<CloudBaseOptions>(File.ReadAllText(path), SyncJson.Options) ?? new() : new();
}
public sealed record EmailVerification(string Email, string Id, bool IsUser);
public sealed record CloudSession(string UserId, string Email, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
public sealed record CloudUserProfile(string Name = "", string Picture = "", bool HasPassword = false);
public sealed class CloudBaseException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
    public bool RequiresSignIn => Code is "invalid_grant" or "unauthenticated" or "login_required" or "Unauthorized";
}

public sealed class CloudBaseClient(CloudBaseOptions options, HttpClient? client = null)
{
    private readonly HttpClient _http = client ?? new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions RequestJson = new(SyncJson.Options) { IgnoreReadOnlyProperties = false };
    public CloudBaseOptions Options => options;

    private async Task<JsonElement> Send(string path, object? body = null, string? accessToken = null, CancellationToken cancel = default, HttpMethod? method = null)
    {
        if (!options.Configured) throw new InvalidOperationException("账号服务尚未配置。");
        using var request = new HttpRequestMessage(method ?? (body is null ? HttpMethod.Get : HttpMethod.Post), options.Endpoint + path);
        if (accessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: RequestJson);
        using var response = await _http.SendAsync(request, cancel);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(SyncJson.Options, cancel);
        if (!response.IsSuccessStatusCode || data.TryGetProperty("error", out _))
        {
            var code = data.TryGetProperty("error", out var error) ? error.ToString()
                : data.TryGetProperty("code", out var databaseCode) ? databaseCode.ToString() : response.StatusCode.ToString();
            var message = data.TryGetProperty("error_description", out var description) ? description.GetString()
                : data.TryGetProperty("message", out var detail) ? detail.GetString() : $"账号服务请求失败（{(int)response.StatusCode}）。";
            if (code == "failed_precondition" && (message?.Contains("token hash not match", StringComparison.OrdinalIgnoreCase) == true
                || message?.Contains("token expiry", StringComparison.OrdinalIgnoreCase) == true))
                throw new CloudBaseException("invalid_grant", "登录状态已失效，请重新登录；本机数据会保留。");
            throw new CloudBaseException(code, code switch
            {
                "invalid_verification_code" => "验证码不正确，请检查后重试。",
                "captcha_required" => "邮箱服务要求图形验证，请稍后重试或通过托管登录页完成验证。",
                "invalid_grant" => "登录已过期，请重新验证邮箱。",
                "invalid_username_or_password" => "邮箱或密码不正确。",
                "password_not_set" => "此账号尚未设置密码，请先通过验证码登录。",
                "invalid_password" => "原密码不正确，请检查后重试。",
                "weak_password" => "密码强度不足，请使用8至64位大小写字母、数字和符号。",
                "PGRST202" => "同步接口尚未部署，请先在CloudBase SQL编辑器执行PostgreSQL初始化脚本。",
                _ => message ?? "账号服务暂时不可用。"
            });
        }
        return data;
    }
    public async Task<EmailVerification> SendCode(string email, CancellationToken cancel = default)
    {
        email = new System.Net.Mail.MailAddress(email.Trim()).Address;
        var result = await Send("auth/v1/verification", new { email, target = "ANY" }, cancel: cancel);
        return new(email, result.GetProperty("verification_id").GetString()!, result.TryGetProperty("is_user", out var exists) && exists.GetBoolean());
    }
    public async Task<EmailVerification> SendRecoveryCode(string email, CancellationToken cancel = default)
    {
        email = new System.Net.Mail.MailAddress(email.Trim()).Address;
        var result = await Send("auth/v1/verification", new { email, target = "USER", usage = "recovery" }, cancel: cancel);
        return new(email, result.GetProperty("verification_id").GetString()!, true);
    }
    public async Task ResetPassword(EmailVerification verification, string code, string password, CancellationToken cancel = default)
    {
        var verified = await Send("auth/v1/verification/verify", new { verification_id = verification.Id, verification_code = code.Trim() }, cancel: cancel);
        await Send("auth/v1/reset", new { email = verification.Email, new_password = password, verification_token = verified.GetProperty("verification_token").GetString() }, cancel: cancel);
    }
    public async Task<CloudSession> SignIn(EmailVerification verification, string code, CancellationToken cancel = default)
    {
        var verified = await Send("auth/v1/verification/verify", new { verification_id = verification.Id, verification_code = code.Trim() }, cancel: cancel);
        var token = verified.GetProperty("verification_token").GetString();
        var result = verification.IsUser
            ? await Send("auth/v1/signin", new { verification_token = token }, cancel: cancel)
            : await Send("auth/v1/signup", new { email = verification.Email, verification_token = token }, cancel: cancel);
        return Session(result, verification.Email);
    }
    public async Task<CloudSession> Refresh(CloudSession session, CancellationToken cancel = default)
    {
        var result = await Send("auth/v1/token", new { grant_type = "refresh_token", refresh_token = session.RefreshToken }, cancel: cancel);
        var refreshed = Session(result, session.Email);
        if (refreshed.UserId != session.UserId) throw new InvalidOperationException("账号身份发生变化，请重新登录。");
        return refreshed;
    }
    public async Task<CloudSession> SignInWithPassword(string email, string password, CancellationToken cancel = default)
    {
        email = new System.Net.Mail.MailAddress(email.Trim()).Address;
        var result = await Send("auth/v1/signin", new { username = email, password }, cancel: cancel);
        return Session(result, email);
    }
    public async Task SendPasswordCode(CloudSession session, CancellationToken cancel = default) =>
        await Send("auth/v1/user/reauthenticate", new { verify_opt = "email_code" }, session.AccessToken, cancel);
    public async Task SetPassword(CloudSession session, string password, string code, string oldPassword = "", CancellationToken cancel = default)
    {
        var body = new Dictionary<string, string> { ["new_password"] = password, ["confirm_password"] = password, ["verify_code"] = code };
        if (oldPassword.Length > 0) body["old_password"] = oldPassword;
        await Send("auth/v1/user/password", body, session.AccessToken, cancel, HttpMethod.Patch);
    }
    private static CloudSession Session(JsonElement result, string email) => new(result.GetProperty("sub").GetString()!, email,
        result.GetProperty("access_token").GetString()!, result.GetProperty("refresh_token").GetString()!,
        DateTimeOffset.UtcNow.AddSeconds(result.GetProperty("expires_in").GetDouble()));

    public async Task<CloudUserProfile> GetProfile(CloudSession session, CancellationToken cancel = default)
    {
        var result = await Send("auth/v1/user/me", accessToken: session.AccessToken, cancel: cancel);
        return new(result.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            result.TryGetProperty("picture", out var picture) ? picture.GetString() ?? "" : "",
            result.TryGetProperty("has_password", out var hasPassword) && hasPassword.GetBoolean());
    }
    public async Task UpdateProfile(CloudSession session, CloudUserProfile profile, CancellationToken cancel = default)
    {
        await Send("auth/v1/user/basic/edit", new { nickname = profile.Name, avatar_url = profile.Picture }, session.AccessToken, cancel);
    }

    public async Task<SyncResponse> Synchronize(CloudSession session, SyncRequest request, CancellationToken cancel = default, bool workspace = false)
    {
        var result = await Send("v1/rdb/rest/rpc/" + Uri.EscapeDataString(workspace ? "focusbloom_sync_workspace" : options.SyncRpc),
            new { p_cursor = request.Cursor, p_changes = request.Changes }, session.AccessToken, cancel);
        return result.Deserialize<SyncResponse>(SyncJson.Options) ?? throw new InvalidOperationException("同步服务返回了无效响应。");
    }
}
