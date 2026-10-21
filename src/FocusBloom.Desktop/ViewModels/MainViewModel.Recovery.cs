using System;
using System.Linq;
using System.Text.Json;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Restrictions;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record RecoveryEntry(FocusCheckpoint Timer, AllowedApplication[] Applications,
        WebsiteRule[] Websites, AccessListMode ApplicationMode, AccessListMode WebsiteMode);
    private const string CheckpointKey = "focus.checkpoint";
    private RecoveryEntry? _recovery;
    private bool _checkpointExists;
    private DateTimeOffset _checkpointAt;
    private FocusState _checkpointState;
    private Guid _checkpointSession;
    private Guid? _checkpointSaved;
    private int _checkpointInterceptions;
    private Guid? _limitNoticeId;
    private Guid? _lastLimitNoticeId;
    public bool HasLimitNotice => _limitNoticeId is not null;
    public string LimitNoticeMessage => "上次正计时已达到24小时上限并自动结束，限制与常亮已解除。"
        + (HasUnsavedRecord ? "记录尚未保存，请在主窗口重试保存。" : "可在专注记录中查看。");

    private void RememberLimitNotice(FocusRecord record)
    {
        if (record.EndReason != EndReason.DurationLimit || record.Id == _lastLimitNoticeId) return;
        _lastLimitNoticeId = _limitNoticeId = record.Id;
        try { _store.SaveSetting("focus.limitNotice", record.Id.ToString()); }
        catch (Exception ex) { StatusMessage = $"上限提示状态保存失败：{ex.Message}"; }
    }

    public void AcknowledgeLimitNotice()
    {
        try { _store.SaveSetting("focus.limitNotice", ""); }
        catch (Exception ex) { StatusMessage = $"提示状态保存失败：{ex.Message}"; }
        _limitNoticeId = null;
    }

    public bool HasPendingRecovery => _recovery is not null;
    public string RecoveryProject => _recovery is { } entry
        ? _store.GetProjects(includeDeleted: true).FirstOrDefault(p => p.Id == entry.Timer.Options.ProjectId)?.Name ?? "未分类" : "";
    public string RecoveryDetails => _recovery is { Timer: var checkpoint }
        ? $"{RecoveryProject} · {checkpoint.Options.Mode switch { TimerMode.CountUp => "正计时", TimerMode.Countdown => "倒计时", _ => $"番茄第 {checkpoint.Round} / {checkpoint.Options.Pomodoro!.Rounds} 轮" }}\n"
            + (checkpoint.State == FocusState.Resting ? $"休息剩余 {Format(checkpoint.PhaseRemaining)}\n"
                : $"已确认有效专注 {Format(checkpoint.CurrentRecord?.FocusDuration ?? checkpoint.PendingRecord?.FocusDuration ?? TimeSpan.Zero)}\n")
            + $"最后自动保存：{checkpoint.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss}"
        : "";
    public bool CanContinueRecovery => _recovery?.Timer.State is FocusState.Focusing or FocusState.Paused or FocusState.Resting;
    public string RecoveryContinueText => _recovery?.Timer.State switch
    { FocusState.Paused => "恢复暂停", FocusState.Resting => "继续休息", _ => "继续专注" };

    private void LoadRecovery()
    {
        if (Guid.TryParse(_store.GetSetting("focus.limitNotice"), out var noticeId)) _lastLimitNoticeId = _limitNoticeId = noticeId;
        var json = _store.GetSetting(CheckpointKey);
        if (string.IsNullOrEmpty(json)) return;
        _checkpointExists = true;
        try
        {
            _recovery = JsonSerializer.Deserialize<RecoveryEntry>(json);
            if (_recovery is null) return;
            var saved = _store.GetRecords().Select(record => record.Id).ToHashSet();
            // 正式记录已落库时，不能用较早的自动保存覆盖它。
            if (_recovery.Timer.CurrentRecord is { } current && saved.Contains(current.Id)) _recovery = null;
            else if (_recovery.Timer.PendingRecord is { } pending && saved.Contains(pending.Id))
                _recovery = _recovery with { Timer = _recovery.Timer with { PendingRecord = null } };
            if (_recovery?.Timer is { State: FocusState.Ended, PendingRecord: null }) _recovery = null;
            if (HasPendingRecovery) StatusMessage = "发现上次未结束的专注，请选择恢复或保存。";
        }
        catch (JsonException) { StatusMessage = "上次计时的自动保存无法读取，已完成的专注记录不受影响。"; }
    }

    private bool SaveCheckpoint(bool force = false)
    {
        if (HasPendingRecovery) return true;
        var checkpoint = _timer.CaptureCheckpoint(HasUnsavedRecord);
        try
        {
            if (checkpoint is null)
            {
                if (_checkpointExists) { _store.SaveSetting(CheckpointKey, ""); _checkpointExists = false; }
                return true;
            }
            if (!force && _checkpointExists && checkpoint.CapturedAt - _checkpointAt < TimeSpan.FromSeconds(5)
                && _checkpointState == State && _checkpointSession == _timer.SessionId
                && _checkpointSaved == _savedRecordId && _checkpointInterceptions == _timer.InterceptionCount) return true;
            var entry = new RecoveryEntry(checkpoint, _sessionApplications, _sessionWebsites, SessionApplicationMode, SessionWebsiteMode);
            _store.SaveSetting(CheckpointKey, JsonSerializer.Serialize(entry));
            _checkpointExists = true;
            _checkpointAt = checkpoint.CapturedAt; _checkpointState = State; _checkpointSession = _timer.SessionId;
            _checkpointSaved = _savedRecordId; _checkpointInterceptions = _timer.InterceptionCount;
            return true;
        }
        catch (Exception ex) { StatusMessage = $"计时自动保存失败：{ex.Message}"; return false; }
    }

    public bool ResolveRecovery(bool continueTimer, bool discard = false)
    {
        if (_recovery is not { } entry || continueTimer && !CanContinueRecovery) return false;
        try
        {
            var checkpoint = entry.Timer;
            if (!discard)
            {
                if (checkpoint.PendingRecord is { } pending && pending.FocusDuration >= TimeSpan.FromSeconds(10))
                {
                    RememberLimitNotice(pending);
                    _store.SaveRecord(pending);
                }
                if (!continueTimer && checkpoint.CurrentRecord is { } current && current.FocusDuration >= TimeSpan.FromSeconds(10))
                    _store.SaveRecord(current with { EndReason = EndReason.Interrupted });
            }
            if (continueTimer)
            {
                _timer.RestoreCheckpoint(checkpoint);
                _savedRecordId = checkpoint.PendingRecord?.Id;
                _activeProjectId = checkpoint.Options.ProjectId;
                _activeTodoId = checkpoint.Options.TodoId;
                ActiveProjectName = RecoveryProject;
                _activeTodoTitle = _store.GetTodos(includeDeleted: true).FirstOrDefault(todo => todo.Id == _activeTodoId)?.Title ?? "";
                _sessionApplications = entry.Applications; _sessionWebsites = entry.Websites;
                SessionApplicationMode = entry.ApplicationMode; SessionWebsiteMode = entry.WebsiteMode;
                _mediaSessionId = _timer.SessionId;
                if (_timer.IsActive) Media.BeginSession();
            }
            else _store.SaveSetting(CheckpointKey, "");
            _recovery = null;
            _checkpointExists = continueTimer;
            var checkpointSaved = !continueTimer || SaveCheckpoint(force: true);
            LoadRecords(); Refresh();
            Notify(nameof(HasPendingRecovery));
            if (checkpointSaved) StatusMessage = continueTimer ? "计时已恢复，中断期间未计入专注。"
                : discard ? "已放弃这次未完成的计时。"
                : "已保存中断前的记录；有效专注不足10秒的部分未计入。";
            return true;
        }
        catch (Exception ex) { StatusMessage = $"恢复处理失败，请重试：{ex.Message}"; return false; }
    }
}
