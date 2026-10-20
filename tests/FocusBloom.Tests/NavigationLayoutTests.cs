using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Desktop;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class NavigationLayoutTests
{
    [Fact]
    public void CollapsedIconsFitAtShortAndTallWindowHeights()
    {
        WpfTestHost.Run(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomCollapsedNavigation", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db")); main = new(store); window = new(main, store, Path.Combine(directory, "browser")); window.Show(); window.NavigationCollapsed = true; Flush();
                var tabs = (TabControl)window.FindName("MainTabs"); var viewport = (ScrollViewer)tabs.Template.FindName("NavigationViewport", tabs); var items = (ItemsControl)tabs.Template.FindName("NavigationItems", tabs);
                foreach (var height in new[] { 560d, 900d })
                {
                    window.Height = height; Flush(); window.UpdateLayout();
                    Assert.Equal(Visibility.Collapsed, viewport.ComputedVerticalScrollBarVisibility);
                    var presenter = (FrameworkElement)viewport.Template.FindName("PART_ScrollContentPresenter", viewport);
                    foreach (var button in Descendants<Button>(items).ToArray())
                    {
                        button.BringIntoView(); Flush(); var labels = Descendants<TextBlock>(button).ToArray(); var icon = labels.Single(label => label.FontFamily.Source == "Segoe MDL2 Assets");
                        var natural = new FormattedText(icon.Text, CultureInfo.CurrentUICulture, icon.FlowDirection, new Typeface(icon.FontFamily, icon.FontStyle, icon.FontWeight, icon.FontStretch), icon.FontSize, icon.Foreground, VisualTreeHelper.GetDpi(icon).PixelsPerDip);
                        Assert.True(button.ActualWidth - button.Padding.Left - button.Padding.Right >= natural.WidthIncludingTrailingWhitespace + icon.Margin.Left + icon.Margin.Right);
                        Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment); Assert.Equal(Visibility.Collapsed, labels.Single(label => label != icon).Visibility);
                        var rect = icon.TransformToAncestor(presenter).TransformBounds(new Rect(icon.RenderSize)); Assert.True(rect.Left >= -.5 && rect.Right <= presenter.ActualWidth + .5); Assert.True(rect.Top >= -.5 && rect.Bottom <= presenter.ActualHeight + .5);
                    }
                    viewport.ScrollToTop(); Flush(); GoalsUiTests.Save(window, $"navigation-collapsed-{height:0}.png");
                }
                window.NavigationCollapsed = false; window.Height = 560; Flush(); Assert.All(Descendants<Button>(items), button => Assert.Equal(HorizontalAlignment.Left, button.HorizontalContentAlignment));
                window.NavigationCollapsed = true; Flush(); Assert.Equal("True", store.GetSetting("ui.navigation.collapsed"));
                var settings = main.Navigation.Items.Single(item => item.Key == "settings"); settings.OpenCommand.Execute(null); Flush(); Assert.Same(window.FindName("PreferencesTab"), tabs.SelectedItem); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
