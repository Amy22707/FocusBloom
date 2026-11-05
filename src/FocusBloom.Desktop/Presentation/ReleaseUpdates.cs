using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FocusBloom.Desktop.Presentation;

internal sealed record ReleaseUpdate(Version Version, Uri Installer, byte[] Digest);

internal static class ReleaseUpdates
{
    internal const string ReleasesPage = "https://github.com/Amy22707/FocusBloom/releases";
    private const string LatestApi = "https://api.github.com/repos/Amy22707/FocusBloom/releases/latest";
    private const string InstallerName = "FocusBloom-Windows-x64.msi";
    private static readonly HttpClient Client = new();

    internal static Version CurrentVersion
    {
        get
        {
            var version = typeof(ReleaseUpdates).Assembly.GetName().Version!;
            return new Version(version.Major, version.Minor, version.Build);
        }
    }

    internal static bool IsInstalled => string.Equals(Environment.ProcessPath, Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "FocusBloom", "FocusBloom.exe"),
        StringComparison.OrdinalIgnoreCase);

    internal static async Task<ReleaseUpdate?> CheckAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.UserAgent.ParseAdd("FocusBloom/" + CurrentVersion);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return ReadRelease(json.RootElement, CurrentVersion);
    }

    internal static ReleaseUpdate? ReadRelease(JsonElement release, Version installedVersion)
    {
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) ||
            version.Build < 0 || version.Revision >= 0 || release.GetProperty("draft").GetBoolean() ||
            release.GetProperty("prerelease").GetBoolean() || version <= installedVersion)
            return null;

        var expected = new Uri($"{ReleasesPage}/download/{tag}/{InstallerName}");
        var asset = release.GetProperty("assets").EnumerateArray().FirstOrDefault(item =>
            item.GetProperty("name").GetString() == InstallerName &&
            item.GetProperty("browser_download_url").GetString() == expected.AbsoluteUri);
        if (asset.ValueKind == JsonValueKind.Undefined ||
            !asset.TryGetProperty("digest", out var digestProperty) ||
            digestProperty.GetString() is not { Length: 71 } digest ||
            !digest.StartsWith("sha256:", StringComparison.Ordinal) || !digest[7..].All(Uri.IsHexDigit))
            throw new InvalidDataException(UiText.Get("此版本缺少可验证的 Windows 安装包。"));

        return new(version, expected, Convert.FromHexString(digest[7..]));
    }

    internal static async Task<string> DownloadAsync(ReleaseUpdate update, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusBloom", "updates");
        Directory.CreateDirectory(directory);
        var installer = Path.Combine(directory, $"FocusBloom-{update.Version}-x64.msi");
        var temporary = Path.Combine(directory, Guid.NewGuid() + ".download");
        try
        {
            using var response = await Client.GetAsync(update.Installer, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, useAsync: true))
            {
                var buffer = new byte[65536];
                int count;
                while ((count = await source.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    hash.AppendData(buffer, 0, count);
                    await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }
            if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), update.Digest))
                throw new InvalidDataException(UiText.Get("安装包校验失败，请重新下载。"));
            File.Move(temporary, installer, overwrite: true);
            return installer;
        }
        finally { File.Delete(temporary); }
    }
}
