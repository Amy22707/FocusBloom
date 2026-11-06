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
                if (OperatingSystem.IsMacOS() && !smoke)
                {
                    desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var menu = new NativeMenu();
                    var open = new NativeMenuItem("打开 FocusBloom / Open FocusBloom");
                    open.Click += (_, _) => { window.Show(); window.Activate(); };
                    var quit = new NativeMenuItem("退出 / Quit");
                    quit.Click += (_, _) => { if (window.PrepareToQuit()) desktop.Shutdown(); };
                    menu.Items.Add(open); menu.Items.Add(quit);
                    var icon = new TrayIcon { Menu = menu, ToolTipText = "FocusBloom" };
                    var image = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "FocusBloom.png"));
                    if (File.Exists(image)) icon.Icon = new WindowIcon(image);
                    TrayIcon.SetIcons(this, new TrayIcons { icon });
                    desktop.ShutdownRequested += (_, request) => { if (!window.PrepareToQuit()) request.Cancel = true; };
                    desktop.Exit += (_, _) => icon.Dispose();
                }
                if (smoke) window.Opened += async (_, _) =>
                {
                    try
                    {
                        await window.VerifyStartup();
                        File.WriteAllText(Path.Combine(data, "smoke-success.txt"), "FocusBloom 1.0.1");
                        window.PrepareToQuit();
                        desktop.Shutdown();
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); window.PrepareToQuit(); desktop.Shutdown(1); }
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
