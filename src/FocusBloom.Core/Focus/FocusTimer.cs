namespace FocusBloom.Core.Focus;

public sealed class FocusTimer(TimeProvider? timeProvider = null)
{
    public static readonly TimeSpan MaximumCountUpDuration = TimeSpan.FromHours(24);
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly List<FocusInterval> _intervals = [];
    private FocusOptions? _options;
    private Guid _sessionId;
    private DateTimeOffset _startedAt;
    private DateTimeOffset _phaseStartedAt;
    private DateTimeOffset _phaseEndsAt;

    public FocusState State { get; private set; } = FocusState.Ready;
    public FocusRecord? LastRecord { get; private set; }
    public bool IsActive => State is FocusState.Focusing or FocusState.Paused;
    public bool IsPomodoro => _options?.Mode == TimerMode.Pomodoro;
    public bool IsRunning => IsActive || (IsPomodoro && State == FocusState.Resting);
    public Guid SessionId => _sessionId;
    public int Round { get; private set; }
    public int TotalRounds => _options?.Pomodoro?.Rounds ?? 1;
    public bool IsLongRest { get; private set; }
    public int InterceptionCount { get; private set; }
    public void RegisterInterception() { if (RestrictionsActive) InterceptionCount++; }
    public bool RestrictionsActive => IsActive && _options?.Strict == true;
    public bool IsStrictSession => _options?.Strict == true;
    public TimeSpan Elapsed => ElapsedAt(_clock.GetUtcNow());
    public FocusRecord? CaptureCurrentRecord()
    {
        if (!IsActive) return null;
        var now = _clock.GetUtcNow();
        var intervals = _intervals.Append(new FocusInterval(State == FocusState.Paused ? IntervalKind.Pause : IntervalKind.Focus, _phaseStartedAt, now)).ToArray();
        return new(_sessionId, _options!.ProjectId, _options.Mode, _startedAt, now, EndReason.Manual, intervals, _options.Strict, _options.TodoId, InterceptionCount);
    }
    public FocusCheckpoint? CaptureCheckpoint(bool includeLastRecord = false)
    {
        if (_options is null || (!IsActive && State != FocusState.Resting && !includeLastRecord)) return null;
        return new(_options, State, Round, IsLongRest, _clock.GetUtcNow(), PhaseRemaining,
            CaptureCurrentRecord(), includeLastRecord ? LastRecord : null);
    }

    public void RestoreCheckpoint(FocusCheckpoint checkpoint)
    {
        if (IsActive || State == FocusState.Resting) throw new InvalidOperationException("请先结束当前计时。");
        var now = _clock.GetUtcNow();
        _options = checkpoint.Options;
        State = checkpoint.State;
        Round = checkpoint.Round;
        IsLongRest = checkpoint.IsLongRest;
        LastRecord = checkpoint.PendingRecord;
        _intervals.Clear();
        if (checkpoint.CurrentRecord is { } record)
        {
            _sessionId = record.Id;
            _startedAt = record.StartedAt;
            _intervals.AddRange(record.Intervals);
            // 自动保存之后到主动恢复之间的时间不计入专注。
            if (now > record.EndedAt) _intervals.Add(new(IntervalKind.Pause, record.EndedAt, now));
            InterceptionCount = record.InterceptionCount ?? 0;
        }
        _phaseStartedAt = now;
        _phaseEndsAt = now + checkpoint.PhaseRemaining;
    }
    public TimeSpan DisplayTime => IsPomodoro && State == FocusState.Resting ? PhaseRemaining
        : _options?.Mode is TimerMode.Countdown or TimerMode.Pomodoro
        ? MaxZero(_options.Duration - Elapsed)
        : Elapsed;
    public TimeSpan PhaseRemaining => State is FocusState.Paused or FocusState.Resting
        ? MaxZero(_phaseEndsAt - _clock.GetUtcNow())
        : TimeSpan.Zero;

    public void Start(FocusOptions options)
    {
        if (State is not (FocusState.Ready or FocusState.Ended))
            throw new InvalidOperationException("请先结束当前专注或跳过休息。");
        if (options.Mode is TimerMode.Countdown or TimerMode.Pomodoro && options.Duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "倒计时时长必须大于零。");
        if (options.PauseDuration <= TimeSpan.Zero || options.RestDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "暂停须大于零，休息不能小于零。");
        if (options.Mode == TimerMode.Pomodoro && (options.Pomodoro is not { } cycle
            || cycle.LongRestDuration < TimeSpan.Zero || cycle.LongRestEvery is < 1 or > 100 || cycle.Rounds is < 1 or > 100))
            throw new ArgumentOutOfRangeException(nameof(options), "请设置有效的番茄循环时长和轮数。");

        _options = options;
        Round = 1;
        IsLongRest = false;
        LastRecord = null;
        BeginRound(_clock.GetUtcNow());
    }

