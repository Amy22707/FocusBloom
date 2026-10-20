using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class DateAlignmentTests
{
    [Fact]
    public void DateInputsAndWeekHeadersStayCenteredAcrossPagesAndThemes()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomDateAlignment", Guid.NewGuid().ToString()); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); main = new(store); window = new(main, store, Path.Combine(root, "browser")); window.Show(); Flush();
                foreach (var dark in new[] { false, true })
                {
                    main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == (dark ? AppearanceMode.Dark : AppearanceMode.Light));
                    foreach (var key in new[] { "goals", "reflection" })
                    {
                        main.Navigation.Items.Single(item => item.Key == key).OpenCommand.Execute(null); Flush();
                        var fields = Children<DatePickerTextBox>(window).Where(field => field.IsVisible).ToArray(); Assert.NotEmpty(fields);
                        Assert.All(fields, field => { Assert.Equal(TextAlignment.Center, field.TextAlignment); Assert.Equal(VerticalAlignment.Center, field.VerticalContentAlignment); });
                    }
                    main.Navigation.Items.Single(item => item.Key == "calendar").OpenCommand.Execute(null); Flush(); var page = Children<CalendarView>(window).First(view => ReferenceEquals(view.DataContext, main.Calendar));
                    foreach (var mode in new[] { "周", "课程表" })
                    {
                        main.Calendar.Mode = mode; Flush(); var header = (ContentControl)page.FindName("CalendarDayHeader"); var labels = Children<TextBlock>(header).ToArray(); Assert.Equal(7, labels.Length);
                        Assert.All(labels, label => { Assert.Equal(TextAlignment.Center, label.TextAlignment); Assert.Equal(VerticalAlignment.Center, label.VerticalAlignment); });
                    }
                    GoalsUiTests.Save(window, "date-alignment-" + (dark ? "dark" : "light") + ".png");
                }
                var editor = new DateEventEditorWindow(main) { Owner = window }; editor.Show(); Flush(); Assert.All(Children<DatePickerTextBox>(editor), field => Assert.Equal(TextAlignment.Center, field.TextAlignment)); editor.Close();
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
