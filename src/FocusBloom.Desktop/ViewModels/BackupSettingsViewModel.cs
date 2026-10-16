using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Infrastructure.Backup;

namespace FocusBloom.Desktop.ViewModels;

public sealed class BackupSettingsViewModel : ObservableObject
{
    private readonly IFocusStore _store;
    private readonly MainViewModel _main;
    private BackupOptions _options;
    private readonly DispatcherTimer _automatic = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly CancellationTokenSource _stop = new();
    private Task? _operation;
    private bool _busy, _stopped;
    private DateTimeOffset _retryAfter;
    private string _status = "备份包含已保存数据和自定义素材，可用于换电脑迁移。";
    public LocalBackupService Service { get; }
    public bool AutomaticEnabled { get => _options.Automatic; set { Save(_options with { Automatic = value }); _retryAfter = default; Notify(); if (_automatic.IsEnabled) _ = CheckAutomaticAsync(); } }
    public string DirectoryPath { get => string.IsNullOrWhiteSpace(_options.Directory) ? Service.DefaultDirectory : _options.Directory; set { Save(_options with { Directory = Path.GetFullPath(value) }); _retryAfter = default; Notify(); if (_automatic.IsEnabled) _ = CheckAutomaticAsync(); } }
    public int Keep { get => _options.Keep; set { Save(_options with { Keep = Math.Clamp(value, 1, 30) }); Notify(); } }
    public bool IsBusy { get => _busy; private set { _busy = value; Notify(); Notify(nameof(CanOperate)); Notify(nameof(CanRestore)); } }
    public bool CanOperate => !IsBusy && !_stopped;
    public bool CanRestore => CanOperate && _main.CanConfigure && !_main.HasUnsavedRecord;
    public string RestoreHint => _main.CanConfigure && !_main.HasUnsavedRecord ? "恢复会替换当前数据，并重新启动软件。恢复前会自动备份当前内容。" : "请先结束本次计时或休息，并保存记录，再恢复备份。";
    public string Status { get => _status; private set { _status = value; Notify(); } }
    public string LastBackup => _options.LastBackup is { } at ? $"最近备份：{at.LocalDateTime:yyyy-MM-dd HH:mm}\n{_options.LastFile}" : "尚未创建备份";
    public BackupSettingsViewModel(LocalBackupService service, IFocusStore store, MainViewModel main)
    {
        Service = service; _store = store; _main = main; _options = store.GetSetting(LocalBackupService.OptionsKey) is { } json ? JsonSerializer.Deserialize<BackupOptions>(json)! : new();
        _automatic.Tick += async (_, _) => await CheckAutomaticAsync(); _main.PropertyChanged += MainChanged;
    }
    private void MainChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName is nameof(MainViewModel.CanConfigure) or nameof(MainViewModel.HasUnsavedRecord)) { Notify(nameof(CanRestore)); Notify(nameof(RestoreHint)); } }
    private void Save(BackupOptions value) { _options = value; _store.SaveSetting(LocalBackupService.OptionsKey, JsonSerializer.Serialize(value)); }
    private IProgress<BackupProgress> Progress() => new Progress<BackupProgress>(progress => Status = progress.Total > 0 ? $"{progress.Message}（{progress.Completed}/{progress.Total}）" : progress.Message);
    public Task<BackupResult> BackupAsync(string file)
    { if (!CanOperate) throw new InvalidOperationException("正在处理备份，请稍候。"); var task = CreateBackupAsync(file, false); _operation = task; return task; }
    private async Task<BackupResult> CreateBackupAsync(string file, bool automatic)
    {
        IsBusy = true;
        try
        {
            _main.Media.Save();
            var result = await Service.CreateAsync(file, Progress(), _stop.Token); var at = result.Manifest.CreatedAt;
            Save(_options with { LastFile = result.File, LastBackup = at, LastAutomatic = automatic ? at : _options.LastAutomatic }); Notify(nameof(LastBackup));
            Status = result.Manifest.Missing.Count == 0 ? "备份完成，数据和素材已保存。" : $"数据备份完成；{result.Manifest.Missing.Count} 个原文件已无法读取，未打包。打开此备份时可查看清单。";
            if (automatic) { try { LocalBackupService.PruneAutomatic(DirectoryPath, Keep); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Status = "备份已完成，旧自动备份清理失败：" + error.Message; } }
            return result;
        }
        finally { IsBusy = false; }
    }
    public Task<PreparedRestore> PrepareRestoreAsync(string file)
    { if (!CanRestore) throw new InvalidOperationException(RestoreHint); var task = PrepareAsync(file); _operation = task; return task; }
    private async Task<PreparedRestore> PrepareAsync(string file)
    {
        IsBusy = true; PreparedRestore? prepared = null;
        try
        {
            prepared = await Service.PrepareRestoreAsync(file, Progress(), _stop.Token);
            if (!_main.CanConfigure || _main.HasUnsavedRecord) throw new InvalidOperationException(RestoreHint);
            _main.Media.Save();
            var safety = await Service.CreateAsync(Path.Combine(DirectoryPath, LocalBackupService.NewFileName("FocusBloom-before-restore")), Progress(), _stop.Token);
            Save(_options with { LastFile = safety.File, LastBackup = safety.Manifest.CreatedAt }); Notify(nameof(LastBackup));
            Status = "当前数据已备份，正在重新启动以恢复…"; return prepared;
        }
        catch { prepared?.Dispose(); throw; }
        finally { IsBusy = false; }
    }
    public void ReportError(Exception error) => Status = "操作未完成：" + error.Message;
    public void StartAutomatic() { if (_stopped) return; _automatic.Start(); _ = CheckAutomaticAsync(); }
    public async Task CheckAutomaticAsync()
    {
        if (!CanOperate || DateTimeOffset.Now < _retryAfter || !LocalBackupService.AutomaticDue(_options, DateTimeOffset.Now)) return;
        try { var task = CreateBackupAsync(Path.Combine(DirectoryPath, LocalBackupService.NewFileName("FocusBloom-auto")), true); _operation = task; await task; }
        catch (OperationCanceledException) { }
        catch (Exception error) { ReportError(error); _retryAfter = DateTimeOffset.Now.AddHours(1); }
    }
    public async Task StopAsync()
    {
        _stopped = true; _automatic.Stop(); _stop.Cancel(); _main.PropertyChanged -= MainChanged;
        if (_operation is { } operation) { try { await operation; } catch (Exception) { } }
        Notify(nameof(CanOperate)); Notify(nameof(CanRestore));
    }
}
