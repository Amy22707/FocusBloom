using System.Windows;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class StatisticsModulesWindow : Window
{
    public StatisticsModulesWindow(StatisticsViewModel model) { InitializeComponent(); WindowAppearance.Attach(this); DataContext = model; }
    private void Order_Requested(object sender, RoutedEventArgs args)
    { var move = (ReorderEventArgs)args; ((StatisticsViewModel)DataContext).MoveModule((StatisticsModuleOption)((ReorderButton)sender).DataContext, move.Direction, move.Target as StatisticsModuleOption); args.Handled = true; }
}
