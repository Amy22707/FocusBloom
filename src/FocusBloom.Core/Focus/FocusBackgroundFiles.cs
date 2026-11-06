namespace FocusBloom.Core.Focus;

public static class FocusBackgroundFiles
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".mp4", ".wmv"
    };

    public static IEnumerable<string> InFolder(string folder) => Directory.EnumerateFiles(folder)
        .Where(path => Extensions.Contains(Path.GetExtension(path)));
}
