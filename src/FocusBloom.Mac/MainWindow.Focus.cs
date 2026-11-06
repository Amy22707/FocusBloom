using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Restrictions;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private sealed record Recovery(FocusCheckpoint Timer, MacApplication[]? MacApplications = null, WebsiteRule[]? Websites = null,
        AccessListMode ApplicationMode = AccessListMode.Blacklist, AccessListMode WebsiteMode = AccessListMode.Blacklist);
    private readonly FocusTimer _timer = new();
    private readonly DispatcherTimer _pulse = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private Recovery? _recovery;
    private Guid _savedRecord;
    private DateTimeOffset _lastCheckpoint;
    private string _focusTitle = "";
    private TextBlock? _timeLabel;
    private TextBlock? _phaseLabel;
    private FocusState _renderedState;
    private Border? _focusScene;
    private bool _fullscreen;
    private WindowState _previousState;

    private void InitializeTimer()
    {
        LoadCheckpoint();
        CaptureRules();
        _pulse.Tick += async (_, _) =>
        {
            try
            {
                UpdateTimer();
                await UpdateAnimation();
                if (_sound?.HasExited == true) UpdateSound();
                EnforceRestrictions();
                RefreshWidgets();
                await CheckReminders();
                if (DateTimeOffset.Now - _lastBackupCheck >= TimeSpan.FromMinutes(1))
                { _lastBackupCheck = DateTimeOffset.Now; await RunAutomaticBackup(); }
            }
            catch (Exception error) { Report(error.Message); }
        };
        Opened += async (_, _) => { _pulse.Start(); await RunAutomaticBackup(); };
        Closing += (_, args) =>
        {
            if (OperatingSystem.IsMacOS() && !_quitting) { args.Cancel = true; Hide(); return; }
            if (_backupBusy) { args.Cancel = true; Report(T("备份正在进行。", "A backup is in progress.")); return; }
            try
            {
                if (_timer.IsActive) { _timer.End(EndReason.ApplicationExit, false); UpdateTimer(true); }
                else if (_timer.State == FocusState.Resting) Checkpoint(true);
            }
            catch (Exception error) { args.Cancel = true; Report(error.Message); }
        };
        Closed += (_, _) => { _pulse.Stop(); UpdateSound(); UpdateKeepAwake(); CloseWidgets(); foreach (var bitmap in _bitmaps) bitmap.Dispose(); };
    }

    private void LoadCheckpoint()
    {
        _recovery = null;
        if (_store.GetSetting("focus.checkpoint") is not { Length: > 0 } value) return;
        try { _recovery = JsonSerializer.Deserialize<Recovery>(value); }
        catch (JsonException) { Report(T("上次计时状态无法读取。", "Could not read the previous timer state.")); }
        if (_recovery is not { Timer: { } checkpoint }) { _recovery = null; return; }
        var saved = _store.GetRecords().Select(record => record.Id).ToHashSet();
        if (checkpoint.CurrentRecord is { } record && saved.Contains(record.Id)) _recovery = null;
        else if (checkpoint.PendingRecord is { } pending && saved.Contains(pending.Id))
            _recovery = checkpoint.State == FocusState.Ended ? null : new(checkpoint with { PendingRecord = null });
    }

    private void Checkpoint(bool force = false)
    {
        if (_recovery is not null) return;
        var now = DateTimeOffset.UtcNow;
        if (!force && now - _lastCheckpoint < TimeSpan.FromSeconds(5)) return;
        var state = _timer.CaptureCheckpoint(_timer.LastRecord is { } record && record.Id != _savedRecord);
        _store.SaveSetting("focus.checkpoint", state is null ? "" : JsonSerializer.Serialize(new Recovery(state, _sessionApps, _sessionSites, _applicationMode, _websiteMode)));
        _lastCheckpoint = now;
    }

    private void ResolveRecovery(bool resume)
    {
        if (_recovery is not { Timer: var checkpoint }) return;
        if (checkpoint.PendingRecord is { } pending) SaveRecord(pending);
        if (resume)
        {
            _sessionApps = _recovery.MacApplications ?? [];
            _sessionSites = _recovery.Websites ?? [];
            _applicationMode = _recovery.ApplicationMode;
            _websiteMode = _recovery.WebsiteMode;
            _focusTitle = checkpoint.Options.PlannerTitle ?? (checkpoint.Options.TodoId is { } id
                ? _store.GetTodos(true).FirstOrDefault(todo => todo.Id == id)?.Title : null) ?? ProjectName(checkpoint.Options.ProjectId);
            _timer.RestoreCheckpoint(checkpoint with { Options = checkpoint.Options with { Strict = checkpoint.Options.Strict && _recovery.MacApplications is not null }, PendingRecord = null });
        }
        else if (checkpoint.CurrentRecord is { } current) SaveRecord(current with { EndReason = EndReason.Interrupted });
        _recovery = null;
        Checkpoint(true);
        Refresh();
    }

    private void SaveRecord(FocusRecord record)
    {
        if (record.FocusDuration >= TimeSpan.FromSeconds(10)) _store.SaveRecord(record);
        else Report(T("不足 10 秒，本次专注未计入记录。", "Sessions shorter than 10 seconds are not recorded."));
        _savedRecord = record.Id;
        if (record.EndReason == EndReason.DurationLimit)
            _store.SaveSetting("focus.limitNotice", record.Id.ToString());
    }

    private void UpdateTimer(bool force = false)
    {
        var previous = _timer.State;
        _timer.Update();
        if (_timer.LastRecord is { } record && _savedRecord != record.Id)
        {
            // Keep the completed record recoverable until SQLite has accepted it.
            Checkpoint(true);
            SaveRecord(record);
            force = true;
        }
        if (previous != _timer.State || force) { UpdateSound(); UpdateKeepAwake(); }
        if (force || previous != _timer.State || _timer.IsActive || _timer.State == FocusState.Resting)
            Checkpoint(force || previous != _timer.State);
        if (_section != 0) return;
        if (_renderedState != _timer.State) Refresh();
        else if (_timeLabel is not null && _phaseLabel is not null) UpdateClock();
    }

    private string ProjectName(Guid id) => _store.GetProjects(true).FirstOrDefault(p => p.Id == id)?.Name ?? T("未分类", "Uncategorized");
    private string[] TimerNames => [T("正计时", "Count up"), T("倒计时", "Countdown"), T("番茄循环", "Pomodoro")];
    private bool Busy => _timer.IsActive || _timer.State == FocusState.Resting || _recovery is not null;
    private void FocusPage()
    {
        _renderedState = _timer.State;
        if (_recovery is { Timer: var checkpoint })
        {
            var actions = Row(Button(T("保存已有记录", "Save elapsed focus"), () => ResolveRecovery(false)));
            if (checkpoint.State is FocusState.Focusing or FocusState.Paused or FocusState.Resting)
                actions.Children.Insert(0, Button(T("继续计时", "Continue timer"), () => ResolveRecovery(true), true));
            _page.Children.Add(Card(Label(T("上次计时未结束", "Unfinished timer"), 17, true), actions));
        }
        if (Guid.TryParse(_store.GetSetting("focus.limitNotice"), out _))
            _page.Children.Add(Card(Label(T("正计时达到 24 小时，已自动结束。", "The count-up timer reached 24 hours and ended automatically.")),
                Button(T("知道了", "Dismiss"), () => { _store.SaveSetting("focus.limitNotice", ""); Refresh(); })));
        var project = _timer.IsActive || _timer.State == FocusState.Resting ? _focusTitle : T("留一刻，给自己", "Take a moment for yourself");
        _timeLabel = Label("00:00:00", 62, true); _timeLabel.FontFamily = new FontFamily(_store.GetSetting("mac.timerFont") is { Length: > 0 } timerFont ? timerFont : "Menlo, monospace");
        _timeLabel.FontSize = 62 * TimerScale;
        _phaseLabel = Label("");
        var quote = Label(SelectedQuote(), 16);
        var content = new StackPanel { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(20), Children = { Label(project, 25, true), _timeLabel, _phaseLabel, quote } };
        foreach (var text in content.Children.OfType<TextBlock>())
        {
            text.TextAlignment = TextAlignment.Center;
            if (text != _timeLabel)
                text.FontFamily = _store.GetSetting("mac.focusFont") is { Length: > 0 } font ? new FontFamily(font) : FontFamily.Default;
        }
        _focusScene = new Border { CornerRadius = new CornerRadius(22), MinHeight = _fullscreen ? 440 : 280, ClipToBounds = true };
        var backdrop = SelectedBackground();
        var backgroundPath = SelectedBackgroundPath();
        var animated = Path.GetExtension(backgroundPath)?.ToLowerInvariant() is ".gif" or ".mp4" or ".mov" or ".m4v";
        if (backdrop is not null || animated)
        {
            var image = new Image { Source = backdrop, Stretch = Stretch.UniformToFill };
            SetAnimation(image, backgroundPath);
            var overlay = new Border { Background = new SolidColorBrush(Color.FromArgb(104, 0, 0, 0)) };
            foreach (var text in content.Children.OfType<TextBlock>()) text.Foreground = Brushes.White;
            _focusScene.Child = new Grid { Children = { image, overlay, content } };
        }
        else
        {
            _focusScene.Background = new SolidColorBrush(Color.Parse(_store.GetSetting("mac.accent") ?? "#427B72"));
            foreach (var text in content.Children.OfType<TextBlock>()) text.Foreground = Brushes.White;
            _focusScene.Child = content;
        }
        _page.Children.Add(_focusScene);
        UpdateClock();
        var actionsRow = Row();
        if (!Busy) actionsRow.Children.Add(AsyncButton(T("开始专注", "Start focus"), () => StartFocus(), true));
        if (_timer.State == FocusState.Focusing) actionsRow.Children.Add(Button(T("暂停", "Pause"), () => { _timer.Pause(); UpdateTimer(true); }));
        if (_timer.State == FocusState.Paused) actionsRow.Children.Add(Button(T("继续", "Resume"), () => { _timer.Resume(); UpdateTimer(true); }, true));
        if (_timer.IsActive || _timer.IsPomodoro && _timer.State == FocusState.Resting)
            actionsRow.Children.Add(Button(T("结束", "End"), () => { _timer.End(); UpdateTimer(true); }));
        if (_timer.State == FocusState.Resting) actionsRow.Children.Add(Button(T("跳过休息", "Skip break"), () => { _timer.SkipRest(); UpdateTimer(true); }));
        actionsRow.Children.Add(Button(_fullscreen ? T("退出全屏", "Exit full screen") : T("全屏", "Full screen"), ToggleFullScreen));
        actionsRow.Children.Add(AsyncButton(T("保存截图", "Save screenshot"), CaptureFocus));
        _page.Children.Add(actionsRow);
    }

    private void UpdateClock()
    {
        _timeLabel!.Text = Clock(_timer.State == FocusState.Resting ? _timer.PhaseRemaining : _timer.DisplayTime);
        _phaseLabel!.Text = _timer.State switch
        {
            FocusState.Focusing => _timer.IsPomodoro ? T($"第 {_timer.Round} / {_timer.TotalRounds} 轮", $"Round {_timer.Round} / {_timer.TotalRounds}") : T("专注中", "Focusing"),
            FocusState.Paused => T("暂停 · ", "Paused · ") + Clock(_timer.PhaseRemaining),
            FocusState.Resting => T("休息中", "On a break"),
            FocusState.Ended => T("已结束", "Finished"),
            _ => T("准备开始", "Ready")
        };
    }

    private async Task StartFocus(TodoItem? todo = null, ChecklistEntry? plan = null, FocusProject? project = null)
    {
        if (Busy) { Navigate(0); Report(T("请先结束当前计时或处理上次的记录。", "Finish the current timer or handle the previous session first.")); return; }
        var projects = _store.GetProjects();
        var requested = project?.Id ?? todo?.PreferredProjectId ?? SqliteFocusStore.DefaultProjectId;
        var selected = Math.Max(0, projects.ToList().FindIndex(p => p.Id == requested));
        var picker = Choice(projects.Select(p => p.Name), selected);
        var mode = Choice(TimerNames, Enum.TryParse<TimerMode>(_store.GetSetting("timer.mode"), out var saved) ? (int)saved : 0);
        var duration = Input(_store.GetSetting("timer.durationMinutes") ?? "25");
        var pause = Input(_store.GetSetting("pauseMinutes") ?? "5");
        var rest = Input(_store.GetSetting("restMinutes") ?? "0");
        var rounds = Input(_store.GetSetting("pomodoro.rounds") ?? "4");
        var longRest = Input(_store.GetSetting("pomodoro.longRestMinutes") ?? "15");
        var longEvery = Input(_store.GetSetting("pomodoro.longRestEvery") ?? "4");
        var strict = new CheckBox { Content = T("严格模式", "Strict focus"), IsChecked = _store.GetSetting("timer.strict") == "True" };
        var durationField = Field(T("专注分钟数", "Focus minutes"), duration);
        var roundsField = Field(T("番茄循环", "Pomodoro cycle"), Row(Field(T("轮数", "Rounds"), rounds), Field(T("长休息分钟", "Long break minutes"), longRest), Field(T("长休息间隔", "Long break interval"), longEvery)));
        void ShowMode() { durationField.IsVisible = mode.SelectedIndex != 0; roundsField.IsVisible = mode.SelectedIndex == 2; }
        mode.SelectionChanged += (_, _) => ShowMode(); ShowMode();
        var result = await Form(todo?.Title ?? plan?.Text ?? T("开始专注", "Start focus"),
            [Field(T("项目", "Project"), picker), Field(T("计时方式", "Timer mode"), mode), durationField,
                Field(T("暂停分钟数", "Pause minutes"), pause), Field(T("休息分钟数", "Break minutes"), rest), roundsField, strict], () =>
            {
                var type = (TimerMode)mode.SelectedIndex;
                var minutes = type == TimerMode.CountUp ? 0 : Minutes(duration, 1d / 6, 1440);
                var pauseMinutes = Minutes(pause, 1d / 60, 1440);
                var restMinutes = Minutes(rest, 0, 1440);
                var pomodoro = type == TimerMode.Pomodoro ? new PomodoroOptions(TimeSpan.FromMinutes(Minutes(longRest, 0, 1440)), Number(longEvery, 1, 100), Number(rounds, 1, 100)) : null;
                var target = projects[picker.SelectedIndex];
                CaptureRules();
                _timer.Start(new(target.Id, type, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(pauseMinutes), TimeSpan.FromMinutes(restMinutes),
                    Strict: strict.IsChecked == true, Pomodoro: pomodoro, TodoId: todo?.Id, PlannerTitle: plan?.Text, PlannerItemId: plan?.Id));
                _focusTitle = todo?.Title ?? plan?.Text ?? target.Name;
                _store.SaveSetting("timer.mode", type.ToString());
                _store.SaveSetting("timer.strict", (strict.IsChecked == true).ToString());
                if (type != TimerMode.CountUp) _store.SaveSetting("timer.durationMinutes", minutes.ToString(CultureInfo.InvariantCulture));
                _store.SaveSetting("pauseMinutes", pauseMinutes.ToString(CultureInfo.InvariantCulture));
                _store.SaveSetting("restMinutes", restMinutes.ToString(CultureInfo.InvariantCulture));
                if (pomodoro is not null)
                {
                    _store.SaveSetting("pomodoro.rounds", pomodoro.Rounds.ToString(CultureInfo.InvariantCulture));
                    _store.SaveSetting("pomodoro.longRestMinutes", pomodoro.LongRestDuration.TotalMinutes.ToString(CultureInfo.InvariantCulture));
                    _store.SaveSetting("pomodoro.longRestEvery", pomodoro.LongRestEvery.ToString(CultureInfo.InvariantCulture));
                }
                Checkpoint(true);
            }, T("开始", "Start"));
        if (result) { UpdateSound(); UpdateKeepAwake(); Navigate(0); }
    }

    private double Minutes(TextBox input, double min, double max)
    {
        var value = Text(input);
        if ((double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) ||
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)) && parsed >= min && parsed <= max) return parsed;
        throw new FormatException(T("分钟数超出范围。倒计时至少 10 秒，各阶段最长 24 小时。", "Invalid duration. Countdowns start at 10 seconds, and each phase can last up to 24 hours."));
    }

    private void ToggleFullScreen()
    {
        if (!_fullscreen) { _previousState = WindowState; WindowState = WindowState.FullScreen; }
        else WindowState = _previousState;
        _fullscreen = !_fullscreen;
        Refresh();
    }

    private async Task CaptureFocus()
    {
        if (_focusScene is not { Bounds.Width: > 0, Bounds.Height: > 0 } scene) return;
        var name = "FocusBloom-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png";
        var directory = _store.GetSetting("mac.screenshotDirectory") ?? Path.Combine(_data, "screenshots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        var pixels = new PixelSize((int)Math.Ceiling(scene.Bounds.Width), (int)Math.Ceiling(scene.Bounds.Height));
        using var bitmap = new RenderTargetBitmap(pixels);
        bitmap.Render(scene);
        bitmap.Save(path, new PngBitmapEncoderOptions());
        Report(T("截图已保存到 ", "Screenshot saved to ") + path);
        var message = _notice.Text;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (_notice.Text == message) _notice.Text = "";
    }
}
