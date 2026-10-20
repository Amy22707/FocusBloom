using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class DateGroupDisplayTests
{
    [Fact]
    public void GroupOrderAndMixedSelectionSurviveReopenAndGroupRename()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomDateDisplay", Guid.NewGuid().ToString());
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = new DateOnly(2026, 10, 21);
                var items = new[] { new DateEvent(Guid.NewGuid(), "入学纪念日", day.AddDays(-52), Mode: DateCountMode.CountUp), new DateEvent(Guid.NewGuid(), "期中考试", day.AddDays(13), Group: "考试"), new DateEvent(Guid.NewGuid(), "阶段检查", day.AddDays(3), Group: "工作") };
                foreach (var item in items) store.SaveDateEvent(item);
                var model = new DateEventsViewModel(store); model.MoveGroup(model.Groups.Single(group => group.Name == "工作"), OrderMove.First);
                Assert.Equal(new[] { "工作", "未分组", "考试" }, model.GroupNames); model.MixedView = true; model.GroupFilters.Single(group => group.Name == "未分组").Selected = false;
                Assert.False(model.ShowBoard); Assert.Equal(2, model.VisibleItems.Count); Assert.Equal(3, model.Items.Count); Assert.Equal("已选 2 组", model.MixedGroupSummary);
                model = new(new SqliteFocusStore(store.DatabasePath)); Assert.True(model.MixedView); Assert.Equal(new[] { "工作", "未分组", "考试" }, model.GroupNames); Assert.Equal(2, model.VisibleItems.Count);
                Assert.Null(model.SaveGroup("考试", "课程考试")); Assert.True(model.GroupFilters.Single(group => group.Name == "课程考试").Selected); Assert.Equal(2, model.VisibleItems.Count);
                model.SelectedGroup = model.GroupChoices.Single(group => group.Name == "未分组"); Assert.Single(model.VisibleItems); Assert.False(model.ShowMixedGroups);
                model.SelectedGroup = model.GroupChoices[0]; Assert.Equal(2, model.VisibleItems.Count); model.MixedView = false; Assert.True(model.ShowBoard); Assert.Equal(3, model.VisibleItems.Count);
                model.MixedView = true; foreach (var filter in model.GroupFilters) filter.Selected = true; Assert.Equal("全部分组", model.MixedGroupSummary); Assert.Null(model.SaveGroup(null, "展示")); Assert.True(model.GroupFilters.Single(group => group.Name == "展示").Selected);
                foreach (var filter in model.GroupFilters) filter.Selected = false; Assert.Empty(model.VisibleItems); Assert.Equal("请选择要查看的分组。", model.EmptyMessage);
                Assert.Equal(items[0], store.GetDateEvents().Single(item => item.Id == items[0].Id)); Assert.Equal(items[1] with { Group = "课程考试" }, store.GetDateEvents().Single(item => item.Id == items[1].Id));
                Assert.Equal("已经", new DateEventRow(items[0], day).Label); Assert.Equal("已过去", new DateEventRow(items[0] with { Mode = DateCountMode.Countdown }, day).Label);
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void ColumnHandlesReorderGroupsAndMixedPopupFiltersTheSharedGrid()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomDateGroupUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var day = DateOnly.FromDateTime(DateTime.Today);
                var items = new[] { new DateEvent(Guid.NewGuid(), "入学纪念日", day.AddDays(-52), Mode: DateCountMode.CountUp), new DateEvent(Guid.NewGuid(), "期中考试", day.AddDays(13), Group: "考试"), new DateEvent(Guid.NewGuid(), "阶段测验", day.AddDays(14), Group: "考试"), new DateEvent(Guid.NewGuid(), "阶段检查", day.AddDays(3), Notes: "携带证件及运动装备\n提前到达", Group: "工作"), new DateEvent(Guid.NewGuid(), "项目展示", day.AddDays(26), Group: "展示") };
                foreach (var item in items) store.SaveDateEvent(item);
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1120, Height = 720, NavigationCollapsed = true }; window.Show(); main.Navigation.Items.Single(item => item.Key == "dates").OpenCommand.Execute(null); Flush();
                var dates = Children<DateEventsView>(window).Single(); var board = (ItemsControl)dates.FindName("DateGroupBoard"); Assert.True(ReorderButton.GetEnableDrop(board));
                var handle = Children<ReorderButton>(board).Single(item => item.DataContext is DateEventGroup { Name: "工作" }); Assert.Equal(4, Children<ReorderButton>(board).Count());
                var target = Children<Border>(board).Single(item => item.Width == 280 && item.DataContext is DateEventGroup { Name: "未分组" });
                var payloadType = typeof(ReorderButton).GetNestedType("DragItem", BindingFlags.NonPublic)!; var data = new DataObject("FocusBloom.ItemOrder", Activator.CreateInstance(payloadType, handle, board)!);
                var over = Args(target, data, DragDrop.DragOverEvent); target.RaiseEvent(over); Assert.True(over.Handled); Assert.Equal(DragDropEffects.Move, over.Effects);
                var drop = Args(target, data, DragDrop.DropEvent); target.RaiseEvent(drop); Flush(); Assert.Equal("工作", main.DateEvents.Groups[0].Name); Assert.Equal("工作", main.DateEvents.GroupChoices[1].Name);
                GoalsUiTests.Save(window, "date-groups-reordered.png");
                var mixed = (CheckBox)dates.FindName("MixedDatesToggle"); mixed.IsChecked = true; Flush(); Assert.False(main.DateEvents.ShowBoard); Assert.Equal(Visibility.Collapsed, ((ScrollViewer)dates.FindName("DateBoardViewport")).Visibility);
                var popupToggle = (ToggleButton)dates.FindName("MixedGroupsToggle"); popupToggle.IsChecked = true; Flush();
                var filters = (ItemsControl)dates.FindName("MixedGroupFilters"); Assert.Equal(4, filters.Items.Count); var check = Children<CheckBox>(filters).Single(item => item.DataContext is DateGroupFilter { Name: "未分组" }); check.IsChecked = false;
                Children<CheckBox>(filters).Single(item => item.DataContext is DateGroupFilter { Name: "工作" }).IsChecked = false; Flush(); Assert.Equal(3, main.DateEvents.VisibleItems.Count); popupToggle.IsChecked = false; Flush();
                var cards = (ItemsControl)dates.FindName("DateCards"); Assert.Equal(3, cards.Items.Count); Assert.Contains(Children<TextBlock>(cards), item => item.Text == "考试" && item.IsVisible); Assert.Contains(Children<TextBlock>(cards), item => item.Text == "展示" && item.IsVisible);
                GoalsUiTests.Save(window, "date-groups-mixed-light.png"); main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); window.Width = 900; window.Height = 560; Flush(); GoalsUiTests.Save(window, "date-groups-mixed-dark.png");
                mixed.IsChecked = false; Flush(); Assert.True(main.DateEvents.ShowBoard); Assert.Equal(5, main.DateEvents.VisibleItems.Count); Assert.Equal("工作", main.DateEvents.Groups[0].Name);
                Assert.Equal(items, store.GetDateEvents()); Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    private static DragEventArgs Args(FrameworkElement target, IDataObject data, RoutedEvent routed)
    { var args = (DragEventArgs)typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single().Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(40, 30)]); args.RoutedEvent = routed; return args; }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
