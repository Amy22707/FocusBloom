using System.Windows;
using FocusBloom.Core.Restrictions;

namespace FocusBloom.Desktop.Windows;

public partial class ApplicationPickerWindow : Window
{
    public ApplicationPickerWindow()
    {
        InitializeComponent();
        WindowAppearance.Attach(this);
        ApplicationList.ItemsSource = ForegroundApplications.GetRunningApplications();
    }
    public AllowedApplication? SelectedApplication => ApplicationList.SelectedItem as AllowedApplication;
    private void Add_Click(object sender, RoutedEventArgs e) { if (SelectedApplication is not null) DialogResult = true; }
}
