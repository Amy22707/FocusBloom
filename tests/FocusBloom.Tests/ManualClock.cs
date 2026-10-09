namespace FocusBloom.Tests;

internal sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan duration) => _now += duration;
}
