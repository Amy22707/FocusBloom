namespace FocusBloom.Core.Focus;

public enum TimerMode { CountUp, Countdown }
public enum FocusState { Ready, Focusing, Paused, Resting, Ended }
public enum IntervalKind { Focus, Pause }
public enum EndReason { Manual, CountdownElapsed, ApplicationExit }

public sealed record FocusOptions(
    Guid ProjectId,
    TimerMode Mode,
    TimeSpan Duration,
    TimeSpan PauseDuration,
    TimeSpan RestDuration,
    bool Strict = false);

public sealed record FocusInterval(IntervalKind Kind, DateTimeOffset StartedAt, DateTimeOffset EndedAt)
{
    public TimeSpan Duration => EndedAt - StartedAt;
}

public sealed record FocusRecord(
    Guid Id,
    Guid ProjectId,
    TimerMode Mode,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    EndReason EndReason,
    IReadOnlyList<FocusInterval> Intervals,
    bool Strict = false)
{
    public TimeSpan FocusDuration => TimeSpan.FromTicks(
        Intervals.Where(i => i.Kind == IntervalKind.Focus).Sum(i => i.Duration.Ticks));
}
