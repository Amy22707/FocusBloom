using System.IO;
using System.Text.Json;
using FocusBloom.Core.Restrictions;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class ApplicationIdentityTests
{
    private const string Old = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1007.2314.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";
    private const string Updated = @"C:\Program Files\WindowsApps\OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe";

    [Fact]
    public void StoreUpdatesKeepWhitelistAndBlacklistRulesWithoutAccessToTheOldExecutable()
    {
        var apps = new[] { new AllowedApplication("Codex", Old) };
        Assert.True(ApplicationRuleIdentity.Matches(Old, Updated));
        Assert.True(ApplicationAccess.IsAllowed(2, 1, "ChatGPT", Updated, apps));
        Assert.False(ApplicationAccess.IsAllowed(2, 1, "ChatGPT", Updated, apps, AccessListMode.Blacklist));
        Assert.True(ApplicationRuleIdentity.Matches(Old, @"D:\WindowsApps\OpenAI.Codex_26.1019.1000.0_arm64__2p2nqsd0c76g0\app\ChatGPT.exe"));
        Assert.True(ApplicationRuleIdentity.Matches(Old, Updated.Replace('\\', '/').ToUpperInvariant()));
    }

    [Theory]
    [InlineData(@"C:\Downloads\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.ChatGPT_26.1013.4894.0_x64__2p2nqsd0c76g0\app\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.1013.4894.0_x64__differentpublisher\app\ChatGPT.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_26.1013.4894.0_x64__2p2nqsd0c76g0\tools\ChatGPT.exe")]
    public void OtherApplicationsAndExecutablesRemainSeparate(string other)
    {
        Assert.False(ApplicationRuleIdentity.Matches(Old, other));
        var apps = new[] { new AllowedApplication("Codex", Old) };
        Assert.False(ApplicationAccess.IsAllowed(2, 1, "ChatGPT", other, apps));
        Assert.True(ApplicationAccess.IsAllowed(2, 1, "ChatGPT", other, apps, AccessListMode.Blacklist));
        Assert.True(ApplicationRuleIdentity.Matches(@"C:\Apps\editor.exe", @"c:/apps/EDITOR.EXE"));
        Assert.False(ApplicationRuleIdentity.Matches(@"C:\Apps\editor.exe", @"C:\Other\editor.exe"));
    }

    [Fact]
    public void ExistingVersionEntriesDisplayOnceAndReaddingRefreshesTheSavedPathInBothLists()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomAppIdentity", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); var oldRules = JsonSerializer.Serialize(new[] { new AllowedApplication("Codex", Old), new AllowedApplication("Codex", Updated) });
            store.SaveSetting("restrictions.applications", oldRules); store.SaveSetting("restrictions.applications.blacklist", oldRules);
            var model = new RestrictionSettingsViewModel(store, _ => { });
            foreach (var mode in model.ListModes)
            {
                model.ApplicationMode = mode; Assert.Single(model.Applications);
                model.AddApplication(new("Codex", Updated)); Assert.Single(model.Applications); Assert.Equal(Updated, model.SelectedApplication!.ExecutablePath);
                model.AddApplication(new("普通编辑器", @"C:\Apps\editor.exe")); Assert.Equal(2, model.Applications.Count);
            }
            var reopened = new RestrictionSettingsViewModel(new SqliteFocusStore(Path.Combine(directory, "test.db")), _ => { });
            foreach (var mode in reopened.ListModes) { reopened.ApplicationMode = mode; Assert.Equal(2, reopened.Applications.Count); Assert.Equal(Updated, reopened.Applications[0].ExecutablePath); }
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}

