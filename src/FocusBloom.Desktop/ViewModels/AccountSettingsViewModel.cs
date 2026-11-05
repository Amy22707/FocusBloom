using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Win32;
using FocusBloom.Core.Sync;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Infrastructure.Cloud;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Desktop.ViewModels;

public sealed record SyncConflictRow(string Heading, string Local, string Cloud, RelayCommand KeepLocalCommand, RelayCommand UseCloudCommand);

public sealed class AccountSettingsViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly SqliteFocusStore _store;
    private readonly AccountProfiles _profiles;
    private readonly CloudBaseClient _client;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly DispatcherTimer _resendTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly CancellationTokenSource _cancel = new();
    private EmailVerification? _verification;
    private string _email = "", _code = "", _status = "", _lastSync = "尚未同步";
    private bool _busy, _mergeGuest, _disposed, _importGuestPending;
    private int _resendSeconds;
    private CloudUserProfile _profile = new();
    private string _profileName = "", _profilePicture = "";
    private bool _editingProfile;
    private bool _usePasswordLogin;
    private bool _recoveringPassword;
    private int _passwordResendSeconds;
    private string _passwordCode = "";
    public AccountSettingsViewModel(MainViewModel main, SqliteFocusStore store, AccountProfiles profiles, CloudBaseClient? client = null)
    {
        _main = main; _store = store; _profiles = profiles;
        _client = client ?? new(CloudBaseOptions.Load(Path.Combine(AppContext.BaseDirectory, "cloud-config.json")));
        _email = profiles.Session?.Email ?? "";
        if (SignedIn && store.GetSetting("account.profile") is { } savedProfile) _profile = JsonSerializer.Deserialize<CloudUserProfile>(savedProfile, SyncJson.Options) ?? new();
        Avatar = ProfileAvatar.Read(_profile.Picture);
        _status = LoginExpired ? "登录已失效，本机数据已保留，请重新登录。" : _client.Options.Configured ? profiles.Session is null ? "未登录时，数据只保存在本机。" : "已登录，等待同步。" : "账号服务尚未配置，本机功能可正常使用。";
        if (store.GetSetting("sync.lastSuccess") is { } last) _lastSync = "上次同步：" + last;
        SendCodeCommand = new(() => _ = Run(SendCode), () => !Busy && _resendSeconds == 0 && _client.Options.Configured);
        SignInCommand = new(() => _ = Run(SignIn), () => !Busy && _verification is not null);
        SyncCommand = new(() => _ = Run(() => Sync()), () => !Busy && SignedIn && _client.Options.Configured);
        SignOutCommand = new(() => _ = Run(() => { EnsureSwitchable(); _timer.Stop(); _profiles.Save(null); RestartRequested?.Invoke(); return Task.CompletedTask; }), () => !Busy && SignedIn);
        ImportGuestCommand = new(() => _ = Run(ImportGuest), () => !Busy && SignedIn && File.Exists(_profiles.GuestDatabase));
        OpenAccountCommand = new(() => { if (SignedIn) BeginEditProfile(); AccountPageRequested?.Invoke(); });
        EditProfileCommand = new(BeginEditProfile, () => !Busy && SignedIn);
        CancelProfileCommand = new(() => EditingProfile = false, () => !Busy);
        SaveProfileCommand = new(() => _ = Run(SaveProfile), () => !Busy && SignedIn);
        ChooseAvatarCommand = new(ChooseAvatar, () => !Busy);
        ClearAvatarCommand = new(() => { _profilePicture = ""; AvatarPreview = null; Notify(nameof(AvatarPreview)); }, () => !Busy);
        PasswordLoginCommand = new(() => _ = Run(LoginWithPassword), () => !Busy && IsGuest && _client.Options.Configured);
        ResetPasswordCommand = new(() => _ = Run(ResetPassword), () => !Busy && RecoveringPassword && _verification is not null);
        SendPasswordCodeCommand = new(() => _ = Run(SendPasswordCode), () => !Busy && SignedIn && _passwordResendSeconds == 0 && _client.Options.Configured);
        SetPasswordCommand = new(() => _ = Run(SetPassword), () => !Busy && SignedIn && _client.Options.Configured);
        _timer.Tick += (_, _) => CheckAutomaticSync();
        _resendTimer.Tick += (_, _) => { if (_resendSeconds > 0) _resendSeconds--; if (_passwordResendSeconds > 0) _passwordResendSeconds--; if (_resendSeconds == 0 && _passwordResendSeconds == 0) _resendTimer.Stop(); Notify(nameof(SendCodeText)); Notify(nameof(SendPasswordCodeText)); SendCodeCommand.Refresh(); SendPasswordCodeCommand.Refresh(); };
        _store.InitializeSync(); RefreshCounts();
    }
    public event Action? RestartRequested;
    public event Action? AccountPageRequested;
    public event Action? SecretsCleared;
    public RelayCommand SendCodeCommand { get; }
    public RelayCommand SignInCommand { get; }
    public RelayCommand SyncCommand { get; }
    public RelayCommand SignOutCommand { get; }
    public RelayCommand ImportGuestCommand { get; }
    public RelayCommand OpenAccountCommand { get; }
    public RelayCommand EditProfileCommand { get; }
    public RelayCommand CancelProfileCommand { get; }
    public RelayCommand SaveProfileCommand { get; }
    public RelayCommand ChooseAvatarCommand { get; }
    public RelayCommand ClearAvatarCommand { get; }
    public RelayCommand PasswordLoginCommand { get; }
    public RelayCommand ResetPasswordCommand { get; }
    public RelayCommand SendPasswordCodeCommand { get; }
    public RelayCommand SetPasswordCommand { get; }
    public ObservableCollection<SyncConflictRow> Conflicts { get; } = [];
    public bool SignedIn => _profiles.SignedIn;
    public bool LoginExpired => _profiles.LoginExpired;
    public bool IsGuest => !SignedIn;
    public bool CanMergeGuest => !LoginExpired && !RecoveringPassword;
    public string LoginHeading => RecoveringPassword ? "找回密码" : LoginExpired ? "重新登录 FocusBloom" : "登录 FocusBloom";
    public string LoginDescription => RecoveringPassword ? "通过账号邮箱验证身份，然后设置新密码。" : LoginExpired ? "本机数据已保留，重新登录后继续同步。" : "让每一段投入，都在你的设备间延续。";
    public string LoginFootnote => RecoveringPassword ? "新密码设置完成后，可使用邮箱和新密码登录。" : LoginExpired ? "当前账号的本机内容保留在原处，登录同一邮箱即可继续。" : "新邮箱验证后自动创建账号。访客原数据保留在本机。";
    public string AccountText => SignedIn ? _profiles.Session!.Email : LoginExpired ? "登录已失效" : "访客 · 本机使用";
    public string DisplayName => !SignedIn ? UiText.Get("未登录") : string.IsNullOrWhiteSpace(_profile.Name) ? _profiles.Session!.Email.Split('@')[0] : _profile.Name;
    public string AccountSubtitle => UiText.Get(SignedIn ? "个人账号" : LoginExpired ? "请重新登录" : "点击登录");
    public string AvatarInitial => SignedIn ? System.Globalization.StringInfo.GetNextTextElement(DisplayName).ToUpperInvariant() : "F";
    public ImageSource? Avatar { get; private set; }
    public ImageSource? AvatarPreview { get; private set; }
    public string ProfileName { get => _profileName; set { _profileName = value; Notify(); } }
    public bool EditingProfile { get => _editingProfile; private set { _editingProfile = value; Notify(); } }
    public bool UsePasswordLogin
    {
        get => _usePasswordLogin;
        set { if (value == _usePasswordLogin) return; _usePasswordLogin = value; if (value) SetPasswordRecovery(false); Notify(); Notify(nameof(UseCodeLogin)); Notify(nameof(ShowVerification)); }
    }
    public bool RecoveringPassword => _recoveringPassword;
    public bool ShowVerification => !UsePasswordLogin;
    public bool UseCodeLogin => !UsePasswordLogin && !RecoveringPassword;
    public void SetPasswordRecovery(bool active)
    {
        if (active == _recoveringPassword) return;
        _recoveringPassword = active;
        if (active) _usePasswordLogin = false;
        _verification = null; Code = ""; ClearSecrets();
        Notify(nameof(UsePasswordLogin)); Notify(nameof(UseCodeLogin)); Notify(nameof(ShowVerification)); Notify(nameof(RecoveringPassword));
        NotifyAccountState(); RefreshCommands();
    }
    public bool HasPassword => _profile.HasPassword;
    public string PasswordHeading => HasPassword ? "修改密码" : "设置密码";
    public string PasswordCode { get => _passwordCode; set { _passwordCode = value; Notify(); } }
    public string SendPasswordCodeText => _passwordResendSeconds > 0 ? UiText.Format("{0}秒后重发", _passwordResendSeconds) : UiText.Get("发送验证邮件");
    public string LoginPassword { private get; set; } = "";
    public string NewPassword { private get; set; } = "";
    public string ConfirmPassword { private get; set; } = "";
    public string OldPassword { private get; set; } = "";
    public bool Busy { get => _busy; private set { _busy = value; Notify(); RefreshCommands(); } }
    public string Email { get => _email; set { _email = value; _verification = null; Notify(); SignInCommand.Refresh(); ResetPasswordCommand.Refresh(); } }
    public string Code { get => _code; set { _code = value; Notify(); } }
    public bool MergeGuest { get => _mergeGuest; set { _mergeGuest = value; Notify(); } }
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string LastSync { get => _lastSync; private set { _lastSync = value; Notify(); } }
    public string SendCodeText => _resendSeconds > 0 ? UiText.Format("{0}秒后重发", _resendSeconds) : UiText.Get("获取验证码");
    public string PendingText { get; private set; } = "";
    public void Start() { if (SignedIn) { _timer.Start(); CheckAutomaticSync(); } }
    private void CheckAutomaticSync() { if (SyncCommand.CanExecute(null)) _ = Run(() => Sync(automatic: true)); }
    private void RefreshCommands() { SendCodeCommand.Refresh(); SignInCommand.Refresh(); SyncCommand.Refresh(); SignOutCommand.Refresh(); ImportGuestCommand.Refresh(); EditProfileCommand.Refresh(); CancelProfileCommand.Refresh(); SaveProfileCommand.Refresh(); ChooseAvatarCommand.Refresh(); ClearAvatarCommand.Refresh(); PasswordLoginCommand.Refresh(); SendPasswordCodeCommand.Refresh(); SetPasswordCommand.Refresh(); ResetPasswordCommand.Refresh(); }
    private async Task Run(Func<Task> operation)
    {
        if (Busy) return; Busy = true;
        try { await operation(); }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (CloudBaseException error) when (error.RequiresSignIn && _profiles.Session is not null) { ExpireLogin(); }
        catch (Exception error) { Status = "未完成：" + error.Message; }
        finally { Busy = false; NotifyAccountState(); RefreshCounts(); }
    }
    private void NotifyAccountState()
    {
        foreach (var name in new[] { nameof(SignedIn), nameof(IsGuest), nameof(LoginExpired), nameof(CanMergeGuest), nameof(LoginHeading), nameof(LoginDescription), nameof(LoginFootnote), nameof(AccountText), nameof(DisplayName), nameof(AccountSubtitle), nameof(AvatarInitial) }) Notify(name);
    }
    private void ExpireLogin()
    {
        _profiles.ExpireSession(); _timer.Stop(); EditingProfile = false; MergeGuest = false;
        Code = PasswordCode = ""; _verification = null; ClearSecrets(); Avatar = null; Notify(nameof(Avatar));
        Status = "登录已失效，请重新登录。本机数据已保留，可继续本机使用。";
    }
    private async Task SendCode()
    {
        var recovery = RecoveringPassword; var email = Email;
        var verification = recovery ? await _client.SendRecoveryCode(email, _cancel.Token) : await _client.SendCode(email, _cancel.Token);
        if (email == Email && recovery == RecoveringPassword) _verification = verification;
        Code = ""; _resendSeconds = 60; _resendTimer.Start(); Notify(nameof(SendCodeText));
        Status = recovery ? "重置密码验证码已发送，请查看邮箱。" : "验证码已发送，请查看邮箱；新邮箱验证后会创建账号。";
    }
    private void EnsureSwitchable()
    {
        if (!_main.CanConfigure || _main.HasUnsavedRecord || _main.HasPendingRecovery)
            throw new InvalidOperationException("请先结束当前计时并保存记录，再登录或退出账号。");
    }
    private async Task SignIn()
    {
        EnsureSwitchable();
        if (Code.Trim().Length != 6 || !Code.Trim().All(char.IsAsciiDigit)) throw new InvalidOperationException("请输入6位邮箱验证码。");
        var session = await _client.SignIn(_verification!, Code, _cancel.Token);
        await OpenAccount(session);
    }
    private async Task LoginWithPassword()
    {
        EnsureSwitchable(); if (LoginPassword.Length == 0) throw new InvalidOperationException("请输入密码。");
        var session = await _client.SignInWithPassword(Email, LoginPassword, _cancel.Token);
        ClearSecrets(); await OpenAccount(session);
    }
    private async Task OpenAccount(CloudSession session)
    {
        EnsureSwitchable();
        _main.SetAccountSwitching(true);
        try
        {
            await Task.Run(() => _profiles.SignIn(session, MergeGuest), _cancel.Token);
            _timer.Stop();
            Code = ""; _verification = null;
            Status = "登录成功，正在打开此账号的本地数据。"; RestartRequested?.Invoke();
        }
        finally { _main.SetAccountSwitching(false); }
    }
    private async Task ImportGuest()
    {
        EnsureSwitchable(); _main.SetAccountSwitching(true);
        try
        {
            Status = "正在备份并导入本机原有数据…";
            await Task.Run(() =>
            {
                var backup = Path.Combine(Path.GetDirectoryName(_store.DatabasePath)!, "backups", "before-local-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".db");
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                using (var source = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + _store.DatabasePath))
                using (var target = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + backup))
                { source.Open(); target.Open(); source.BackupDatabase(target); }
            }, _cancel.Token);
            _importGuestPending = true;
            _timer.Stop();
            Status = "正在导入本机数据并重新打开软件。"; RestartRequested?.Invoke();
        }
        finally { _main.SetAccountSwitching(false); }
    }
    public void CompleteLocalImport()
    {
        if (!_importGuestPending) return;
        _store.ImportGuestData(new(_profiles.GuestDatabase), replacePreferences: true);
        _importGuestPending = false;
    }
    private void BeginEditProfile()
    {
        ProfileName = DisplayName; _profilePicture = _profile.Picture; AvatarPreview = Avatar; Notify(nameof(AvatarPreview)); EditingProfile = true;
    }
    private void ChooseAvatar()
    {
        var dialog = new OpenFileDialog { Title = UiText.Get("选择头像"), Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog() != true) return;
        try { SetAvatarFile(dialog.FileName); }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException) { Status = "头像无法读取：" + error.Message; }
    }
    public void SetAvatarFile(string path)
    {
        _profilePicture = ProfileAvatar.FromFile(path); AvatarPreview = ProfileAvatar.Read(_profilePicture); Notify(nameof(AvatarPreview));
    }
    private void CacheProfile(CloudUserProfile profile)
    {
        _profile = profile; _store.SaveSetting("account.profile", JsonSerializer.Serialize(profile, SyncJson.Options));
        Avatar = ProfileAvatar.Read(profile.Picture); Notify(nameof(Avatar)); Notify(nameof(DisplayName)); Notify(nameof(AvatarInitial)); Notify(nameof(HasPassword)); Notify(nameof(PasswordHeading));
    }
    private async Task<CloudSession> CurrentSession()
    {
        var session = _profiles.Session ?? throw new InvalidOperationException("请先登录账号。");
        if (session.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1)) { session = await _client.Refresh(session, _cancel.Token); _profiles.Save(session); }
        return session;
    }
    private async Task SaveProfile()
    {
        var name = ProfileName.Trim();
        if (name.Length is < 2 or > 48) throw new InvalidOperationException("姓名或昵称需要2至48个字符。");
        var profile = new CloudUserProfile(name, _profilePicture, HasPassword); var session = await CurrentSession();
        Status = "正在保存个人资料…"; await _client.UpdateProfile(session, profile, _cancel.Token);
        CacheProfile(profile); EditingProfile = false; Status = "个人资料已保存。";
    }
    private async Task SendPasswordCode()
    {
        await _client.SendPasswordCode(await CurrentSession(), _cancel.Token);
        _passwordResendSeconds = 60; _resendTimer.Start(); Notify(nameof(SendPasswordCodeText)); Status = "验证邮件已发送到当前账号邮箱。";
    }
    private async Task SetPassword()
    {
        ValidateNewPassword();
        if (HasPassword && OldPassword.Length == 0) throw new InvalidOperationException("请输入原密码。");
        if (PasswordCode.Trim().Length != 6 || !PasswordCode.Trim().All(char.IsAsciiDigit)) throw new InvalidOperationException("请输入邮件中的6位验证码。");
        var password = NewPassword; var code = PasswordCode.Trim(); var oldPassword = OldPassword;
        await _client.SetPassword(await CurrentSession(), password, code, oldPassword, _cancel.Token);
        CacheProfile(_profile with { HasPassword = true }); PasswordCode = ""; ClearSecrets(); Status = "密码已设置，可以使用邮箱和密码登录。";
    }
    private void ValidateNewPassword()
    {
        if (NewPassword.Length is < 8 or > 64 || !NewPassword.Any(char.IsAsciiLetterUpper) || !NewPassword.Any(char.IsAsciiLetterLower) || !NewPassword.Any(char.IsAsciiDigit) || !NewPassword.Any(value => !char.IsLetterOrDigit(value) && !char.IsWhiteSpace(value)))
            throw new InvalidOperationException("请使用8至64位密码，包含大小写字母、数字和符号。");
        if (NewPassword != ConfirmPassword) throw new InvalidOperationException("两次输入的密码不一致。");
    }
    private async Task ResetPassword()
    {
        ValidateNewPassword();
        if (Code.Trim().Length != 6 || !Code.Trim().All(char.IsAsciiDigit)) throw new InvalidOperationException("请输入邮件中的6位验证码。");
        await _client.ResetPassword(_verification!, Code, NewPassword, _cancel.Token);
        SetPasswordRecovery(false); UsePasswordLogin = true;
        Status = "密码已重置，请用新密码登录。";
    }
    private void ClearSecrets() { LoginPassword = NewPassword = ConfirmPassword = OldPassword = ""; SecretsCleared?.Invoke(); }
    private async Task Sync(bool automatic = false)
    {
        if (!SignedIn) return;
        var schedule = await Task.Run(_store.ObserveSyncSchedule, _cancel.Token);
        var now = DateTimeOffset.UtcNow;
        if (automatic && !schedule.IsDue(now)) return;
        await Task.Run(() => _store.RecordSyncAttempt(schedule.Revision, now), _cancel.Token);
        var session = await CurrentSession();
        Status = "正在同步…";
        await SyncBatch(false);
        var workspaceAvailable = true;
        try { await SyncBatch(true); }
        catch (CloudBaseException error) when (error.Code == "PGRST202") { workspaceAvailable = false; }

        async Task SyncBatch(bool workspace)
        {
            SyncResponse response;
            do
            {
                var request = await Task.Run(() => _store.PrepareSync(workspace), _cancel.Token);
                response = await _client.Synchronize(session, request, _cancel.Token, workspace);
                await Task.Run(() => _store.ApplySync(request, response, workspace), _cancel.Token);
            } while (response.HasMore || response.Accepted.Count > 0 && _store.PendingSyncCount > _store.SyncConflicts().Count);
        }
        _main.ReloadSyncedData();
        var at = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        _store.SaveSetting("sync.lastSuccess", at); LastSync = "上次同步：" + at;
        Status = _store.SyncConflicts().Count > 0 ? "有两端都修改过的内容，请选择保留哪一份。" : "同步完成。";
        if (!workspaceAvailable) Status += " 工作区同步尚未部署。";
        try { CacheProfile(await _client.GetProfile(session, _cancel.Token)); }
        catch (CloudBaseException error) when (error.RequiresSignIn) { throw; }
        catch (Exception error) when (!_cancel.IsCancellationRequested && error is System.Net.Http.HttpRequestException or CloudBaseException or OperationCanceledException)
        { Status += " 个人资料暂时无法刷新，已保留本机显示。"; }
    }
    private static string Describe(SyncDocument document) => document.Kind == "workspace" ? DescribeWorkspace(document.Payload) : document.Kind == "project"
        ? document.Payload.GetProperty("name").GetString() + " · " + (document.Payload.GetProperty("deleted").GetBoolean() ? "已删除" : document.Payload.GetProperty("category").GetString())
        : document.Id is "restrictions.websites" or "restrictions.websites.blacklist"
            ? string.Join("、", JsonSerializer.Deserialize<FocusBloom.Core.Restrictions.WebsiteRule[]>(document.Payload.GetString()!)!.Select(item => item.Domain))
            : document.Payload.GetString() ?? "";
    private static string DescribeWorkspace(JsonElement row)
    {
        if (row.ValueKind == JsonValueKind.Null || row.TryGetProperty("deleted", out var deleted) && deleted.GetInt32() != 0) return "已删除";
        var body = row.TryGetProperty("payload", out var payload) ? payload : row;
        return string.Join(" · ", body.EnumerateObject()
            .Where(field => field.Name is "name" or "title" or "notes" or "resume_note" or "summary" or "next_step" or "feeling" or "highlights" or "nextStep" or "date" or "status" or "due_date" or "mood" or "quantity")
            .Select(field => $"{field.Name switch { "status" => "状态", "due_date" => "截止", "resume_note" or "next_step" or "nextStep" => "下一步", "quantity" => "数量", _ => "" }} {field.Value}".Trim())
            .Where(value => value.Length > 0).DefaultIfEmpty(body.GetRawText()));
    }
    private void RefreshCounts()
    {
        PendingText = SignedIn ? $"待同步 {_store.PendingSyncCount} 项" : "登录后可与其他设备同步"; Notify(nameof(PendingText));
        Conflicts.Clear();
        foreach (var document in SignedIn ? _store.SyncConflicts() : [])
        {
            void Resolve(bool local)
            {
                if (Busy) return; _store.ResolveSyncConflict(document, local); _main.ReloadSyncedData(); RefreshCounts();
            }
            var local = _store.GetSyncLocalDocument(document.Kind, document.Id);
            var heading = document.Kind == "workspace" ? document.Id.Split('/')[0] switch
            {
                "todo_items" => "待办", "todo_categories" or "todo_folders" => "待办分组",
                "session_outcomes" => "专注成果", "session_todos" => "专注关联待办",
                "daily_reflections" => "心情与复盘", "habits" or "habit_checkins" => "习惯打卡",
                "focus_goals" => "目标", "school_courses" or "course_changes" => "课表",
                "calendar_plans" or "date_events" or "date_groups" => "日历事项", _ => "工作区内容"
            } : document.Kind == "project" ? "项目名称或归属" : document.Id switch
            { "restrictions.websites" => "网站白名单", "restrictions.websites.blacklist" => "网站黑名单", "restrictions.websiteMode" => "网站限制方式", _ => "计时偏好" };
            Conflicts.Add(new(heading, "本机：" + Describe(local), "云端：" + Describe(document), new(() => Resolve(true)), new(() => Resolve(false))));
        }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _resendTimer.Stop(); _cancel.Cancel(); ClearSecrets(); }
}
