using System.IO;
using System.Text.Json;
using FocusBloom.Desktop.Presentation;
using Xunit;

namespace FocusBloom.Tests;

public sealed class ReleaseUpdateTests
{
    [Fact]
    public void UpdateRequiresNewStableVersionAndVerifiedInstallerFromTheProject()
    {
        const string installer = "https://github.com/Amy22707/FocusBloom/releases/download/v1.0.1/FocusBloom-Windows-x64.msi";
        var installed = new Version(1, 0, 0);
        using var stable = Release(installer);
        var update = Assert.IsType<ReleaseUpdate>(ReleaseUpdates.ReadRelease(stable.RootElement, installed));
        Assert.Equal(new Version(1, 0, 1), update.Version);
        Assert.Equal(installer, update.Installer.AbsoluteUri);
        Assert.Equal(32, update.Digest.Length);
        Assert.Null(ReleaseUpdates.ReadRelease(stable.RootElement, update.Version));
        using var preview = Release(installer, prerelease: true);
        Assert.Null(ReleaseUpdates.ReadRelease(preview.RootElement, installed));
        using var differentOrigin = Release("https://example.com/FocusBloom-Windows-x64.msi");
        Assert.Throws<InvalidDataException>(() => ReleaseUpdates.ReadRelease(differentOrigin.RootElement, installed));
        using var invalidDigest = Release(installer, digest: "sha256:missing");
        Assert.Throws<InvalidDataException>(() => ReleaseUpdates.ReadRelease(invalidDigest.RootElement, installed));
    }

    private static JsonDocument Release(string url, bool prerelease = false, string? digest = null) =>
        JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            tag_name = "v1.0.1", draft = false, prerelease,
            assets = new[] { new { name = "FocusBloom-Windows-x64.msi", browser_download_url = url, digest = digest ?? "sha256:" + new string('a', 64) } }
        }));
}
