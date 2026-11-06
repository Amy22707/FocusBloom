using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;

namespace FocusBloom.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<App>()
        .UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(args);
}

internal sealed class App : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var index = Array.IndexOf(args, "--data-dir");
            var smoke = args.Contains("--smoke-test");
            if ((smoke && index < 0) || (index >= 0 && index + 1 >= args.Length))
                throw new ArgumentException("Provide an isolated --data-dir for the smoke test.");
            var data = index >= 0 ? Path.GetFullPath(args[index + 1]) : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "FocusBloom");
            if (smoke && Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any())
                throw new ArgumentException("The smoke test needs an empty data directory.");
            try
            {
                Directory.CreateDirectory(data);
                var window = new MainWindow(data);
                desktop.MainWindow = window;
                if (smoke) window.Opened += async (_, _) =>
                {
                    try { await window.VerifyStartup(); desktop.Shutdown(); }
                    catch (Exception error) { Console.Error.WriteLine(error); desktop.Shutdown(1); }
                };
            }
            catch (Exception error) when (!smoke)
            {
                try { File.WriteAllText(Path.Combine(data, "startup.log"), error.ToString()); }
                catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
                desktop.MainWindow = new Window
                {
                    Title = "FocusBloom", Width = 510, SizeToContent = SizeToContent.Height,
                    Content = new TextBlock
                    {
                        Text = "无法启动一刻专注 / FocusBloom could not start\n\n" + error.Message,
                        Margin = new Thickness(24), TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
