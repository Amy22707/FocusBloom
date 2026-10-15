using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public partial class DateEventsView : UserControl
{
    private MainViewModel Main => (MainViewModel)Window.GetWindow(this).DataContext;
    public DateEventsView() => InitializeComponent();
    private void New_Click(object sender, RoutedEventArgs e) => new DateEventEditorWindow(Main) { Owner = Window.GetWindow(this) }.ShowDialog();
    private void Edit_Click(object sender, RoutedEventArgs e)
    { var editor = new DateEventEditorWindow(Main, ((DateEventRow)((Button)sender).DataContext).Item) { Owner = Window.GetWindow(this) }; editor.ShowDialog(); if (editor.FocusRequested) ((MainWindow)Window.GetWindow(this)).ReturnToFocus(); }
}
