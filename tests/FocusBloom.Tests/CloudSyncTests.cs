using System.Net;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Sync;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Cloud;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class CloudSyncTests
{
    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value, SyncJson.Options);
    private static SyncDocument Doc(string kind, string id, long version, object payload) => new(kind, id, version, Json(payload));
    [Fact]
    public void DownloadedBrowserRecordEntersStatisticsOnceAndPrivateSettingsStayLocal()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomSync", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "test.db"));
            store.SaveSetting("window.layout", "private-window"); store.SaveSetting("focus.media", "private-image"); store.SaveSetting("timer.mode", "CountUp");
            var request = store.PrepareSync(); Assert.DoesNotContain(request.Changes, change => change.Id is "window.layout" or "focus.media");
            var project = new SharedProject(Guid.NewGuid(), "浏览器阅读", "浏览器", "#627F90");
            var start = DateTimeOffset.UtcNow.AddMinutes(-5); var record = new FocusRecord(Guid.NewGuid(), project.Id, TimerMode.CountUp, start, start.AddMinutes(3), EndReason.Manual,
                [new(IntervalKind.Focus, start, start.AddMinutes(1)), new(IntervalKind.Pause, start.AddMinutes(1), start.AddMinutes(2)), new(IntervalKind.Focus, start.AddMinutes(2), start.AddMinutes(3))], true, InterceptionCount: null);
            var response = new SyncResponse(2, false, [], [Doc("project", project.Id.ToString(), 1, project), Doc("record", record.Id.ToString(), 2, record)], []);
            store.ApplySync(request, response); store.ApplySync(store.PrepareSync(), response);
            Assert.Equal(TimeSpan.FromMinutes(2), Assert.Single(store.GetRecords()).FocusDuration);
            Assert.Null(Assert.Single(store.GetRecords()).InterceptionCount);
            Assert.DoesNotContain(store.PrepareSync().Changes, change => change.Id == record.Id.ToString() || change.Id == project.Id.ToString());
            Assert.Equal("private-window", store.GetSetting("window.layout"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    [Fact]
    public void OfflineChangesAndEditsDuringUploadRemainPendingAndConflictsRequireAChoice()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomSync", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "test.db")); store.SaveSetting("pauseMinutes", "5");
            var sent = store.PrepareSync(); store.SaveSetting("pauseMinutes", "7");
            var accepted = sent.Changes.Select((change, index) => new SyncAccepted(change.Kind, change.Id, index + 1)).ToArray();
            store.ApplySync(sent, new(accepted.Length, false, accepted, [], []));
            var next = store.PrepareSync(); var pause = Assert.Single(next.Changes); Assert.Equal("pauseMinutes", pause.Id); Assert.Equal("7", pause.Payload.GetString());
            var remote = Doc("setting", "pauseMinutes", 20, "9");
            store.ApplySync(next, new(20, false, [], [], [remote]));
            Assert.Equal("7", store.GetSetting("pauseMinutes")); Assert.Single(store.SyncConflicts()); Assert.Empty(store.PrepareSync().Changes);
            store.ResolveSyncConflict(remote, true); var keep = Assert.Single(store.PrepareSync().Changes); Assert.Equal(20, keep.BaseVersion);
            store.ApplySync(new(20, [keep]), new(21, false, [new("setting", "pauseMinutes", 21)], [], []));
            store.SaveSetting("pauseMinutes", "8"); var pending = store.PrepareSync();
            var newer = Doc("setting", "pauseMinutes", 22, "10"); store.ApplySync(pending, new(22, false, [], [], [newer]));
            store.ResolveSyncConflict(newer, false); Assert.Equal("10", store.GetSetting("pauseMinutes")); Assert.Empty(store.PrepareSync().Changes);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    [Fact]
    public void AccountProfilesSeparateUsersAndMergeGuestOnlyWhenSelected()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomProfiles", Guid.NewGuid().ToString());
        try
        {
            var profiles = new AccountProfiles(root); var guest = new SqliteFocusStore(profiles.GuestDatabase);
            guest.SaveProject(new(Guid.NewGuid(), "访客项目", "学习", "#627F90"));
            guest.SaveSetting("restrictions.websiteMode", "Blacklist"); guest.SaveSetting("focus.media", "private");
            CloudSession Session(string id) => new(id, id + "@example.com", "access-" + id, "refresh-" + id, DateTimeOffset.UtcNow.AddHours(1));
            profiles.SignIn(Session("one"), true); var firstPath = profiles.CurrentDatabase;
            Assert.Contains(new SqliteFocusStore(firstPath).GetProjects(), p => p.Name == "访客项目");
            Assert.Equal("private", new SqliteFocusStore(firstPath).GetSetting("focus.media"));
            Assert.DoesNotContain("refresh-one", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "account.session"))));
            var reopened = new AccountProfiles(root); reopened.Load(); Assert.Equal("one", reopened.Session!.UserId);
            profiles.SignIn(Session("two"), false); Assert.NotEqual(firstPath, profiles.CurrentDatabase); Assert.Single(new SqliteFocusStore(profiles.CurrentDatabase).GetProjects());
            profiles.Save(null); Assert.Equal(profiles.GuestDatabase, profiles.CurrentDatabase); Assert.Contains(new SqliteFocusStore(profiles.CurrentDatabase).GetProjects(), p => p.Name == "访客项目");
            profiles.SignIn(Session("one"), true); Assert.Equal(2, new SqliteFocusStore(profiles.CurrentDatabase).GetProjects().Count);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
    }
    [Fact]
    public void FullGuestImportPreservesLocalContentLinksAndPreferencesWithoutExpandingCloudScope()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomFullImport", Guid.NewGuid().ToString());
        try
        {
            var guest = new SqliteFocusStore(Path.Combine(root, "guest.db"));
            var account = new SqliteFocusStore(Path.Combine(root, "account.db"));
            var project = new FocusProject(Guid.NewGuid(), "阅读", "学习", "#627F90"); guest.SaveProject(project);
            guest.SaveFolder("学习"); guest.SaveTodoCategory("学习");
            var folder = new TodoFolder(Guid.NewGuid(), "学习安排", "学习"); guest.SaveTodoFolder(folder);
            var todo = new TodoItem(Guid.NewGuid(), folder.Id, "读完一章", Notes: "笔记", PreferredProjectId: project.Id); guest.SaveTodo(todo);
            var day = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
            var start = DateTimeOffset.Now.AddMinutes(-2);
            var record = new FocusRecord(Guid.NewGuid(), project.Id, TimerMode.CountUp, start, start.AddMinutes(1), EndReason.Manual,
                [new(IntervalKind.Focus, start, start.AddMinutes(1))], false, todo.Id);
            guest.SaveRecord(record);
            guest.SaveOutcome(new(record.Id, "完成阅读", 1, "章", "继续"));
            guest.SaveDateEvent(new(Guid.NewGuid(), "考试", day, Group: "考试", TodoId: todo.Id, CoverPath: Path.Combine(root, "cover.png")));
            guest.SaveCalendarPlan(new(Guid.NewGuid(), "阅读安排", day, new TimeOnly(10, 0), TodoId: todo.Id));
            var course = new SchoolCourse(Guid.NewGuid(), "课程", 1, 1, 2); guest.SaveCourse(course);
            guest.SaveCourseChange(new(Guid.NewGuid(), course.Id, day, true));
            var habit = new Habit(Guid.NewGuid(), "阅读", [1, 2, 3, 4, 5, 6, 7], day); guest.SaveHabit(habit); guest.SetHabitCheckIn(habit.Id, day, true);
            guest.SaveGoal(new(Guid.NewGuid(), "坚持阅读", GoalMetric.CheckInDays, 10, GoalCycle.Total, day, HabitId: habit.Id));
            guest.SaveDailyReflection(new(day, 4, ["学习"], Feeling: "愉快", Photos: [Path.Combine(root, "photo.png")]));
            foreach (var key in new[] { "appearance.fonts", "focus.media", "widgets.Mood", "school.term", "window.layout" }) guest.SaveSetting(key, "guest-" + key);
            guest.SaveSetting("sync.cursor", "999"); guest.SaveSetting("focus.checkpoint", "active-guest"); guest.SaveSetting("notifications.pending", "guest-notice");
            guest.SaveSetting("account.profile", "guest-profile"); account.SaveSetting("account.profile", "account-profile");
            account.SaveSetting("sync.cursor", "12"); account.SaveSetting("appearance.fonts", "account-font");
            account.SaveProject(project); account.SaveRecord(record with { TodoId = null });
            var accountDate = new DateEvent(Guid.NewGuid(), "账号事项", day); account.SaveDateEvent(accountDate);
            account.ImportGuestData(guest);
            Assert.Equal("account-font", account.GetSetting("appearance.fonts"));
            account.ImportGuestData(guest, replacePreferences: true); account.ImportGuestData(guest, replacePreferences: true);
            Assert.Equal("guest-appearance.fonts", account.GetSetting("appearance.fonts")); Assert.Equal("guest-focus.media", account.GetSetting("focus.media"));
            Assert.Equal("12", account.GetSetting("sync.cursor")); Assert.Null(account.GetSetting("focus.checkpoint")); Assert.Null(account.GetSetting("notifications.pending"));
            Assert.Equal("account-profile", account.GetSetting("account.profile"));
            Assert.Equal(todo, Assert.Single(account.GetTodos())); Assert.Equal(todo.Id, Assert.Single(account.GetRecords()).TodoId);
            Assert.Equal("完成阅读", account.GetOutcome(record.Id)!.Summary);
            Assert.Equal(2, account.GetDateEvents().Count); Assert.Contains(account.GetDateEvents(), item => item.Id == accountDate.Id);
            Assert.Equal(todo.Id, Assert.Single(account.GetCalendarPlans()).TodoId); Assert.Contains("学习", account.GetFolders()); Assert.Contains("考试", account.GetDateGroups());
            Assert.Single(account.GetCourses()); Assert.Single(account.GetCourseChanges()); Assert.Single(account.GetGoals()); Assert.Single(account.GetHabits()); Assert.Single(account.GetHabitCheckIns());
            Assert.Equal("愉快", Assert.Single(account.GetDailyReflections()).Feeling); Assert.Equal(Path.Combine(root, "photo.png"), Assert.Single(account.GetDailyReflections()).Photos![0]);
            Assert.DoesNotContain(account.PrepareSync().Changes, item => item.Kind is not ("project" or "record" or "setting") || item.Kind == "setting" && !SyncJson.SettingKeys.Contains(item.Id));
            Assert.Single(guest.GetDateEvents()); Assert.Equal("999", guest.GetSetting("sync.cursor"));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Handler : HttpMessageHandler
    {
        public List<(string Path, string Body, string Token)> Requests = [];
        public List<HttpMethod> Methods = [];
        public Queue<string> Responses = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.Parameter ?? ""));
            Methods.Add(request.Method);
            return new(HttpStatusCode.OK) { Content = new StringContent(Responses.Dequeue(), System.Text.Encoding.UTF8, "application/json") };
        }
    }
    [Fact]
    public async Task PasswordLoginAndUpdatesUseUsernameMappingReauthenticationAndPatch()
    {
        var handler = new Handler(); handler.Responses.Enqueue("""{"sub":"one","access_token":"user-access","refresh_token":"refresh","expires_in":7200}""");
        handler.Responses.Enqueue("{}"); handler.Responses.Enqueue("{}"); handler.Responses.Enqueue("{}");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "publishable"), new(handler));
        var session = await client.SignInWithPassword("person@example.com", "Example9!");
        var login = JsonSerializer.Deserialize<JsonElement>(handler.Requests[0].Body); Assert.Equal("person@example.com", login.GetProperty("username").GetString()); Assert.Equal("Example9!", login.GetProperty("password").GetString()); Assert.Equal("", handler.Requests[0].Token);
        await client.SendPasswordCode(session); Assert.Equal("/auth/v1/user/reauthenticate", handler.Requests[1].Path); Assert.Contains("email_code", handler.Requests[1].Body); Assert.Equal("user-access", handler.Requests[1].Token);
        await client.SetPassword(session, "NewExample9!", "123456"); var create = JsonSerializer.Deserialize<JsonElement>(handler.Requests[2].Body);
        Assert.Equal(HttpMethod.Patch, handler.Methods[2]); Assert.Equal("/auth/v1/user/password", handler.Requests[2].Path); Assert.Equal("123456", create.GetProperty("verify_code").GetString()); Assert.False(create.TryGetProperty("old_password", out _));
        await client.SetPassword(session, "NextExample9!", "234567", "NewExample9!"); var change = JsonSerializer.Deserialize<JsonElement>(handler.Requests[3].Body); Assert.Equal("NewExample9!", change.GetProperty("old_password").GetString());
    }
    [Fact]
    public async Task ExpiredOrRejectedLoginIsReportedWithoutRawTokenHashMessage()
    {
        var handler = new Handler(); handler.Responses.Enqueue("""{"error":"failed_precondition","error_description":"token hash not match"}""");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler));
        var error = await Assert.ThrowsAsync<CloudBaseException>(() => client.Refresh(new("one", "one@example.com", "expired", "refresh", DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Equal("invalid_grant", error.Code); Assert.Contains("重新登录", error.Message); Assert.DoesNotContain("token hash", error.Message);
    }
    [Fact]
    public async Task ProfileUsesAuthenticatedUserEndpointAndDocumentedNicknameAvatarFields()
    {
        var handler = new Handler(); handler.Responses.Enqueue("""{"sub":"one","name":"小林","picture":"data:image/jpeg;base64,abc"}"""); handler.Responses.Enqueue("{}");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "publishable"), new(handler));
        var session = new CloudSession("one", "person@example.com", "user-access", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        var profile = await client.GetProfile(session); Assert.Equal("小林", profile.Name);
        await client.UpdateProfile(session, profile with { Name = "林同学" });
        Assert.Equal("/auth/v1/user/me", handler.Requests[0].Path); Assert.Equal("", handler.Requests[0].Body);
        var update = handler.Requests[1]; Assert.Equal("/auth/v1/user/basic/edit", update.Path); Assert.Equal("user-access", update.Token);
        var body = JsonSerializer.Deserialize<JsonElement>(update.Body); Assert.Equal("林同学", body.GetProperty("nickname").GetString()); Assert.Equal(profile.Picture, body.GetProperty("avatar_url").GetString()); Assert.False(body.TryGetProperty("user_id", out _));
    }
    [Fact]
    public async Task RecoveryUsesEmailCodeAndResetsWithoutLoginOrRegistration()
    {
        var handler = new Handler();
        handler.Responses.Enqueue("""{"verification_id":"recovery-code"}""");
        handler.Responses.Enqueue("""{"verification_token":"verified-recovery"}""");
        handler.Responses.Enqueue("{}");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "public"), new(handler));
        var verification = await client.SendRecoveryCode("person@example.com");
        await client.ResetPassword(verification, "123456", "NewExample9!");
        Assert.Equal(new[] { "/auth/v1/verification", "/auth/v1/verification/verify", "/auth/v1/reset" }, handler.Requests.Select(request => request.Path));
        var send = JsonSerializer.Deserialize<JsonElement>(handler.Requests[0].Body);
        Assert.Equal("USER", send.GetProperty("target").GetString());
        Assert.Equal("recovery", send.GetProperty("usage").GetString());
        var reset = JsonSerializer.Deserialize<JsonElement>(handler.Requests[2].Body);
        Assert.Equal("person@example.com", reset.GetProperty("email").GetString());
        Assert.Equal("verified-recovery", reset.GetProperty("verification_token").GetString());
        Assert.Equal("NewExample9!", reset.GetProperty("new_password").GetString());
        Assert.All(handler.Requests, request => Assert.Equal("", request.Token));
    }
    [Fact]
    public async Task SynchronizeCallsPostgresRpcWithOnlyCursorAndChangesInBody()
    {
        var handler = new Handler(); handler.Responses.Enqueue("""{"cursor":3,"hasMore":false,"accepted":[],"documents":[],"conflicts":[]}""");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "publishable"), new(handler));
        var result = await client.Synchronize(new("one", "person@example.com", "user-access", "refresh", DateTimeOffset.UtcNow.AddHours(1)), new(2, []));
        Assert.Equal(3, result.Cursor);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("/v1/rdb/rest/rpc/focusbloom_sync", request.Path); Assert.Equal("user-access", request.Token);
        var body = JsonSerializer.Deserialize<JsonElement>(request.Body); Assert.Equal(2, body.GetProperty("p_cursor").GetInt64());
        Assert.Equal(0, body.GetProperty("p_changes").GetArrayLength()); Assert.False(body.TryGetProperty("accessToken", out _));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmailVerificationUsesCorrectSignupOrSigninAndRefreshProtocol(bool existing)
    {
        var handler = new Handler(); handler.Responses.Enqueue($$"""{"verification_id":"verification","is_user":{{existing.ToString().ToLowerInvariant()}}} """);
        handler.Responses.Enqueue("""{"verification_token":"verified"}""");
        handler.Responses.Enqueue("""{"sub":"user-one","access_token":"access","refresh_token":"refresh","expires_in":7200}""");
        handler.Responses.Enqueue("""{"sub":"user-one","access_token":"access-two","refresh_token":"refresh-two","expires_in":7200}""");
        var client = new CloudBaseClient(new("test-env", PublishableKey: "publishable"), new(handler));
        var verification = await client.SendCode("person@example.com"); var session = await client.SignIn(verification, "123456"); session = await client.Refresh(session);
        Assert.Equal(existing ? "/auth/v1/signin" : "/auth/v1/signup", handler.Requests[2].Path);
        Assert.Equal("access-two", session.AccessToken); Assert.All(handler.Requests, request => Assert.Equal("", request.Token));
        Assert.Contains("person@example.com", handler.Requests[0].Body); Assert.Contains("123456", handler.Requests[1].Body); Assert.Contains("verified", handler.Requests[2].Body);
        Assert.Contains("refresh_token", handler.Requests[3].Body);
    }
}
