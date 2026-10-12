using FocusBloom.Core.Focus;
using FocusBloom.Core.Restrictions;
using Xunit;

namespace FocusBloom.Tests;

public sealed class RestrictionTests
{
    [Fact]
    public void BlacklistRestrictsOnlyListedApplicationsAndWebsites()
    {
        var applications = new[] { new AllowedApplication("浏览器", "C:\\Chrome\\chrome.exe") };
        Assert.False(ApplicationAccess.IsAllowed(20, 10, "chrome", applications[0].ExecutablePath, applications, AccessListMode.Blacklist));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "Photos", "C:\\Photos\\Photos.exe", applications, AccessListMode.Blacklist));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "Taskmgr", null, applications, AccessListMode.Blacklist));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "chrome", applications[0].ExecutablePath, Array.Empty<AllowedApplication>(), AccessListMode.Blacklist));
        var websites = new[] { new WebsiteRule("example.edu", true) };
        Assert.False(WebsiteAccess.IsAllowed("https://example.edu/", true, websites, AccessListMode.Blacklist));
        Assert.False(WebsiteAccess.IsAllowed("https://other.example.edu/", true, websites, AccessListMode.Blacklist));
        Assert.True(WebsiteAccess.IsAllowed("https://other.edu/", true, websites, AccessListMode.Blacklist));
        Assert.True(WebsiteAccess.IsAllowed("https://example.edu/", true, Array.Empty<WebsiteRule>(), AccessListMode.Blacklist));
    }

    [Fact]
    public void StrictPauseRetainsRestrictionsAndRestAndExitReleaseThem()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(new FocusOptions(Guid.NewGuid(), TimerMode.CountUp, TimeSpan.Zero,
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5), true));
        Assert.True(timer.RestrictionsActive);
        timer.Pause();
        Assert.True(timer.RestrictionsActive);
        timer.Resume();
        Assert.True(timer.RestrictionsActive);
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.End();
        Assert.Equal(FocusState.Resting, timer.State);
        Assert.False(timer.RestrictionsActive);
        Assert.True(timer.LastRecord!.Strict);
        timer.SkipRest();
        timer.Start(new FocusOptions(Guid.NewGuid(), TimerMode.Countdown, TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(5), TimeSpan.Zero, true));
        clock.Advance(TimeSpan.FromSeconds(10));
        timer.Update();
        Assert.False(timer.RestrictionsActive);
        timer.Start(new FocusOptions(Guid.NewGuid(), TimerMode.CountUp, TimeSpan.Zero,
            TimeSpan.FromMinutes(5), TimeSpan.Zero));
        Assert.False(timer.RestrictionsActive);
    }

    [Theory]
    [InlineData("https://example.edu/lesson", true)]
    [InlineData("https://library.example.edu/", true)]
    [InlineData("https://other.edu/", false)]
    [InlineData("https://example.edu.other.edu/", false)]
    public void DomainRuleChecksPageHost(string address, bool allowed)
    {
        Assert.True(WebsiteRule.TryCreate("https://example.edu/path", true, out var rule));
        Assert.Equal(allowed, WebsiteAccess.IsAllowed(address, true, new[] { rule! }));
    }

    [Fact]
    public void ExactDomainAndEmptyWhitelistKeepTheirMeaning()
    {
        var rule = new WebsiteRule("example.edu", false);
        Assert.False(WebsiteAccess.IsAllowed("https://library.example.edu/", true, new[] { rule }));
        Assert.False(WebsiteAccess.IsAllowed("https://example.edu/", true, Array.Empty<WebsiteRule>()));
        Assert.True(WebsiteAccess.IsAllowed("about:blank", true, Array.Empty<WebsiteRule>()));
        Assert.True(WebsiteAccess.IsAllowed("https://other.edu/", false, Array.Empty<WebsiteRule>()));
    }

    [Fact]
    public void BrowserIsBlockedByDefaultWhileOwnAndSystemWindowsAreAllowed()
    {
        Assert.True(ApplicationAccess.IsAllowed(10, 10, "FocusBloom", "D:\\FocusBloom.exe", Array.Empty<AllowedApplication>()));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "SystemSettings", null, Array.Empty<AllowedApplication>()));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "Taskmgr", null, Array.Empty<AllowedApplication>()));
        Assert.False(ApplicationAccess.IsAllowed(20, 10, "chrome", "C:\\Chrome\\chrome.exe", Array.Empty<AllowedApplication>()));
        Assert.True(ApplicationAccess.IsAllowed(20, 10, "chrome", "C:\\Chrome\\chrome.exe", new[] { new AllowedApplication("Chrome", "C:\\Chrome\\chrome.exe") }));
    }

    [Theory]
    [InlineData(AccessListMode.Whitelist)]
    [InlineData(AccessListMode.Blacklist)]
    public void WindowsScreenshotAndPhotoToolsAreAlwaysAllowed(AccessListMode mode)
    {
        foreach (var name in new[] { "SnippingTool", "ScreenClippingHost", "Photos", "Microsoft.Photos" })
        {
            var path = $"C:\\WindowsApps\\{name}.exe";
            Assert.True(ApplicationAccess.IsAllowed(20, 10, name, path, Array.Empty<AllowedApplication>(), mode));
            Assert.True(ApplicationAccess.IsAllowed(20, 10, name.ToLowerInvariant(), null, Array.Empty<AllowedApplication>(), mode));
            Assert.True(ApplicationAccess.IsAllowed(20, 10, name, path, new[] { new AllowedApplication(name, path) }, mode));
        }
        Assert.False(ApplicationAccess.IsAllowed(20, 10, "chrome", "C:\\Chrome\\chrome.exe",
            mode == AccessListMode.Blacklist ? new[] { new AllowedApplication("Chrome", "C:\\Chrome\\chrome.exe") } : Array.Empty<AllowedApplication>(), mode));
    }

}
