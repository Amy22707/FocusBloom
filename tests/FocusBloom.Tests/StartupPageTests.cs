using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FocusBloom.Desktop;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class StartupPageTests
{
    [Fact]
    public void FirstShowDisplaysInitialPageWithoutSwitchingNavigation()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStartup", Guid.NewGuid().ToString());
            MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db"));
                main = new(store); window = new(main, store, Path.Combine(directory, "browser")); window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var tabs = (TabControl)window.FindName("MainTabs"); var first = (TabItem)window.FindName("FocusTab");
                var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
                Assert.True(ReferenceEquals(first.Content, host.Content), $"初次显示应呈现专注页，实际SelectedIndex={tabs.SelectedIndex}, SelectedItem={tabs.SelectedItem}, SelectedContent={tabs.SelectedContent}, HostContent={host.Content}");
                Assert.True(((FrameworkElement)first.Content).IsVisible); Assert.True(((FrameworkElement)first.Content).ActualWidth > 100);
            }
            finally { window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    [Fact]
    public void NewNavigationPagesPrecedeSettingsAndHiddenFocusStartsOnVisiblePage()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomStartupNavigation", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db"));
                store.SaveSetting("ui.navigation.items", """[{"Key":"focus","Visible":false},{"Key":"todos","Visible":true},{"Key":"projects","Visible":true},{"Key":"calendar","Visible":true},{"Key":"dates","Visible":true},{"Key":"records","Visible":true},{"Key":"settings","Visible":true}]""");
                main = new(store); Assert.Equal("settings", main.Navigation.Items[^1].Key); Assert.Equal("reflection", main.Navigation.Items[^2].Key); Assert.Equal("goals", main.Navigation.Items[^3].Key); Assert.Equal("todos", main.Navigation.VisibleItems[0].Key);
                window = new(main, store, Path.Combine(directory, "browser")); window.Show(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
                var tabs = (TabControl)window.FindName("MainTabs"); var expected = (TabItem)window.FindName("TodoTab"); var host = (ContentPresenter)tabs.Template.FindName("PART_SelectedContentHost", tabs);
                Assert.Same(expected, tabs.SelectedItem); Assert.Same(expected.Content, host.Content); Assert.True(((FrameworkElement)expected.Content).IsVisible);
                main.Navigation.Items.Single(item => item.Key == "records").OpenCommand.Execute(null); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Same(window.FindName("RecordsTab"), tabs.SelectedItem); Assert.NotNull(host.Content);
                Assert.Equal("settings", new NavigationViewModel(new SqliteFocusStore(Path.Combine(directory, "new.db"))).Items[^1].Key);
            }
            finally { window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
}
