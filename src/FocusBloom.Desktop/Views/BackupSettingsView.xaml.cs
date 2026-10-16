using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Backup;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Views;

public partial class BackupSettingsView : UserControl
{
    private BackupSettingsViewModel Model => (BackupSettingsViewModel)DataContext;
    public event Action<PreparedRestore>? RestoreRequested;
    public BackupSettingsView() { InitializeComponent(); BackupRetentionSelector.ItemsSource = Enumerable.Range(1, 30).ToArray(); }
    private async void Create_Click(object sender, RoutedEventArgs args)
    {
        var picker = new SaveFileDialog { Title = "保存 FocusBloom 备份", Filter = "FocusBloom 备份|*.fbbak", DefaultExt = ".fbbak", FileName = LocalBackupService.NewFileName(), InitialDirectory = Directory.Exists(Model.DirectoryPath) ? Model.DirectoryPath : "", AddExtension = true };
        if (picker.ShowDialog(Window.GetWindow(this)) != true) return;
        try { await Model.BackupAsync(picker.FileName); } catch (OperationCanceledException) { } catch (Exception error) { Model.ReportError(error); }
    }
    private async void Restore_Click(object sender, RoutedEventArgs args)
    {
        if (!Model.CanRestore) { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), Model.RestoreHint, "暂时不能恢复"); return; }
        var picker = new OpenFileDialog { Title = "选择 FocusBloom 备份", Filter = "FocusBloom 备份|*.fbbak", InitialDirectory = Directory.Exists(Model.DirectoryPath) ? Model.DirectoryPath : "" };
        if (picker.ShowDialog(Window.GetWindow(this)) == true) await OpenRestoreAsync(picker.FileName);
    }
    public async Task OpenRestoreAsync(string file)
    {
        try
        {
            var manifest = await Task.Run(() => LocalBackupService.Inspect(file));
            if (!Model.CanRestore) { ConfirmDeletionWindow.ShowNotice(Window.GetWindow(this), Model.RestoreHint, "暂时不能恢复"); return; }
            var preview = new BackupRestoreWindow(Model, file, manifest) { Owner = Window.GetWindow(this) };
            if (preview.ShowDialog() == true && preview.Prepared is { } prepared) RestoreRequested?.Invoke(prepared);
        }
        catch (OperationCanceledException) { } catch (Exception error) { Model.ReportError(error); }
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs args)
    { var picker = new OpenFolderDialog { Title = "选择备份保存位置", InitialDirectory = Directory.Exists(Model.DirectoryPath) ? Model.DirectoryPath : "" }; if (picker.ShowDialog(Window.GetWindow(this)) == true) Model.DirectoryPath = picker.FolderName; }
    private void OpenFolder_Click(object sender, RoutedEventArgs args)
    { try { Directory.CreateDirectory(Model.DirectoryPath); Process.Start(new ProcessStartInfo(Model.DirectoryPath) { UseShellExecute = true }); } catch (Exception error) { Model.ReportError(error); } }
}
