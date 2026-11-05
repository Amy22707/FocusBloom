using System.Text.Json;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Storage;

namespace FocusBloom.Core.Todos;

public enum ChecklistPeriod { Day, Week, Month }

public readonly record struct ChecklistScope(ChecklistPeriod Period, DateOnly Start, DateOnly? Section)
{
    public static ChecklistScope For(ChecklistPeriod period, DateOnly date, bool subdivided = false)
    {
        var start = period switch
        {
            ChecklistPeriod.Week => CalendarRules.Monday(date),
            ChecklistPeriod.Month => new DateOnly(date.Year, date.Month, 1),
            _ => date
        };
        DateOnly? section = !subdivided ? null : period switch
        {
            ChecklistPeriod.Week => date,
            ChecklistPeriod.Month => CalendarRules.Monday(date),
            _ => null
        };
        return new(period, start, section);
    }

    internal string Key => $"checklist.{Period}.{Start:yyyy-MM-dd}.{Section?.ToString("yyyy-MM-dd") ?? "all"}";
}

public sealed record ChecklistEntry(Guid Id, string Text, bool Done);

public sealed class PeriodicChecklist(IFocusStore store)
{
    public IReadOnlyList<ChecklistEntry> Read(ChecklistScope scope) =>
        store.GetSetting(scope.Key) is { } value ? JsonSerializer.Deserialize<ChecklistEntry[]>(value) ?? [] : [];

    public void Save(ChecklistScope scope, IEnumerable<ChecklistEntry> entries) =>
        store.SaveSetting(scope.Key, JsonSerializer.Serialize(entries));
}
