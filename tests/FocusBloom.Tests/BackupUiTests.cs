using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class BackupUiTests
{
    private sealed class Clock : TimeProvider { private DateTimeOffset _now = DateTimeOffset.Now; public override DateTimeOffset GetUtcNow() => _now; public void Advance(TimeSpan span) => _now += span; }
    [Fact]
    public void SettingsAutomaticBackupActiveSessionNoticeRestorePreviewAndReopenPreserveCustomMedia()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomBackupUi", Guid.NewGuid().ToString()); MainWindow? window = null; MainViewModel? main = null; PreparedRestore? prepared = null; DispatcherTimer? confirm = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "profile", "focusbloom.db")); var clock = new Clock(); main = new(store, new FocusTimer(clock));
                Directory.CreateDirectory(Path.Combine(root, "original")); var image = Path.Combine(root, "original", "我的图片.jpg"); File.Copy(main.Media.Images.First().Asset.File, image); main.Media.ImportFolder(Path.GetDirectoryName(image)!); File.Copy(image, Path.Combine(root, "original", "文件夹新增.jpg")); main.Media.Dispose(); main = new(store, new FocusTimer(clock)); Assert.Equal(2, main.Media.Images.Count(item => item.Custom));
                window = new(main, store, Path.Combine(root, "browser")); window.Show(); Flush(); var backups = window.Backups!;
                main.Navigation.Items.Single(item => item.Key == "settings").OpenCommand.Execute(null); ((TextBox)window.FindName("SettingsSearchInput")).Text = "备份"; Flush(); var section = (Expander)window.FindName("BackupSettingsSection"); Assert.True(section.IsExpanded); Assert.Equal(Visibility.Visible, section.Visibility); Assert.Equal(Visibility.Collapsed, ((Expander)window.FindName("MediaSettingsSection")).Visibility);
                var view = (BackupSettingsView)window.FindName("BackupSettings"); backups.DirectoryPath = Path.Combine(root, "backups"); ((ComboBox)view.FindName("BackupRetentionSelector")).SelectedItem = 3; ((CheckBox)view.FindName("AutomaticBackupInput")).IsChecked = true; Flush(); Assert.Equal(3, backups.Keep);
                Wait(backups.CheckAutomaticAsync()); Wait(backups.CheckAutomaticAsync()); Assert.Single(Directory.GetFiles(backups.DirectoryPath, "FocusBloom-auto-*.fbbak")); Assert.Contains("最近备份", backups.LastBackup);
                main.SelectedTimerMode = main.TimerModes[0]; main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.StartCommand.Execute(null); Assert.False(backups.CanRestore);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { var notice = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single(); Assert.Equal(Visibility.Collapsed, ((Button)notice.FindName("ConfirmDeletionButton")).Visibility); Assert.Equal(Visibility.Visible, ((Button)notice.FindName("NoticeButton")).Visibility); notice.Close(); })); ((Button)view.FindName("RestoreBackupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Flush(); Assert.True(main.IsFocusActive);
                var manual = Path.Combine(root, "manual.fbbak"); Wait(backups.BackupAsync(manual)); Assert.Equal(0, LocalBackupService.Inspect(manual).Counts["专注记录"]); clock.Advance(TimeSpan.FromSeconds(12)); main.EndCommand.Execute(null); Flush(); Assert.True(backups.CanRestore); Assert.Single(store.GetRecords());
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Light); Flush(); GoalsUiTests.Save(window, "backup-settings-light.png");
                var preview = new BackupRestoreWindow(backups, manual, LocalBackupService.Inspect(manual)) { Owner = window };
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => { Assert.True(((Button)preview.FindName("ConfirmRestoreButton")).IsEnabled); GoalsUiTests.Save(preview, "backup-restore-preview.png"); preview.Close(); })); preview.ShowDialog(); Assert.Single(store.GetRecords()); Assert.Empty(Directory.GetFiles(backups.DirectoryPath, "FocusBloom-before-restore-*.fbbak"));
                window.BackupRestoreRequested += item => prepared = item;
                confirm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) }; confirm.Tick += (_, _) => { var dialog = Application.Current.Windows.OfType<BackupRestoreWindow>().SingleOrDefault(); if (dialog is null) return; confirm.Stop(); ((Button)dialog.FindName("ConfirmRestoreButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }; confirm.Start(); Wait(view.OpenRestoreAsync(manual)); Assert.NotNull(prepared); var safety = Assert.Single(Directory.GetFiles(backups.DirectoryPath, "FocusBloom-before-restore-*.fbbak")); Assert.Equal(1, LocalBackupService.Inspect(safety).Counts["专注记录"]); Assert.Single(store.GetRecords());
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); GoalsUiTests.Save(window, "backup-settings-dark.png");
                Wait(backups.StopAsync()); window.Close(); main.Media.Dispose(); backups.Service.ApplyRestore(prepared!); prepared!.Dispose(); prepared = null;
                main = new(new SqliteFocusStore(store.DatabasePath)); window = new(main, new SqliteFocusStore(store.DatabasePath), Path.Combine(root, "browser-reopened")); window.Show(); Flush(); Assert.Empty(main.Calendar.Store.GetRecords()); Assert.Equal(3, window.Backups!.Keep); Assert.True(window.Backups.AutomaticEnabled); Assert.Equal(backups.DirectoryPath, window.Backups.DirectoryPath); Assert.Equal(2, main.Media.Images.Count(item => item.Custom)); var restoredImage = Assert.Single(main.Media.Images, item => item.Custom && item.Asset.Name == "我的图片"); Assert.True(File.Exists(restoredImage.Asset.File)); Assert.StartsWith(Path.Combine(root, "profile", "restored-media"), restoredImage.Asset.File);
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally
            {
                confirm?.Stop(); foreach (var dialog in Application.Current.Windows.OfType<BackupRestoreWindow>().ToArray()) dialog.Close(); prepared?.Dispose(); if (window?.Backups is { } backups) Wait(backups.StopAsync()); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }
    private static void Wait(Task task)
    { var until = DateTime.UtcNow.AddSeconds(40); while (!task.IsCompleted) { Flush(); if (DateTime.UtcNow > until) throw new TimeoutException("备份界面操作未完成。"); Thread.Sleep(5); } task.GetAwaiter().GetResult(); Flush(); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
}
