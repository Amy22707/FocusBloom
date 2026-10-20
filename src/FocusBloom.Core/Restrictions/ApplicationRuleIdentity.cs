namespace FocusBloom.Core.Restrictions;

public static class ApplicationRuleIdentity
{
    public static string Key(string executablePath)
    {
        var path = executablePath.Replace('/', '\\');
        const string packageDirectory = "\\WindowsApps\\";
        var start = path.IndexOf(packageDirectory, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "path:" + path;
        start += packageDirectory.Length;
        var end = path.IndexOf('\\', start);
        if (end < 0 || end == path.Length - 1) return "path:" + path;
        var package = path[start..end].Split('_');
        if (package.Length != 5 || package[0].Length == 0 || package[4].Length == 0
            || !Version.TryParse(package[1], out var version) || version.Revision < 0
            || package[2].ToLowerInvariant() is not ("x86" or "x64" or "arm" or "arm64" or "neutral")) return "path:" + path;
        return "package:" + package[0] + "_" + package[4] + "|" + path[(end + 1)..];
    }

    public static bool Matches(string registeredPath, string executablePath) => Key(registeredPath).Equals(Key(executablePath), StringComparison.OrdinalIgnoreCase);
}
