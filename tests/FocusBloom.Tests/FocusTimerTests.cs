using FocusBloom.Core.Focus;
using Xunit;

namespace FocusBloom.Tests;

public sealed class FocusTimerTests
{
    private static FocusOptions Options(TimerMode mode = TimerMode.CountUp, int restMinutes = 0) =>
        new(Guid.NewGuid(), mode, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(restMinutes));

    [Fact]
    public void CountUpKeepsRunningPastTwentyFiveMinutesAndExcludesPause()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(Options());
        clock.Advance(TimeSpan.FromMinutes(30));
        timer.Pause();
        clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(TimeSpan.FromMinutes(30), timer.Elapsed);
        timer.Resume();
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.End();

        Assert.Equal(FocusState.Ended, timer.State);
        var record = Assert.IsType<FocusRecord>(timer.LastRecord);
        Assert.Equal(TimeSpan.FromMinutes(31), record.FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(33), record.EndedAt - record.StartedAt);
        Assert.Collection(record.Intervals,
            i => Assert.Equal(IntervalKind.Focus, i.Kind),
            i => Assert.Equal(TimeSpan.FromMinutes(2), i.Duration),
            i => Assert.Equal(IntervalKind.Focus, i.Kind));
    }

    [Fact]
    public void SleepDuringCountdownEndsAtScheduledTimeAndConsumesRest()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(Options(TimerMode.Countdown, 5));
        var start = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(2));
        timer.Update();

        Assert.Equal(FocusState.Ready, timer.State);
        Assert.Equal(start + TimeSpan.FromMinutes(25), timer.LastRecord!.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.LastRecord.FocusDuration);
        Assert.Equal(EndReason.CountdownElapsed, timer.LastRecord.EndReason);
        timer.Update();
        Assert.Single(timer.LastRecord.Intervals);
    }

    [Fact]
    public void ExpiredPauseResumesAtItsDeadlineWhenNotPolled()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(Options(TimerMode.Countdown));
        var start = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromMinutes(10));
        timer.Pause();
        clock.Advance(TimeSpan.FromMinutes(40));
        timer.Update();

        Assert.Equal(start + TimeSpan.FromMinutes(30), timer.LastRecord!.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(25), timer.LastRecord.FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(5), timer.LastRecord.Intervals[1].Duration);
    }

    [Fact]
    public void EndingWhilePausedClosesPauseAndCanSkipRest()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(Options(restMinutes: 5));
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.Pause();
        clock.Advance(TimeSpan.FromMinutes(1));
        timer.End();

        Assert.Equal(FocusState.Resting, timer.State);
        Assert.Equal(TimeSpan.FromMinutes(1), timer.LastRecord!.FocusDuration);
        Assert.Equal(IntervalKind.Pause, timer.LastRecord.Intervals.Last().Kind);
        var id = timer.LastRecord.Id;
        timer.End();
        Assert.Equal(id, timer.LastRecord.Id);
        timer.SkipRest();
        timer.Start(Options());
        Assert.Equal(FocusState.Focusing, timer.State);
        Assert.Null(timer.LastRecord);
    }

    [Fact]
    public void ApplicationExitPreservesSleepTimeWithoutRest()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(Options(restMinutes: 5));
        clock.Advance(TimeSpan.FromHours(3));
        timer.End(EndReason.ApplicationExit, false);
        Assert.Equal(TimeSpan.FromHours(3), timer.LastRecord!.FocusDuration);
        Assert.Equal(EndReason.ApplicationExit, timer.LastRecord.EndReason);
        Assert.Equal(FocusState.Ended, timer.State);
    }
}
