using System.Windows;
using System.Windows.Threading;
using Xunit;

namespace FocusBloom.Tests;

[CollectionDefinition("Desktop UI", DisableParallelization = true)]
public sealed class DesktopUiCollection { }

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> Host = new(() =>
    {
        Dispatcher? dispatcher = null; Exception? error = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                TestDesktop.RequireIsolated();
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/FocusBloom;component/Resources/Theme.xaml", UriKind.Relative) });
                dispatcher = Dispatcher.CurrentDispatcher; ready.Set(); Dispatcher.Run();
            }
            catch (Exception ex) { error = ex; ready.Set(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if (error is not null) throw error;
        return dispatcher!;
    });
    public static void Run(Action action) { TestDesktop.RequireIsolated(); Host.Value.Invoke(action); }
}
