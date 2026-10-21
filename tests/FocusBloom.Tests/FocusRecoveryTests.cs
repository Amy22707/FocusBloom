using System.Text.Json;
using FocusBloom.Core.Focus;
using Xunit;

namespace FocusBloom.Tests;

public sealed class FocusRecoveryTests
{
    [Theory]
    [InlineData(TimerMode.CountUp)]
    [InlineData(TimerMode.Countdown)]
    [InlineData(TimerMode.Pomodoro)]
    public void ContinueKeepsConfirmedIntervalsIdentityAndExcludesOfflineTime(TimerMode mode)
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        var options = new FocusOptions(Guid.NewGuid(), mode, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10), TimeSpan.Zero,
            true, mode == TimerMode.Pomodoro ? new PomodoroOptions(TimeSpan.Zero, 4, 2) : null, Guid.NewGuid());
        timer.Start(options); timer.RegisterInterception(); clock.Advance(TimeSpan.FromSeconds(40)); timer.Update();
        var checkpoint = JsonSerializer.Deserialize<FocusCheckpoint>(JsonSerializer.Serialize(timer.CaptureCheckpoint()))!;
        clock.Advance(TimeSpan.FromHours(3));
        var restored = new FocusTimer(clock); restored.RestoreCheckpoint(checkpoint);
        Assert.Equal(checkpoint.CurrentRecord!.Id, restored.SessionId);
        Assert.Equal(TimeSpan.FromSeconds(40), restored.Elapsed); Assert.Equal(1, restored.InterceptionCount);
        Assert.True(restored.RestrictionsActive); Assert.Equal(1, restored.Round);
        clock.Advance(TimeSpan.FromSeconds(20)); restored.Update(); restored.End(takeRest: false);
        Assert.Equal(TimeSpan.FromSeconds(60), restored.LastRecord!.FocusDuration);
        Assert.Equal(options.TodoId, restored.LastRecord.TodoId);
        Assert.Contains(restored.LastRecord.Intervals, interval => interval.Kind == IntervalKind.Pause && interval.Duration == TimeSpan.FromHours(3));
    }

    [Fact]
    public void PauseAndPomodoroRestWaitForChoiceAndKeepRemainingPhase()
    {
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(new(Guid.NewGuid(), TimerMode.Pomodoro, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15),
            Pomodoro: new(TimeSpan.FromSeconds(30), 2, 4)));
        clock.Advance(TimeSpan.FromSeconds(12)); timer.Pause(); clock.Advance(TimeSpan.FromSeconds(3));
        var pause = timer.CaptureCheckpoint()!; clock.Advance(TimeSpan.FromDays(1));
        var restored = new FocusTimer(clock); restored.RestoreCheckpoint(pause);
        Assert.Equal(FocusState.Paused, restored.State); Assert.Equal(TimeSpan.FromSeconds(7), restored.PhaseRemaining);
        clock.Advance(TimeSpan.FromSeconds(7)); restored.Update();
        Assert.Equal(TimeSpan.FromSeconds(12), restored.Elapsed);
        clock.Advance(TimeSpan.FromSeconds(18)); restored.Update();
        var rest = restored.CaptureCheckpoint(includeLastRecord: true)!;
        clock.Advance(TimeSpan.FromDays(1)); var afterRest = new FocusTimer(clock); afterRest.RestoreCheckpoint(rest);
        Assert.Equal(FocusState.Resting, afterRest.State); Assert.Equal(1, afterRest.Round);
        Assert.Equal(TimeSpan.FromSeconds(15), afterRest.PhaseRemaining);
        Assert.Equal(TimeSpan.FromSeconds(30), afterRest.LastRecord!.FocusDuration);
        clock.Advance(TimeSpan.FromSeconds(15)); afterRest.Update();
        Assert.Equal(2, afterRest.Round); Assert.Equal(TimeSpan.Zero, afterRest.Elapsed);
    }
}
