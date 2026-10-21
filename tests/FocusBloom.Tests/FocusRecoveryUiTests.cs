using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Todos;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class FocusRecoveryUiTests
{
    [Fact]
    public void DailyLimitSavesAutomaticallyAndNoticeSurvivesRestartUntilAcknowledged()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomLimit", Guid.NewGuid().ToString());
            MainViewModel? first = null, second = null;
            MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new ManualClock();
                first = new(store, new FocusTimer(clock)); first.KeepScreenOn = true;
                window = new(first, store, Path.Combine(root, "browser")); window.Show();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                first.SelectedRestrictionMode = first.RestrictionModes.Single(mode => mode.Strict); first.StartCommand.Execute(null);
                Assert.True(first.RestrictionsActive); Assert.True(first.IsFocusActive);
                clock.Advance(TimeSpan.FromHours(25)); first.Tick();
                Assert.Equal("24:00:00", first.TimerText); Assert.False(first.RestrictionsActive); Assert.False(first.IsFocusActive);
                Assert.False(first.ShowResume); Assert.False(first.ShowEnd); Assert.True(first.CanConfigure); Assert.True(first.HasLimitNotice);
                Assert.DoesNotContain(Application.Current.Windows.OfType<Window>(), window => window is ConfirmDeletionWindow);
                var record = Assert.Single(store.GetRecords()); Assert.Equal(TimeSpan.FromHours(24), record.FocusDuration);
                Assert.Equal(EndReason.DurationLimit, record.EndReason); Assert.False(first.HasUnsavedRecord);
                second = new(store, new FocusTimer(clock)); Assert.False(second.HasPendingRecovery); Assert.True(second.HasLimitNotice);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var notice = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single();
                    Assert.Contains("24小时", ((TextBlock)notice.FindName("DeletionMessage")).Text); notice.Close();
                }));
                window.ShowLimitNotice(); Assert.False(first.HasLimitNotice); Assert.Empty(store.GetSetting("focus.limitNotice")!);
                first.Tick(); Assert.Single(store.GetRecords());
            }
            finally { window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); first?.Media.Dispose(); second?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
    public class RecoveryStoreProxy : DispatchProxy
    {
        public IFocusStore Target = null!;
        public bool FailRecords;
        public bool FailCheckpoint;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (FailRecords && method!.Name == nameof(IFocusStore.SaveRecord)
                || FailCheckpoint && method!.Name == nameof(IFocusStore.SaveSetting) && Equals(args![0], "focus.checkpoint"))
                throw new IOException("测试存储不可写");
            return method!.Invoke(Target, args);
        }
    }

    [Fact]
    public void FailedRecoverySaveKeepsPendingDataForRetryAndAutosaveFailureRemainsVisible()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomRecovery", Guid.NewGuid().ToString());
            MainViewModel? first = null, second = null, third = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new ManualClock();
                first = new(store, new FocusTimer(clock)); first.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(20)); first.Tick();
                var json = store.GetSetting("focus.checkpoint");
                var wrapped = DispatchProxy.Create<IFocusStore, RecoveryStoreProxy>(); var proxy = (RecoveryStoreProxy)wrapped; proxy.Target = store;
                second = new(wrapped, new FocusTimer(clock)); proxy.FailRecords = true;
                Assert.False(second.ResolveRecovery(false)); Assert.True(second.HasPendingRecovery);
                Assert.Equal(json, store.GetSetting("focus.checkpoint")); Assert.Empty(store.GetRecords());
                proxy.FailRecords = false; Assert.True(second.ResolveRecovery(false)); Assert.Single(store.GetRecords());
                second.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(6)); second.Tick();
                third = new(wrapped, new FocusTimer(clock)); proxy.FailCheckpoint = true;
                Assert.True(third.ResolveRecovery(true)); Assert.False(third.HasPendingRecovery);
                Assert.Contains("自动保存失败", third.StatusMessage);
            }
            finally { first?.Media.Dispose(); second?.Media.Dispose(); third?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void ReopenedTimerKeepsTodoAndCanResumeOrSaveWithoutDuplicates()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomRecovery", Guid.NewGuid().ToString());
            var models = new List<MainViewModel>();
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new ManualClock();
                var first = new MainViewModel(store, new FocusTimer(clock)); models.Add(first);
                var folder = new TodoFolder(Guid.NewGuid(), "阅读", "学习"); store.SaveTodoFolder(folder);
                var todo = new TodoItem(Guid.NewGuid(), folder.Id, "阅读资料"); store.SaveTodo(todo); first.Todos.Reload();
                Assert.True(first.StartFromTodo(todo.Id));
                clock.Advance(TimeSpan.FromSeconds(42)); first.Tick();
                var checkpoint = store.GetSetting("focus.checkpoint"); Assert.False(string.IsNullOrEmpty(checkpoint));
                clock.Advance(TimeSpan.FromHours(3));
                var second = new MainViewModel(store, new FocusTimer(clock)); models.Add(second);
                Assert.True(second.HasPendingRecovery); Assert.False(second.StartCommand.CanExecute(null));
                Assert.False(second.CanDeleteTodo(todo.Id)); Assert.False(second.RestrictionsActive);
                Assert.True(second.ResolveRecovery(true)); Assert.Equal("00:00:42", second.TimerText); Assert.True(second.HasFocusTodo);
                clock.Advance(TimeSpan.FromSeconds(8)); second.Tick();
                var third = new MainViewModel(store, new FocusTimer(clock)); models.Add(third);
                Assert.True(third.ResolveRecovery(false));
                var record = Assert.Single(store.GetRecords()); Assert.Equal(TimeSpan.FromSeconds(50), record.FocusDuration);
                Assert.Equal(todo.Id, record.TodoId); Assert.Equal(EndReason.Interrupted, record.EndReason);
                Assert.False(third.HasPendingRecovery); Assert.True(third.CanConfigure); Assert.Empty(store.GetSetting("focus.checkpoint")!);
                store.SaveSetting("focus.checkpoint", checkpoint!);
                var fourth = new MainViewModel(store, new FocusTimer(clock)); models.Add(fourth);
                Assert.False(fourth.HasPendingRecovery); fourth.Tick(); Assert.Single(store.GetRecords());
            }
            finally { foreach (var model in models) model.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void ExitDuringSingleRestClearsCheckpoint()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomRecovery", Guid.NewGuid().ToString());
            MainViewModel? first = null, second = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new ManualClock();
                first = new(store, new FocusTimer(clock)); first.RestMinutes = "1";
                first.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(30)); first.Tick();
                first.EndCommand.Execute(null);
                Assert.Equal(FocusState.Resting, first.State);
                Assert.True(first.PrepareExit());
                Assert.Equal(FocusState.Ready, first.State);
                Assert.Single(store.GetRecords()); Assert.Empty(store.GetSetting("focus.checkpoint")!);
                second = new(store, new FocusTimer(clock));
                Assert.False(second.HasPendingRecovery); Assert.True(second.CanConfigure);
            }
            finally { first?.Media.Dispose(); second?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void DiscardAndNormalExitClearCheckpointAndRecoveryWindowUsesTheme()
    {
        WpfTestHost.Run(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "FocusBloomRecovery", Guid.NewGuid().ToString());
            MainViewModel? first = null, second = null; FocusRecoveryWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var clock = new ManualClock();
                first = new(store, new FocusTimer(clock)); first.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(20)); first.Tick();
                second = new(store, new FocusTimer(clock));
                foreach (var mode in new[] { AppearanceMode.Light, AppearanceMode.Dark })
                {
                    Appearance.Apply(Appearance.Palettes[0], mode);
                    window = new(second); window.Show(); window.UpdateLayout();
                    Assert.Equal(3, ((WrapPanel)((StackPanel)window.Content).Children[^1]).Children.Count);
                    Assert.True(window.ActualHeight > 200); GoalsUiTests.Save(window, $"focus-recovery-{mode}.png"); window.Close(); window = null;
                }
                Assert.True(second.ResolveRecovery(false, discard: true)); Assert.Empty(store.GetRecords());
                second.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(30)); second.Tick();
                Assert.True(second.PrepareExit()); Assert.Single(store.GetRecords()); Assert.Empty(store.GetSetting("focus.checkpoint")!);
            }
            finally { window?.Close(); first?.Media.Dispose(); second?.Media.Dispose(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
        });
    }
}
