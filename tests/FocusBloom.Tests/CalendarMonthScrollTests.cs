using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class CalendarMonthScrollTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllDailyEntriesRemainAccessibleAndWheelScrollsInsideTheDate(bool focusOnly)
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomMonthScroll", Guid.NewGuid().ToString()); Window? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = new DateOnly(2026, 10, 22);
                var project = store.GetProjects().First();
                for (var index = 0; index < 10; index++)
                {
                    if (focusOnly)
                    {
                        var start = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)).AddMinutes(index * 30), TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
                        store.SaveRecord(new(Guid.NewGuid(), project.Id, TimerMode.CountUp, start, start.AddMinutes(20), EndReason.Manual,
                            new[] { new FocusInterval(IntervalKind.Focus, start, start.AddMinutes(20)) }));
                    }
                    else store.SaveDateEvent(new(Guid.NewGuid(), $"当天事项 {index + 1}", day, new TimeOnly(8 + index, 0)));
                }
                var model = new CalendarViewModel(store, focusOnly) { Mode = "月", Date = day };
                var calendar = new CalendarView { DataContext = model };
                window = new Window { Content = calendar, Width = 1040, Height = 760 };
                window.SetResourceReference(FrameworkElement.StyleProperty, typeof(Window)); window.Show(); Flush();
                CalendarEntry? opened = null; calendar.EntryRequested += entry => opened = entry;
                foreach (var dark in new[] { false, true })
                foreach (var small in new[] { false, true })
                {
                    Appearance.Apply(Appearance.Palettes[0], dark ? AppearanceMode.Dark : AppearanceMode.Light);
                    window.Width = small ? 900 : 1040; window.Height = small ? 560 : 760; calendar.Render(); Flush();
                    var scroll = Descendants<ScrollViewer>(calendar).Single(item => item.Name == "MonthDayEntries" && Equals(item.Tag, day));
                    var items = ((StackPanel)scroll.Content).Children.OfType<Button>().ToArray(); Assert.Equal(10, items.Length);
                    Assert.True(scroll.ViewportHeight >= 48); Assert.True(scroll.ScrollableHeight > 0);
                    Assert.DoesNotContain(Descendants<Button>(calendar), button => button.Content is string text && text.StartsWith("＋"));
                    var outer = (ScrollViewer)calendar.FindName("CalendarViewport"); var outerOffset = outer.VerticalOffset;
                    var dateHeading = ((Grid)scroll.Parent).Children[0]; var headingY = ((FrameworkElement)dateHeading).TransformToAncestor(calendar).Transform(new Point()).Y;
                    var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
                    scroll.RaiseEvent(wheel); Flush();
                    Assert.True(wheel.Handled); Assert.InRange(scroll.VerticalOffset, 23, 25); Assert.Equal(outerOffset, outer.VerticalOffset);
                    Assert.Equal(headingY, ((FrameworkElement)dateHeading).TransformToAncestor(calendar).Transform(new Point()).Y);
                    scroll.ScrollToEnd(); Flush();
                    Assert.True(items[^1].TransformToAncestor(scroll).TransformBounds(new Rect(items[^1].RenderSize)).Bottom <= scroll.ViewportHeight + 1);
                    items[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(((CalendarEntry)items[^1].Tag).Id, opened!.Id);
                    Assert.Equal("月", model.Mode); Assert.Equal(day, model.Date);
                    GoalsUiTests.Save(window, $"calendar-month-scroll-{(focusOnly ? "focus" : "events")}-{(dark ? "dark" : "light")}-{(small ? "small" : "large")}.png");
                }
            }
            finally { window?.Close(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Descendants<T>(child)) yield return nested; } }
}
