using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusBloom.Core.Sync;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Cloud;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class AccountUiTests
{
    private sealed class Handler : HttpMessageHandler
    {
        public bool Offline;
        public int SyncCalls;
        public string ProfileName = "专注者", ProfilePicture = "";
        public bool HasPassword;
        public int PasswordCodeCalls, PasswordUpdates;
        public bool ProfileOffline;
        public int RefreshCalls;
        public string UserId = "account-one";
        public bool RefreshDenied, ProfileUnauthorized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            if (RefreshDenied && request.RequestUri!.AbsolutePath.EndsWith("/token") || ProfileUnauthorized && request.RequestUri!.AbsolutePath.EndsWith("/user/me"))
                return new(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"login expired\"}", Encoding.UTF8, "application/json") };
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/auth/v1/verification" => new { verification_id = "verification", is_user = true },
                "/auth/v1/verification/verify" => new { verification_token = "verified" },
                "/auth/v1/signin" => new { sub = "account-one", access_token = "access", refresh_token = "refresh", expires_in = 7200 },
                "/auth/v1/token" => RefreshSession(request),
                "/auth/v1/user/me" => ProfileOffline ? throw new HttpRequestException("资料服务离线") : new { sub = "account-one", name = ProfileName, picture = ProfilePicture, has_password = HasPassword },
                "/auth/v1/user/basic/edit" => UpdateProfile(body),
                "/auth/v1/user/reauthenticate" => SendPasswordCode(),
                "/auth/v1/user/password" => UpdatePassword(body),
                _ => Synchronize(body)
            };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result, new JsonSerializerOptions(SyncJson.Options) { IgnoreReadOnlyProperties = false }), Encoding.UTF8, "application/json") };
        }
        private object RefreshSession(HttpRequestMessage request)
        {
            Assert.Null(request.Headers.Authorization); RefreshCalls++;
            return new { sub = UserId, access_token = "fresh-access", refresh_token = "fresh-refresh", expires_in = 7200 };
        }
        private object SendPasswordCode() { PasswordCodeCalls++; return new { }; }
        private object UpdatePassword(string text)
        {
            var body = JsonSerializer.Deserialize<JsonElement>(text); Assert.Equal("123456", body.GetProperty("verify_code").GetString()); HasPassword = true; PasswordUpdates++; return new { };
        }
        private object UpdateProfile(string text)
        {
            if (Offline) throw new HttpRequestException("模拟离线");
            var body = JsonSerializer.Deserialize<JsonElement>(text); ProfileName = body.GetProperty("nickname").GetString()!; ProfilePicture = body.GetProperty("avatar_url").GetString()!; return new { };
        }
        private SyncResponse Synchronize(string text)
        {
            SyncCalls++; if (Offline) throw new HttpRequestException("模拟离线");
            var body = JsonSerializer.Deserialize<JsonElement>(text);
            var request = new SyncRequest(body.GetProperty("p_cursor").GetInt64(), body.GetProperty("p_changes").Deserialize<SyncChange[]>(SyncJson.Options)!);
            return new(100, false, request.Changes.Select((item, index) => new SyncAccepted(item.Kind, item.Id, index + 1)).ToArray(), [], []);
        }
    }
    private static void Complete(AccountSettingsViewModel model)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (model.Busy && DateTime.UtcNow < deadline) Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Assert.False(model.Busy);
    }
    [Fact]
    public void LoginSwitchingAndOfflineRetryHaveVisibleStatesAndDoNotInterruptFocus()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomAccountUi", Guid.NewGuid().ToString());
            MainViewModel? main = null; Window? window = null; AccountSettingsViewModel? model = null;
            try
            {
                var profiles = new AccountProfiles(root); var store = new SqliteFocusStore(profiles.GuestDatabase); main = new(store);
                var handler = new Handler(); var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler));
                model = new(main, store, profiles, client); var view = new AccountSettingsView { DataContext = model };
                window = new Window { Content = new ScrollViewer { Content = view }, Width = 700, Height = 790 };
                window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window)); window.Show();
                model.Email = "test@example.com"; model.SendCodeCommand.Execute(null); Complete(model); Assert.Contains("验证码已发送", model.Status);
                model.Code = "123456"; main.StartCommand.Execute(null); model.SignInCommand.Execute(null); Complete(model); Assert.Contains("先结束", model.Status); Assert.Null(profiles.Session);
                main.PrepareExit(); var restart = false; model.RestartRequested += () => restart = true;
                model.SignInCommand.Execute(null); Complete(model); Assert.True(restart); Assert.Equal("account-one", profiles.Session!.UserId);
                model.Dispose(); main.Media.Dispose(); store = new(profiles.CurrentDatabase); main = new(store);
                model = new(main, store, profiles, client); view.DataContext = model;
                handler.Offline = true; model.SyncCommand.Execute(null); Complete(model); Assert.Contains("离线", model.Status); Assert.True(store.PendingSyncCount > 0); Assert.True(model.SignedIn); Assert.False(model.LoginExpired);
                handler.Offline = false; model.SyncCommand.Execute(null); Complete(model); Assert.Contains("同步完成", model.Status); Assert.Equal(0, store.PendingSyncCount);
                handler.ProfileOffline = true; model.SyncCommand.Execute(null); Complete(model); Assert.Contains("同步完成", model.Status); Assert.Contains("资料暂时", model.Status); Assert.StartsWith("上次同步：", model.LastSync); handler.ProfileOffline = false;
                foreach (var mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    Appearance.Apply(Appearance.Palettes[0], mode); window.UpdateLayout();
                    Assert.True(((StackPanel)view.FindName("SignedInPanel")).IsVisible); Assert.False(((StackPanel)view.FindName("GuestLoginPanel")).IsVisible);
                    Assert.True(((Button)view.FindName("SignOutButton")).IsVisible);
                    GoalsUiTests.Save(window, $"account-sync-{mode}.png");
                }
                var count = handler.SyncCalls; model.Dispose(); Assert.Equal(count, handler.SyncCalls);
            }
            finally { model?.Dispose(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void ProfileEditingUpdatesSidebarAndCachesAvatarAcrossRestartAndOffline()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomProfileUi", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null; AccountSettingsViewModel? model = null;
            try
            {
                var profiles = new AccountProfiles(root); profiles.Save(new("one", "test@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1)));
                var store = new SqliteFocusStore(profiles.CurrentDatabase); store.SaveSetting("account.profile", JsonSerializer.Serialize(new CloudUserProfile("原昵称"), SyncJson.Options));
                var handler = new Handler { UserId = "one" }; var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler));
                main = new(store); window = new(main, store, Path.Combine(root, "browser"), profiles, client); window.Show(); window.UpdateLayout();
                var tabs = (TabControl)window.FindName("MainTabs"); var footer = (Button)tabs.Template.FindName("NavigationFooter", tabs); Assert.Same(window.Accounts, footer.DataContext); Assert.Equal("原昵称", window.Accounts!.DisplayName);
                footer.Command.Execute(null); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(window.Accounts.EditingProfile); Assert.True(((Expander)window.FindName("AccountSettingsSection")).IsExpanded);
                window.NavigationCollapsed = true; window.UpdateLayout(); Assert.True(footer.IsVisible); Assert.True(footer.ActualWidth >= 32);
                model = window.Accounts;
                model.EditProfileCommand.Execute(null); model.ProfileName = "新的昵称";
                var pixels = new byte[320 * 180 * 4]; for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 170; pixels[i + 3] = 255; }
                var bitmap = BitmapSource.Create(320, 180, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); var imagePath = Path.Combine(root, "avatar.png"); using (var output = File.Create(imagePath)) encoder.Save(output);
                model.SetAvatarFile(imagePath); model.SaveProfileCommand.Execute(null); Complete(model);
                Assert.Equal("新的昵称", model.DisplayName); Assert.NotNull(model.Avatar); Assert.False(model.EditingProfile); Assert.Equal("新的昵称", handler.ProfileName);
                Assert.Equal(1, handler.RefreshCalls); Assert.Equal("fresh-access", profiles.Session!.AccessToken);
                Assert.Equal("新的昵称", ((AccountSettingsViewModel)footer.DataContext).DisplayName);
                Assert.StartsWith("data:image/jpeg;base64,", handler.ProfilePicture); Assert.True(handler.ProfilePicture.Length < 200 * 1024);
                Assert.Equal(256, ((BitmapSource)model.Avatar!).PixelWidth); Assert.Equal(256, ((BitmapSource)model.Avatar!).PixelHeight);
                window.NavigationCollapsed = false; window.UpdateLayout(); GoalsUiTests.Save(window, "account-profile-sidebar.png");
                model.EditProfileCommand.Execute(null); model.ProfileName = "离线更名"; handler.Offline = true; model.SaveProfileCommand.Execute(null); Complete(model);
                Assert.True(model.EditingProfile); Assert.Equal("新的昵称", model.DisplayName); Assert.Contains("离线", model.Status);
                model.Dispose(); model = new(main, store, profiles, new(new("test-env", PublishableKey: "public"), new(handler))); Assert.Equal("新的昵称", model.DisplayName); Assert.NotNull(model.Avatar);
                var guest = new SqliteFocusStore(profiles.GuestDatabase); var guestProfiles = new AccountProfiles(root); using var guestModel = new AccountSettingsViewModel(main, guest, guestProfiles); Assert.Equal("未登录", guestModel.DisplayName); Assert.Null(guestModel.Avatar);
            }
            finally { model?.Dispose(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void PasswordSetupValidatesConfirmationClearsInputsAndSupportsPasswordLogin()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomPasswordUi", Guid.NewGuid().ToString()); MainViewModel? main = null; AccountSettingsViewModel? model = null; Window? window = null;
            try
            {
                var profiles = new AccountProfiles(root); profiles.Save(new("account-one", "test@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)));
                var store = new SqliteFocusStore(profiles.CurrentDatabase); main = new(store); var handler = new Handler(); var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler));
                model = new(main, store, profiles, client); var view = new AccountSettingsView { DataContext = model }; window = new Window { Content = new ScrollViewer { Content = view }, Width = 700, Height = 790 }; window.Show();
                ((Expander)view.FindName("PasswordSection")).IsExpanded = true;
                model.SendPasswordCodeCommand.Execute(null); Complete(model); Assert.Equal(1, handler.PasswordCodeCalls); Assert.False(model.SendPasswordCodeCommand.CanExecute(null));
                var password = (PasswordBox)view.FindName("NewPasswordInput"); var confirm = (PasswordBox)view.FindName("ConfirmPasswordInput");
                password.Password = "Example9!"; confirm.Password = "Other9!"; model.PasswordCode = "123456"; model.SetPasswordCommand.Execute(null); Complete(model); Assert.Contains("不一致", model.Status); Assert.Equal(0, handler.PasswordUpdates);
                confirm.Password = "Example9!"; model.SetPasswordCommand.Execute(null); Complete(model); Assert.Equal(1, handler.PasswordUpdates); Assert.True(model.HasPassword); Assert.Equal("", password.Password); Assert.Equal("", confirm.Password);
                Assert.DoesNotContain("Example9!", store.GetSetting("account.profile")!); GoalsUiTests.Save(window, "account-password.png");
                model.Dispose(); profiles.Save(null); var guest = new SqliteFocusStore(profiles.GuestDatabase); model = new(main, guest, profiles, client); view.DataContext = model; model.UsePasswordLogin = true; model.Email = "test@example.com";
                ((PasswordBox)view.FindName("LoginPasswordInput")).Password = "Example9!"; var restarted = false; model.RestartRequested += () => restarted = true;
                model.PasswordLoginCommand.Execute(null); Complete(model); Assert.True(restarted); Assert.Equal("account-one", profiles.Session!.UserId); Assert.Equal("", ((PasswordBox)view.FindName("LoginPasswordInput")).Password);
            }
            finally { model?.Dispose(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void ExpiredAccountKeepsSelectedLocalDatabaseAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomExpiredProfile", Guid.NewGuid().ToString());
        try
        {
            var profiles = new AccountProfiles(root); var session = new CloudSession("one", "one@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)); profiles.SignIn(session, false);
            var path = profiles.CurrentDatabase; var store = new SqliteFocusStore(path); store.SaveSetting("local.note", "保留本机内容");
            profiles.ExpireSession(); Assert.False(profiles.SignedIn); Assert.True(profiles.LoginExpired); Assert.Equal(path, profiles.CurrentDatabase); Assert.Equal("", profiles.Session!.AccessToken); Assert.Equal("", profiles.Session.RefreshToken);
            var reopened = new AccountProfiles(root); reopened.Load(); Assert.True(reopened.LoginExpired); Assert.Equal(path, reopened.CurrentDatabase); Assert.Equal("保留本机内容", new SqliteFocusStore(reopened.CurrentDatabase).GetSetting("local.note"));
            reopened.SignIn(session, false); Assert.True(reopened.SignedIn); Assert.False(reopened.LoginExpired); Assert.Equal(path, reopened.CurrentDatabase);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void InvalidLoginShowsBannerAndReloginWithoutInterruptingFocusOrChangingLocalData(bool rejectedRefresh)
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomExpiredUi", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var profiles = new AccountProfiles(root); profiles.Save(new("account-one", "test@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddMinutes(rejectedRefresh ? -1 : 60)));
                var store = new SqliteFocusStore(profiles.CurrentDatabase); var originalPath = store.DatabasePath;
                store.SaveDateEvent(new(Guid.NewGuid(), "本机事项", DateOnly.FromDateTime(DateTime.Today)));
                store.SaveSetting("appearance.mode", (rejectedRefresh ? AppearanceMode.Light : AppearanceMode.Dark).ToString());
                var handler = new Handler { RefreshDenied = rejectedRefresh, ProfileUnauthorized = !rejectedRefresh };
                var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler)); main = new(store); window = new(main, store, Path.Combine(root, "browser"), profiles, client); window.Show();
                var model = window.Accounts!; main.StartCommand.Execute(null); var focusing = main.State;
                var restart = false; model.RestartRequested += () => restart = true; model.SyncCommand.Execute(null); Complete(model); window.UpdateLayout();
                Assert.True(model.LoginExpired); Assert.False(model.SignedIn); Assert.Equal("未登录", model.DisplayName); Assert.Equal("请重新登录", model.AccountSubtitle); Assert.Null(model.Avatar);
                Assert.Equal(focusing, main.State); Assert.False(restart); Assert.Equal(originalPath, profiles.CurrentDatabase); Assert.Single(store.GetDateEvents());
                Assert.True(((Border)window.FindName("LoginExpiredBanner")).IsVisible); Assert.False(model.SyncCommand.CanExecute(null));
                model.OpenAccountCommand.Execute(null); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var view = (AccountSettingsView)window.FindName("AccountSettings"); Assert.True(((StackPanel)view.FindName("GuestLoginPanel")).IsVisible); Assert.True(((TextBlock)view.FindName("ExpiredLoginNotice")).IsVisible); Assert.False(((StackPanel)view.FindName("SignedInPanel")).IsVisible);
                GoalsUiTests.Save(window, rejectedRefresh ? "account-expired-light.png" : "account-expired-dark.png");
                handler.RefreshDenied = handler.ProfileUnauthorized = false; main.PrepareExit(); model.LoginPassword = "Example9!"; model.PasswordLoginCommand.Execute(null); Complete(model);
                Assert.True(restart); Assert.True(model.SignedIn); Assert.False(model.LoginExpired); Assert.Equal(originalPath, profiles.CurrentDatabase); Assert.Single(store.GetDateEvents());
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void ImportLocalDataBacksUpPreferencesAndRequestsRestartWithoutNetwork()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomImportUi", Guid.NewGuid().ToString()); MainViewModel? main = null; AccountSettingsViewModel? model = null;
            try
            {
                var profiles = new AccountProfiles(root); var guest = new SqliteFocusStore(profiles.GuestDatabase);
                guest.SaveSetting("appearance.fonts", "original-font"); guest.SaveSetting("focus.media", "original-background");
                profiles.Save(new("account-one", "test@example.com", "access", "refresh", DateTimeOffset.UtcNow.AddHours(1)));
                var store = new SqliteFocusStore(profiles.CurrentDatabase); main = new(store);
                store.SaveSetting("appearance.fonts", "account-font"); var handler = new Handler();
                model = new(main, store, profiles, new(new("test-env", PublishableKey: "public"), new(handler)));
                var restart = false; model.RestartRequested += () => restart = true;
                main.StartCommand.Execute(null); model.ImportGuestCommand.Execute(null); Complete(model); Assert.Contains("先结束", model.Status); Assert.False(restart);
                main.PrepareExit(); model.ImportGuestCommand.Execute(null); Complete(model);
                Assert.True(restart); Assert.Equal("account-font", store.GetSetting("appearance.fonts"));
                store.SaveSetting("window.layout", "closing-layout"); model.CompleteLocalImport();
                Assert.Equal("original-font", store.GetSetting("appearance.fonts")); Assert.Equal("original-background", store.GetSetting("focus.media")); Assert.Equal(0, handler.SyncCalls);
                var backup = Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "backups"), "before-local-import-*.db"));
                Assert.Equal("account-font", new SqliteFocusStore(backup).GetSetting("appearance.fonts"));
            }
            finally { model?.Dispose(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    [Fact]
    public void AccountSettingsAreSearchableInsideExistingSettingsPage()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomAccountSearch", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); main = new(store); window = new(main, store, Path.Combine(root, "browser")); window.Show();
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null);
                var search = (TextBox)window.FindName("SettingsSearchInput"); search.Text = "邮箱";
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(((Expander)window.FindName("AccountSettingsSection")).IsExpanded);
                Assert.True(((Expander)window.FindName("AccountSettingsSection")).IsVisible);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
}
