namespace FocusBloom.Core.Restrictions;

public sealed record AllowedApplication(string Name, string ExecutablePath);
public enum AccessListMode { Whitelist, Blacklist }

public static class ApplicationAccess
{
    private static readonly HashSet<string> SystemApplications = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "SystemSettings", "Taskmgr", "ShellExperienceHost", "ShellHost", "StartMenuExperienceHost",
        "SearchHost", "SearchApp", "TextInputHost", "ctfmon", "LockApp", "LogonUI", "winlogon", "CredentialUIBroker",
        "SnippingTool", "ScreenClippingHost"
    };

    public static bool IsAllowed(int processId, int ownProcessId, string processName, string? executablePath,
        IEnumerable<AllowedApplication> applications, AccessListMode mode = AccessListMode.Whitelist)
    {
        if (processId == ownProcessId || SystemApplications.Contains(processName)) return true;
        var listed = executablePath is not null && applications.Any(app => app.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));
        return mode == AccessListMode.Blacklist ? !listed : listed;
    }
}

public sealed record WebsiteRule(string Domain, bool IncludeSubdomains)
{
    public string Description => IncludeSubdomains ? $"{Domain} · 包含子域名" : $"{Domain} · 仅此域名";

    public bool Matches(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        return host.Equals(Domain, StringComparison.OrdinalIgnoreCase)
            || (IncludeSubdomains && host.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryCreate(string input, bool includeSubdomains, out WebsiteRule? rule)
    {
        rule = null;
        var text = input.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || Uri.CheckHostName(uri.IdnHost) == UriHostNameType.Unknown) return false;
        rule = new WebsiteRule(uri.IdnHost.TrimEnd('.').ToLowerInvariant(), includeSubdomains);
        return true;
    }
}

public static class WebsiteAccess
{
    public static bool IsAllowed(string address, bool restricted, IEnumerable<WebsiteRule> rules, AccessListMode mode = AccessListMode.Whitelist)
    {
        if (!restricted) return true;
        if (address is "about:blank" or "about:srcdoc") return true;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return mode == AccessListMode.Blacklist;
        var listed = rules.Any(rule => rule.Matches(uri));
        return mode == AccessListMode.Blacklist ? !listed : listed;
    }
}
