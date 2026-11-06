using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Avalonia.Controls;
using FocusBloom.Core.Restrictions;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private sealed record MacApplication(string Id, string Name);
    private AccessListMode _applicationMode = AccessListMode.Blacklist;
    private AccessListMode _websiteMode = AccessListMode.Blacklist;
    private MacApplication[] _sessionApps = [];
    private WebsiteRule[] _sessionSites = [];
    private readonly StudyBrowser _browser = new();
    private readonly Stopwatch _interceptionDelay = Stopwatch.StartNew();
    private bool _lastRestricted;
    private Process? _keepAwake;

    private bool Restricted => _timer.RestrictionsActive;
    private static readonly HashSet<string> EssentialMacApplications = new(StringComparer.Ordinal)
    {
        "io.github.amy22707.focusbloom", "com.apple.finder", "com.apple.systempreferences", "com.apple.SystemSettings",
        "com.apple.loginwindow", "com.apple.SecurityAgent", "com.apple.controlcenter", "com.apple.notificationcenterui"
    };
    private T[] ReadRules<T>(string key) => _store.GetSetting(key) is { } text ? JsonSerializer.Deserialize<T[]>(text, Json) ?? [] : [];
    private void CaptureRules()
    {
        _applicationMode = Enum.TryParse<AccessListMode>(_store.GetSetting("mac.restrictions.applicationMode"), out var mode) ? mode : AccessListMode.Blacklist;
        _websiteMode = Enum.TryParse<AccessListMode>(_store.GetSetting("restrictions.websiteMode"), out var websites) ? websites : AccessListMode.Blacklist;
        _sessionApps = ReadRules<MacApplication>("mac.restrictions." + _applicationMode);
        _sessionSites = ReadRules<WebsiteRule>("restrictions.websites" + (_websiteMode == AccessListMode.Blacklist ? ".blacklist" : ""));
        _browser.Allowed = address => WebsiteAccess.IsAllowed(address, Restricted, _sessionSites, _websiteMode);
        _browser.Blocked = address => Report(T("本次专注期间无法访问 ", "Restricted during this focus session: ") + address);
    }
    private void EnforceRestrictions()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_lastRestricted != Restricted)
        {
            _browser.EnforceRules();
            _lastRestricted = Restricted;
        }
        if (!Restricted || _interceptionDelay.ElapsedMilliseconds < 700) return;
        var id = Cocoa.FrontApplication();
        var own = Cocoa.Text(Cocoa.Get(Cocoa.Get(Cocoa.objc_getClass("NSRunningApplication"), "currentApplication"), "bundleIdentifier"));
        if (id.Length == 0 || id == own || EssentialMacApplications.Contains(id)) return;
        var listed = _sessionApps.Any(item => item.Id == id);
        if (_applicationMode == AccessListMode.Whitelist ? listed : !listed) return;
        _interceptionDelay.Restart();
        Cocoa.HideApplication(id);
        Show(); Activate(); Cocoa.Activate();
        _timer.RegisterInterception();
        Report(T("严格专注已阻止切换到 ", "Strict focus prevented switching to ") + id);
    }
    private void UpdateKeepAwake()
    {
        var shouldRun = OperatingSystem.IsMacOS() && _timer.IsActive && _store.GetSetting("power.keepScreenOn") == "True";
        if (!shouldRun)
        {
            if (_keepAwake is not null) { if (!_keepAwake.HasExited) _keepAwake.Kill(); _keepAwake.Dispose(); _keepAwake = null; }
        }
        else if (_keepAwake is null || _keepAwake.HasExited)
        {
            _keepAwake?.Dispose();
            _keepAwake = Process.Start(new ProcessStartInfo("/usr/bin/caffeinate") { UseShellExecute = false, ArgumentList = { "-di", "-w", Environment.ProcessId.ToString() } });
        }
    }
    private void BrowserPage()
    {
        if (!OperatingSystem.IsMacOS()) { _page.Children.Add(Label(T("学习浏览器在 macOS 上使用系统 WebKit。", "The study browser uses system WebKit on macOS."))); return; }
        if (!Restricted) CaptureRules();
        var address = Input(_browser.Address == "about:blank" ? "https://www.wikipedia.org" : _browser.Address);
        address.MinWidth = 280;
        _page.Children.Add(Row(Button("‹", _browser.Back), address, Button(T("前往", "Go"), () => _browser.Navigate(Text(address))), Button(T("刷新", "Reload"), _browser.Reload),
            Button(T("返回计时", "Back to timer"), () => Navigate(0))));
        _page.Children.Add(_browser);
        _browser.Height = Math.Max(350, ClientSize.Height - 180);
    }

    private Control RestrictionSettings()
    {
        var applicationMode = Choice([T("黑名单", "Blocklist"), T("白名单", "Allowlist")], _store.GetSetting("mac.restrictions.applicationMode") == "Whitelist" ? 1 : 0);
        var websiteMode = Choice([T("黑名单", "Blocklist"), T("白名单", "Allowlist")], _store.GetSetting("restrictions.websiteMode") == "Whitelist" ? 1 : 0);
        var apps = new StackPanel { Spacing = 5 }; var sites = new StackPanel { Spacing = 5 };
        AccessListMode AppMode() => applicationMode.SelectedIndex == 0 ? AccessListMode.Blacklist : AccessListMode.Whitelist;
        string AppKey() => "mac.restrictions." + AppMode();
        string SiteKey() => "restrictions.websites" + (websiteMode.SelectedIndex == 0 ? ".blacklist" : "");
        void Render()
        {
            apps.Children.Clear(); sites.Children.Clear();
            foreach (var item in ReadRules<MacApplication>(AppKey())) apps.Children.Add(Row(Label(item.Name), Button(T("移除", "Remove"), () =>
            {
                _store.SaveSetting(AppKey(), JsonSerializer.Serialize(ReadRules<MacApplication>(AppKey()).Where(app => app.Id != item.Id), Json)); Render();
            })));
            foreach (var item in ReadRules<WebsiteRule>(SiteKey())) sites.Children.Add(Row(Label(item.Domain), Button(T("移除", "Remove"), () =>
            {
                _store.SaveSetting(SiteKey(), JsonSerializer.Serialize(ReadRules<WebsiteRule>(SiteKey()).Where(site => site.Domain != item.Domain), Json)); Render();
            })));
        }
        applicationMode.SelectionChanged += (_, _) => { _store.SaveSetting("mac.restrictions.applicationMode", AppMode().ToString()); Render(); };
        websiteMode.SelectionChanged += (_, _) => { _store.SaveSetting("restrictions.websiteMode", websiteMode.SelectedIndex == 0 ? "Blacklist" : "Whitelist"); Render(); };
        var domain = Input();
        var addSite = Button(T("添加网站", "Add website"), () =>
        {
            if (!WebsiteRule.TryCreate(Text(domain), true, out var rule)) throw new FormatException(T("请输入有效域名。", "Enter a valid domain."));
            _store.SaveSetting(SiteKey(), JsonSerializer.Serialize(ReadRules<WebsiteRule>(SiteKey()).Where(site => site.Domain != rule!.Domain).Append(rule!), Json));
            domain.Text = ""; Render();
        });
        var addApp = AsyncButton(T("添加应用", "Add application"), async () =>
        {
            var folder = await ChooseFolder(T("选择 .app 应用", "Choose an .app application"));
            if (folder is null) return;
            var metadata = Path.Combine(folder, "Contents", "Info.plist");
            if (!Directory.Exists(folder) || !File.Exists(metadata)) throw new FormatException(T("请选择应用程序的 .app 文件夹。", "Choose an application's .app bundle."));
            var info = new ProcessStartInfo("/usr/bin/plutil") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "-extract", "CFBundleIdentifier", "raw", "-o", "-", metadata } };
            using var process = Process.Start(info)!;
            var id = (await process.StandardOutput.ReadToEndAsync()).Trim(); await process.WaitForExitAsync();
            if (process.ExitCode != 0 || id.Length == 0) throw new FormatException(T("无法读取应用标识。", "Could not read the application identifier."));
            _store.SaveSetting(AppKey(), JsonSerializer.Serialize(ReadRules<MacApplication>(AppKey()).Where(app => app.Id != id).Append(new(id, Path.GetFileNameWithoutExtension(folder))), Json)); Render();
        });
        var running = AsyncButton(T("从运行中的应用选择", "Choose a running app"), async () =>
        {
            if (!OperatingSystem.IsMacOS()) return;
            var list = Cocoa.Applications().Where(app => !EssentialMacApplications.Contains(app.Id)).OrderBy(app => app.Name).ToArray();
            if (list.Length == 0) return;
            var pick = Choice(list.Select(app => app.Name));
            if (await Form(T("添加应用", "Add application"), [pick], () =>
            {
                var item = list[pick.SelectedIndex];
                _store.SaveSetting(AppKey(), JsonSerializer.Serialize(ReadRules<MacApplication>(AppKey()).Where(app => app.Id != item.Id).Append(new(item.Id, item.Name)), Json));
            })) Render();
        });
        var awake = new CheckBox { Content = T("专注时保持屏幕常亮", "Keep the display awake while focusing"), IsChecked = _store.GetSetting("power.keepScreenOn") == "True" };
        awake.IsCheckedChanged += (_, _) => { _store.SaveSetting("power.keepScreenOn", (awake.IsChecked == true).ToString()); UpdateKeepAwake(); };
        Render();
        return Card(Label(T("严格模式规则", "Strict focus rules"), 17, true),
            Label(T("专注和暂停期间限制切换到受限应用，休息和结束后解除。规则在每次开始时固定。", "Focus and pause phases prevent switching to restricted apps. Rules are captured at the start and released during breaks or when the session ends.")),
            Field(T("应用规则", "Application rules"), applicationMode), apps, Row(addApp, running), Field(T("内置浏览器网站规则", "Study browser website rules"), websiteMode), sites, Row(domain, addSite), awake);
    }
}
