namespace FocusBloom.Core.Focus;

public enum TimerMode { CountUp, Countdown, Pomodoro }
public enum FocusState { Ready, Focusing, Paused, Resting, Ended }
public enum IntervalKind { Focus, Pause }
public enum EndReason { Manual, CountdownElapsed, ApplicationExit }

public sealed record PomodoroOptions(TimeSpan LongRestDuration, int LongRestEvery = 4, int Rounds = 4);

public sealed record FocusOptions(
    Guid ProjectId,
    TimerMode Mode,
    TimeSpan Duration,
    TimeSpan PauseDuration,
    TimeSpan RestDuration,
    bool Strict = false,
    PomodoroOptions? Pomodoro = null,
    Guid? TodoId = null);

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
    bool Strict = false,
    Guid? TodoId = null,
    int? InterceptionCount = 0)
{
    public TimeSpan FocusDuration => TimeSpan.FromTicks(
        Intervals.Where(i => i.Kind == IntervalKind.Focus).Sum(i => i.Duration.Ticks));
}

public sealed record FocusOutcome(Guid SessionId, string Summary = "", double? Quantity = null,
    string Unit = "", string NextStep = "");
