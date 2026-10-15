using System;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class GoalsView : UserControl
{
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public GoalsView() => InitializeComponent();
    private void NewGoal_Click(object sender, RoutedEventArgs args) => new GoalEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void NewHabit_Click(object sender, RoutedEventArgs args) => new HabitEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditGoal_Click(object sender, RoutedEventArgs args) => new GoalEditorWindow(Main, ((GoalRow)((Button)sender).DataContext).Progress.Goal) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void EditHabit_Click(object sender, RoutedEventArgs args) => new HabitEditorWindow(Main, ((HabitRow)((Button)sender).DataContext).Habit) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void CheckHabit_Click(object sender, RoutedEventArgs args) { var item = (HabitRow)((Button)sender).DataContext; if (item.CheckedToday && !ConfirmDeletionWindow.Ask(Window.GetWindow(this), "撤销今天的打卡？")) return; Main.Goals.CheckIn(item.Id, Main.Goals.Today); }
    private void HabitHistory_Click(object sender, RoutedEventArgs args) => new HabitHistoryWindow(Main, ((HabitRow)((Button)sender).DataContext).Habit) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void GoalOrder_Requested(object sender, RoutedEventArgs args) { var move = (ReorderEventArgs)args; Main.Goals.MoveGoal((GoalRow)((ReorderButton)sender).DataContext, move.Direction, move.Target as GoalRow); args.Handled = true; }
    private void HabitOrder_Requested(object sender, RoutedEventArgs args) { var move = (ReorderEventArgs)args; Main.Goals.MoveHabit((HabitRow)((ReorderButton)sender).DataContext, move.Direction, move.Target as HabitRow); args.Handled = true; }
}
