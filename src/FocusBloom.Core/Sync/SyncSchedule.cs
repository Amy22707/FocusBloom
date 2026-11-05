namespace FocusBloom.Core.Sync;

public sealed record SyncSchedule(long Revision, long AttemptedRevision, bool HasPending,
    DateTimeOffset? ChangedAt, DateTimeOffset? LastAttempt)
{
    public bool IsDue(DateTimeOffset now) =>
        LastAttempt is null || now - LastAttempt.Value >= TimeSpan.FromDays(7)
        || HasPending && Revision > AttemptedRevision
            && (ChangedAt is null || now - ChangedAt.Value >= TimeSpan.FromMinutes(2));
}
