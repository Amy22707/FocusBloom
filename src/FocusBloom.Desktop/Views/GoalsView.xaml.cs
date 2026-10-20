using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class GoalsView : UserControl
{
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public GoalsView() => InitializeComponent();
    private void DayCards_SizeChanged(object sender, SizeChangedEventArgs args)
    { if (FindPanel(DayHabitCards) is { } panel) panel.Columns = DayHabitCards.ActualWidth >= 720 ? 2 : 1; }
    private static System.Windows.Controls.Primitives.UniformGrid? FindPanel(DependencyObject root)
    {
        if (root is System.Windows.Controls.Primitives.UniformGrid panel) return panel;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++) if (FindPanel(VisualTreeHelper.GetChild(root, index)) is { } child) return child;
        return null;
    }
    private void CheckDayHabit_Click(object sender, RoutedEventArgs args)
    {
        var row = (HabitDayRow)((Button)sender).DataContext;
        if (row.Checked && !ConfirmDeletionWindow.Ask(Window.GetWindow(this), $"撤销 {row.Date:M月d日}‘{row.Name}’的打卡？")) return;
        var offset = DailyViewport.VerticalOffset;
        if (!Main.Goals.CheckIn(row.Id, row.Date)) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            DailyViewport.ScrollToVerticalOffset(offset);
            if (row.Checked || DayHabitCards.ItemContainerGenerator.ContainerFromItem(Main.Goals.DayHabits.First(item => item.Id == row.Id)) is not FrameworkElement card) return;
            var scale = new ScaleTransform(1, 1); card.RenderTransformOrigin = new Point(.5, .5); card.RenderTransform = scale;
            var animation = new DoubleAnimation(1, 1.025, TimeSpan.FromMilliseconds(130)) { AutoReverse = true, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }; scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
        }));
    }
    private void EditDayHabit_Click(object sender, RoutedEventArgs args) => new HabitEditorWindow(Main, ((HabitDayRow)((Button)sender).DataContext).Habit) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void DayHabitOrder_Requested(object sender, RoutedEventArgs args)
    {
        var row = (HabitDayRow)((ReorderButton)sender).DataContext; var target = ((ReorderEventArgs)args).Target as HabitDayRow;
        Main.Goals.MoveHabit(Main.Goals.Habits.Single(item => item.Id == row.Id), ((ReorderEventArgs)args).Direction, Main.Goals.Habits.FirstOrDefault(item => item.Id == target?.Id)); args.Handled = true;
    }
    private void DayGoalOrder_Requested(object sender, RoutedEventArgs args)
    {
        var row = (GoalRow)((ReorderButton)sender).DataContext; var target = ((ReorderEventArgs)args).Target as GoalRow;
        Main.Goals.MoveGoal(Main.Goals.Goals.Single(item => item.Id == row.Id), ((ReorderEventArgs)args).Direction, Main.Goals.Goals.FirstOrDefault(item => item.Id == target?.Id)); args.Handled = true;
    }
    private void NewGoal_Click(object sender, RoutedEventArgs args) => new GoalEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewHabit_Click(object sender, RoutedEventArgs args) => new HabitEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditGoal_Click(object sender, RoutedEventArgs args) => new GoalEditorWindow(Main, ((GoalRow)((Button)sender).DataContext).Progress.Goal) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditHabit_Click(object sender, RoutedEventArgs args) => new HabitEditorWindow(Main, ((HabitRow)((Button)sender).DataContext).Habit) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void CheckHabit_Click(object sender, RoutedEventArgs args) { var item = (HabitRow)((Button)sender).DataContext; if (item.CheckedToday && !ConfirmDeletionWindow.Ask(Window.GetWindow(this), "撤销今天的打卡？")) return; Main.Goals.CheckIn(item.Id, Main.Goals.Today); }
    private void HabitHistory_Click(object sender, RoutedEventArgs args) => new HabitHistoryWindow(Main, ((HabitRow)((Button)sender).DataContext).Habit) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void GoalOrder_Requested(object sender, RoutedEventArgs args) { var move = (ReorderEventArgs)args; Main.Goals.MoveGoal((GoalRow)((ReorderButton)sender).DataContext, move.Direction, move.Target as GoalRow); args.Handled = true; }
    private void HabitOrder_Requested(object sender, RoutedEventArgs args) { var move = (ReorderEventArgs)args; Main.Goals.MoveHabit((HabitRow)((ReorderButton)sender).DataContext, move.Direction, move.Target as HabitRow); args.Handled = true; }
}
