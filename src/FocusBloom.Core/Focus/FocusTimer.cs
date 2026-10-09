namespace FocusBloom.Core.Focus;

public sealed class FocusTimer(TimeProvider? timeProvider = null)
{
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
    public bool RestrictionsActive => IsActive && _options?.Strict == true;
    public bool IsStrictSession => _options?.Strict == true;
    public TimeSpan Elapsed => ElapsedAt(_clock.GetUtcNow());
    public TimeSpan DisplayTime => _options?.Mode == TimerMode.Countdown
        ? MaxZero(_options.Duration - Elapsed)
        : Elapsed;
    public TimeSpan PhaseRemaining => State is FocusState.Paused or FocusState.Resting
        ? MaxZero(_phaseEndsAt - _clock.GetUtcNow())
        : TimeSpan.Zero;

    public void Start(FocusOptions options)
    {
        if (State is not (FocusState.Ready or FocusState.Ended))
            throw new InvalidOperationException("请先结束当前专注或跳过休息。");
        if (options.Mode == TimerMode.Countdown && options.Duration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "倒计时时长必须大于零。");
        if (options.PauseDuration <= TimeSpan.Zero || options.RestDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "暂停须大于零，休息不能小于零。");

        _options = options;
        _sessionId = Guid.NewGuid();
        _startedAt = _phaseStartedAt = _clock.GetUtcNow();
        _intervals.Clear();
        LastRecord = null;
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
        Update();
        if (!IsActive) return;
        Complete(_clock.GetUtcNow(), reason, takeRest);
    }

    public void SkipRest()
    {
        if (State == FocusState.Resting) State = FocusState.Ready;
    }

    // 界面刷新只是触发计算；区间终点由真实时间点决定，包含睡眠时间。
    public void Update()
    {
        var now = _clock.GetUtcNow();
        if (State == FocusState.Paused && now >= _phaseEndsAt)
        {
            CloseInterval(IntervalKind.Pause, _phaseEndsAt);
            State = FocusState.Focusing;
        }
        if (State == FocusState.Focusing && _options!.Mode == TimerMode.Countdown)
        {
            var completed = SumFocus();
            var deadline = _phaseStartedAt + (_options.Duration - completed);
            if (now >= deadline) Complete(deadline, EndReason.CountdownElapsed, true);
        }
        if (State == FocusState.Resting && now >= _phaseEndsAt)
            State = FocusState.Ready;
    }

    private void Complete(DateTimeOffset endedAt, EndReason reason, bool takeRest)
    {
        CloseInterval(State == FocusState.Paused ? IntervalKind.Pause : IntervalKind.Focus, endedAt);
        LastRecord = new FocusRecord(_sessionId, _options!.ProjectId, _options.Mode,
            _startedAt, endedAt, reason, _intervals.ToArray(), _options.Strict);
        if (takeRest && _options.RestDuration > TimeSpan.Zero)
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
