using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Reflection;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class MoodPaletteTests
{
    [Fact]
    public void SixMoodsPreservePreviousValuesAndSaveNewMoodWithoutChangingDiaryContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomMoodPalette", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
            Assert.Equal(new[] { "低落", "疲惫", "平静", "愉快", "兴奋", "烦躁" }, ReflectionRules.Moods.Select(item => item.Name));
            Assert.Equal(6, ReflectionRules.Moods.Select(item => item.Color).Distinct().Count());
            var records = ReflectionRules.Moods.Select(mood => new DailyReflection(today.AddDays(mood.Value - 6), mood.Value, new[] { "学习" }, "保留感受", "保留成果", "保留下一步")).ToArray();
            foreach (var record in records) store.SaveDailyReflection(record);
            store = new(store.DatabasePath); var restored = store.GetDailyReflections(); Assert.Equal(records.Length, restored.Count);
            foreach (var pair in records.Zip(restored))
            {
                Assert.Equal(pair.First.Date, pair.Second.Date); Assert.Equal(pair.First.Mood, pair.Second.Mood); Assert.Equal(pair.First.Tags, pair.Second.Tags);
                Assert.Equal(pair.First.Feeling, pair.Second.Feeling); Assert.Equal(pair.First.Highlights, pair.Second.Highlights); Assert.Equal(pair.First.NextStep, pair.Second.NextStep);
            }
            Assert.Equal(Enumerable.Range(1, 5), ReflectionRules.Moods.Take(5).Select(item => item.Level)); Assert.Equal(1, ReflectionRules.Mood(6).Level);
            Assert.Throws<InvalidOperationException>(() => ReflectionRules.Normalize(records[0] with { Mood = 7 }, today));
            Assert.Throws<InvalidOperationException>(() => ReflectionRules.Normalize(records[0] with { Mood = 0 }, today));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void SixColoredFacesRenderInEditorCalendarAndWidgetAcrossThemes()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomMoodFaces", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today);
                foreach (var mood in ReflectionRules.Moods) store.SaveDailyReflection(new(today.AddDays(mood.Value - 6), mood.Value, new[] { "学习" }, "慢慢找到自己的节奏", "完成了今天的计划", "整理课程笔记"));
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1240, Height = 850 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "reflection").OpenCommand.Execute(null); Flush();
                var page = Children<ReflectionView>(window).Single(); var calendar = (StackPanel)page.FindName("MoodCalendar"); Assert.Equal(6, Children<MoodIcon>(calendar).Count()); Assert.Equal("烦躁", main.Reflection.MoodTitle);
                foreach (var dark in new[] { false, true })
                {
                    main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == (dark ? AppearanceMode.Dark : AppearanceMode.Light)); Flush();
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        var editor = Application.Current.Windows.OfType<ReflectionEditorWindow>().Single(); var choices = (UniformGrid)editor.FindName("MoodChoices"); Assert.Equal(3, choices.Columns); Assert.Equal(6, choices.Children.Count);
                        Assert.Equal(new[] { 5, 4, 3, 2, 1, 6 }, Children<MoodIcon>(choices).Select(icon => icon.Value)); Assert.All(Children<MoodIcon>(choices), icon => { Assert.Equal(42, icon.ActualHeight); Assert.True(icon.ActualWidth > 0); });
                        ((Button)editor.FindName("MoodChoice4")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(2, ((Button)editor.FindName("MoodChoice4")).BorderThickness.Left);
                        GoalsUiTests.Save(editor, "mood-six-editor-" + (dark ? "dark" : "light") + ".png");
                        ((Button)editor.FindName("MoodChoice6")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    ((Button)page.FindName("EditReflectionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.Equal(6, main.Reflection.SelectedEntry!.Mood); Assert.Equal(6, store.GetDailyReflections().Count);
                    GoalsUiTests.Save(window, "mood-six-month-" + (dark ? "dark" : "light") + ".png");
                    var trend = (Canvas)((ContentControl)page.FindName("MoodTrend")).Content; Assert.All(Children<Button>(trend).Where(item => item.Tag is DateOnly), dot => Assert.InRange(Canvas.GetTop(dot), 0, 140));
                }
                main.Widgets[DesktopWidgetKind.Mood].Enabled = true; Flush(); var widget = window.DesktopWidgets.Windows[DesktopWidgetKind.Mood]; Assert.Equal(6, Children<MoodIcon>(widget).Count());
                main.Widgets[DesktopWidgetKind.Mood].View = "趋势"; Flush(); var chart = Children<Canvas>((StackPanel)widget.FindName("WidgetBody")).Single();
                Assert.All(Children<Button>(chart).Where(item => item.Tag is DateOnly), dot => Assert.InRange(Canvas.GetTop(dot), 0, 124)); main.Widgets[DesktopWidgetKind.Mood].View = "月历"; Flush(); GoalsUiTests.Save(widget, "mood-six-widget.png");
                Assert.All(store.GetDailyReflections(), item => Assert.Equal("整理课程笔记", item.NextStep)); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<ReflectionEditorWindow>().ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
