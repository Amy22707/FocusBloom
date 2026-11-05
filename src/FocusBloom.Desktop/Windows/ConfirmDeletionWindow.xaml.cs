using System.Windows;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public partial class ConfirmDeletionWindow : Window
{
    public ConfirmDeletionWindow(string message)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        DeletionMessage.Text = UiText.Get(message);
    }
    public static bool Ask(Window owner, string message) => new ConfirmDeletionWindow(message) { Owner = owner }.ShowDialog() == true;
    public static void ShowNotice(Window owner, string message, string heading = "暂时不能删除")
    {
        var window = new ConfirmDeletionWindow(message) { Owner = owner, Title = UiText.Get("提示") };
        window.DeletionHeading.Text = UiText.Get(heading);
        window.CancelDeletionButton.Visibility = window.ConfirmDeletionButton.Visibility = Visibility.Collapsed;
        window.CancelDeletionButton.IsDefault = false;
        window.NoticeButton.Visibility = Visibility.Visible;
        window.NoticeButton.IsDefault = true;
        window.ShowDialog();
    }
    private void Confirm_Click(object sender, RoutedEventArgs args) => DialogResult = true;
}
