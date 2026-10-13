using FocusBloom.Core.Focus;
using Xunit;

namespace FocusBloom.Tests;

public sealed class FocusTimerTests
{
    [Fact]
    public void InterceptionsCountOnlyStrictFocusAndPauseAndResetForNextRound()
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(CycleOptions(rounds: 2));
        timer.RegisterInterception(); timer.Pause(); timer.RegisterInterception(); timer.Resume();
        clock.Advance(TimeSpan.FromSeconds(20)); timer.Update();
        Assert.Equal(2, timer.LastRecord!.InterceptionCount);
        timer.RegisterInterception(); Assert.Equal(2, timer.LastRecord.InterceptionCount);
        timer.SkipRest(); Assert.Equal(0, timer.InterceptionCount);
        timer.RegisterInterception(); clock.Advance(TimeSpan.FromSeconds(20)); timer.Update();
        Assert.Equal(1, timer.LastRecord!.InterceptionCount);
        timer.Start(Options()); timer.RegisterInterception(); clock.Advance(TimeSpan.FromSeconds(10)); timer.End();
        Assert.Equal(0, timer.LastRecord!.InterceptionCount);
    }

    private static FocusOptions CycleOptions(int rounds = 5, int restSeconds = 3) =>
        new(Guid.NewGuid(), TimerMode.Pomodoro, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(restSeconds), true, new PomodoroOptions(TimeSpan.FromSeconds(6), 2, rounds));

    [Fact]
    public void PomodoroAlternatesShortAndLongRestAndEndsAfterLastRound()
    {
        var clock = new ManualClock();
        var timer = new FocusTimer(clock);
        timer.Start(CycleOptions());
        var records = new List<FocusRecord>();
        for (var round = 1; round <= 5; round++)
        {
            Assert.Equal(round, timer.Round);
            Assert.True(timer.RestrictionsActive);
            Assert.Equal(TimeSpan.FromSeconds(20), timer.DisplayTime);
            clock.Advance(TimeSpan.FromSeconds(20));
            timer.Update();
            records.Add(timer.LastRecord!);
            Assert.Equal(TimeSpan.FromSeconds(20), timer.LastRecord!.FocusDuration);
            Assert.False(timer.RestrictionsActive);
            if (round == 5) break;
            Assert.Equal(FocusState.Resting, timer.State);
            Assert.True(timer.IsRunning);
            Assert.Equal(round % 2 == 0, timer.IsLongRest);
            var rest = TimeSpan.FromSeconds(round % 2 == 0 ? 6 : 3);
            Assert.Equal(rest, timer.DisplayTime);
            clock.Advance(rest);
            timer.Update();
        }
        Assert.Equal(FocusState.Ended, timer.State);
        Assert.False(timer.IsRunning);
        Assert.Equal(5, records.Select(record => record.Id).Distinct().Count());
        Assert.All(records, record => Assert.Equal(TimerMode.Pomodoro, record.Mode));
    }

    [Fact]
    public void PomodoroPauseKeepsRestrictionsAndDoesNotConsumeFocusTime()
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(CycleOptions());
        clock.Advance(TimeSpan.FromSeconds(8)); timer.Pause();
        clock.Advance(TimeSpan.FromSeconds(5)); timer.Update();
        Assert.True(timer.RestrictionsActive);
        Assert.Equal(TimeSpan.FromSeconds(12), timer.DisplayTime);
        clock.Advance(TimeSpan.FromSeconds(12)); timer.Update();
        Assert.Equal(TimeSpan.FromSeconds(20), timer.LastRecord!.FocusDuration);
        Assert.Equal(TimeSpan.FromSeconds(5), timer.LastRecord.Intervals[1].Duration);
        Assert.Equal(FocusState.Resting, timer.State);
    }

    [Fact]
    public void PomodoroWakeStartsOnlyOneNewRoundAtCurrentTime()
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(CycleOptions());
        var start = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromHours(3)); timer.Update();
        Assert.Equal(start + TimeSpan.FromSeconds(20), timer.LastRecord!.EndedAt);
        Assert.Equal(FocusState.Resting, timer.State);
        var record = timer.LastRecord;
        timer.Update(allowNextRound: false);
        Assert.Equal(FocusState.Resting, timer.State);
        timer.Update();
        Assert.Equal(2, timer.Round);
        Assert.Equal(TimeSpan.Zero, timer.Elapsed);
        Assert.Same(record, timer.LastRecord);
        timer.Update();
        Assert.Equal(2, timer.Round);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EndingPomodoroStopsTheLoopDuringFocusOrRest(bool duringRest)
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(CycleOptions());
        clock.Advance(TimeSpan.FromSeconds(duringRest ? 20 : 12));
        if (duringRest) timer.Update();
        timer.End();
        var record = timer.LastRecord;
        Assert.Equal(FocusState.Ended, timer.State);
        clock.Advance(TimeSpan.FromHours(1)); timer.Update();
        Assert.Same(record, timer.LastRecord);
        Assert.Equal(1, timer.Round);
    }

    [Fact]
    public void ZeroRestAndSkippedRestStartNextRoundWithoutReplacingCompletedRecord()
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(CycleOptions(rounds: 3, restSeconds: 0));
        clock.Advance(TimeSpan.FromSeconds(20)); timer.Update();
        var record = timer.LastRecord;
        Assert.Equal(FocusState.Resting, timer.State);
        timer.Update();
        Assert.Equal(2, timer.Round); Assert.Same(record, timer.LastRecord);
        clock.Advance(TimeSpan.FromSeconds(20)); timer.Update();
        Assert.True(timer.IsLongRest);
        record = timer.LastRecord;
        timer.SkipRest();
        Assert.Equal(3, timer.Round); Assert.Same(record, timer.LastRecord);
        Assert.True(timer.RestrictionsActive);
        timer.End(EndReason.ApplicationExit, false);
        Assert.Equal(FocusState.Ended, timer.State);
        Assert.Equal(EndReason.ApplicationExit, timer.LastRecord!.EndReason);
    }

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
