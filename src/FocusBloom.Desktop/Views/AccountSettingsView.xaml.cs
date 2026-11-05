using System.Windows.Controls;
using System.Windows;
using FocusBloom.Desktop.ViewModels;
namespace FocusBloom.Desktop.Views;
public partial class AccountSettingsView : UserControl
{
    public AccountSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is AccountSettingsViewModel old) old.SecretsCleared -= ClearPasswords;
            if (args.NewValue is AccountSettingsViewModel current) current.SecretsCleared += ClearPasswords;
            ClearPasswords();
        };
    }
    private void ClearPasswords() { LoginPasswordInput.Clear(); OldPasswordInput.Clear(); NewPasswordInput.Clear(); ConfirmPasswordInput.Clear(); RecoveryPasswordInput.Clear(); RecoveryConfirmInput.Clear(); }
    private void UseCode_Click(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) { model.SetPasswordRecovery(false); model.UsePasswordLogin = false; } }
    private void RecoverPassword_Click(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) model.SetPasswordRecovery(true); }
    private void LoginPassword_Changed(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) model.LoginPassword = ((PasswordBox)sender).Password; }
    private void OldPassword_Changed(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) model.OldPassword = ((PasswordBox)sender).Password; }
    private void NewPassword_Changed(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) model.NewPassword = ((PasswordBox)sender).Password; }
    private void ConfirmPassword_Changed(object sender, RoutedEventArgs args) { if (DataContext is AccountSettingsViewModel model) model.ConfirmPassword = ((PasswordBox)sender).Password; }
}