    private void BeginRound(DateTimeOffset now)
    {
        InterceptionCount = 0;
        _sessionId = Guid.NewGuid();
        _startedAt = _phaseStartedAt = now;
        _intervals.Clear();
        State = FocusState.Focusing;
    }

    public void Pause()
    {
        Update();
        if (State != FocusState.Focusing) return;
        var now = _clock.GetUtcNow();
        CloseInterval(IntervalKind.Focus, now);
        _phaseEndsAt = now + _options!.PauseDuration;
        State = FocusState.Paused;
    }

    public void Resume()
    {
        Update();
        if (State != FocusState.Paused) return;
        CloseInterval(IntervalKind.Pause, _clock.GetUtcNow());
        State = FocusState.Focusing;
    }

    public void End(EndReason reason = EndReason.Manual, bool takeRest = true)
    {
        if (IsPomodoro && State == FocusState.Resting) { State = FocusState.Ended; return; }
        Update();
        if (IsPomodoro && State == FocusState.Resting) { State = FocusState.Ended; return; }
        if (!IsActive) return;
        Complete(_clock.GetUtcNow(), reason, takeRest);
    }

    public void SkipRest()
    {
        if (State != FocusState.Resting) return;
        if (IsPomodoro) { Round++; BeginRound(_clock.GetUtcNow()); }
        else State = FocusState.Ready;
    }

    // 界面刷新只是触发计算；区间终点由真实时间点决定，包含睡眠时间。
    public void Update(bool allowNextRound = true)
    {
        var now = _clock.GetUtcNow();
        if (State == FocusState.Paused && now >= _phaseEndsAt)
        {
            CloseInterval(IntervalKind.Pause, _phaseEndsAt);
            State = FocusState.Focusing;
        }
        if (State == FocusState.Focusing && _options!.Mode == TimerMode.CountUp)
        {
            var deadline = _phaseStartedAt + MaxZero(MaximumCountUpDuration - SumFocus());
            if (now >= deadline)
            {
                Complete(deadline, EndReason.DurationLimit, false);
            }
        }
        if (State == FocusState.Focusing && _options!.Mode is TimerMode.Countdown or TimerMode.Pomodoro)
        {
            var completed = SumFocus();
            var deadline = _phaseStartedAt + (_options.Duration - completed);
            if (now >= deadline)
            {
                Complete(deadline, EndReason.CountdownElapsed, true);
                if (IsPomodoro) return;
            }
        }
        if (State == FocusState.Resting && now >= _phaseEndsAt)
        {
            if (IsPomodoro)
            {
                if (!allowNextRound) return;
                Round++;
                BeginRound(now);
            }
            else State = FocusState.Ready;
        }
    }

    private void Complete(DateTimeOffset endedAt, EndReason reason, bool takeRest)
    {
        CloseInterval(State == FocusState.Paused ? IntervalKind.Pause : IntervalKind.Focus, endedAt);
        LastRecord = new FocusRecord(_sessionId, _options!.ProjectId, _options.Mode,
            _startedAt, endedAt, reason, _intervals.ToArray(), _options.Strict, _options.TodoId, InterceptionCount);
        if (IsPomodoro)
        {
            if (reason != EndReason.CountdownElapsed || !takeRest || Round >= TotalRounds)
            { State = FocusState.Ended; return; }
            IsLongRest = Round % _options.Pomodoro!.LongRestEvery == 0;
            _phaseEndsAt = endedAt + (IsLongRest ? _options.Pomodoro.LongRestDuration : _options.RestDuration);
            State = FocusState.Resting;
        }
        else if (takeRest && _options.RestDuration > TimeSpan.Zero)
        {
            _phaseEndsAt = endedAt + _options.RestDuration;
            State = FocusState.Resting;
        }
        else State = FocusState.Ended;
    }

    private void CloseInterval(IntervalKind kind, DateTimeOffset endedAt)
    {
        _intervals.Add(new FocusInterval(kind, _phaseStartedAt, endedAt));
        _phaseStartedAt = endedAt;
    }

    private TimeSpan ElapsedAt(DateTimeOffset now) => SumFocus()
        + (State == FocusState.Focusing ? MaxZero(now - _phaseStartedAt) : TimeSpan.Zero);

    private TimeSpan SumFocus() => TimeSpan.FromTicks(
        _intervals.Where(i => i.Kind == IntervalKind.Focus).Sum(i => i.Duration.Ticks));

    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}
