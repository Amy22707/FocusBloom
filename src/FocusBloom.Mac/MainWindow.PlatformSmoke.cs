using System.Net;
using System.Net.Sockets;
using System.Text;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Restrictions;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private async Task VerifyMacFeatures()
    {
        foreach (var kind in _widgetKeys) SaveWidget(kind, ReadWidget(kind) with { Enabled = true });
        RefreshWidgets(true);
        await Task.Delay(200);
        if (_widgets.Count != 7 || _widgets.Values.Any(window => !window.IsVisible))
            throw new InvalidOperationException("Desktop widgets did not open.");
        foreach (var kind in _widgetKeys)
        {
            SaveWidget(kind, ReadWidget(kind) with { Enabled = false });
            CloseWidget(kind);
        }
        if (!OperatingSystem.IsMacOS()) return;
        var running = Cocoa.Applications();
        if (running.Count == 0 || Cocoa.FrontApplication().Length == 0)
            throw new InvalidOperationException("Could not read the front application through AppKit.");
        _store.SaveSetting("mac.restrictions.applicationMode", "Blacklist");
        _store.SaveSetting("mac.restrictions.Blacklist", "[]");
        _store.SaveSetting("restrictions.websiteMode", "Blacklist");
        _store.SaveSetting("restrictions.websites.blacklist", "[{\"Domain\":\"example.com\",\"IncludeSubdomains\":true}]");
        CaptureRules();
        _timer.Start(new(_store.GetProjects()[0].Id, TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero, Strict: true));
        if (_browser.Allowed("https://example.com") || !_browser.Allowed("https://www.wikipedia.org"))
            throw new InvalidOperationException("Browser rules were not captured for the session.");
        _store.SaveSetting("restrictions.websites.blacklist", "[]");
        if (_browser.Allowed("https://example.com")) throw new InvalidOperationException("Editing saved rules changed the current session.");
        _timer.Pause();
        if (_browser.Allowed("https://example.com")) throw new InvalidOperationException("Pause released browser restrictions.");
        _timer.End(takeRest: false);
        if (!_browser.Allowed("https://example.com")) throw new InvalidOperationException("End did not release browser restrictions.");
        Navigate(9);
        await Task.Delay(300);
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start(); var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/"); listener.Start();
        var context = listener.GetContextAsync();
        _browser.Navigate($"http://127.0.0.1:{port}/");
        var request = await context.WaitAsync(TimeSpan.FromSeconds(15));
        var html = Encoding.UTF8.GetBytes("<html><body><h1>FocusBloom study browser</h1></body></html>");
        request.Response.ContentType = "text/html";
        await request.Response.OutputStream.WriteAsync(html);
        request.Response.Close();
        await Task.Delay(300);
        if (!_browser.Address.StartsWith($"http://127.0.0.1:{port}/", StringComparison.Ordinal))
            throw new InvalidOperationException("WebKit did not load the permitted page.");
        Console.WriteLine("MAC_FEATURES_OK: AppKit, WebKit navigation, restriction snapshots and seven desktop widgets");
    }
}
