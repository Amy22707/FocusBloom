using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Todos;

namespace FocusBloom.Core.Projects;

public sealed record ProjectProgress(int TaskCount, int CompletedCount, TimeSpan Invested, double EstimatedRemainingMinutes,
    int WithoutEstimate, IReadOnlyList<TodoItem> Unscheduled)
{
    public static ProjectProgress Calculate(Guid id, DateOnly today, IEnumerable<TodoItem> todos,
        IEnumerable<FocusRecord> records, IEnumerable<CalendarPlan> plans)
    {
        var linked = todos.Where(todo => todo.PreferredProjectId == id).ToArray();
        var pending = linked.Where(todo => todo.Status != TodoStatus.Completed).ToArray();
        var history = records.ToArray();
        var spentByTodo = history.Where(record => record.TodoId.HasValue).GroupBy(record => record.TodoId!.Value)
            .ToDictionary(group => group.Key, group => group.Sum(record => record.FocusDuration.TotalMinutes));
        var scheduled = plans.Where(plan => plan.Date >= today && plan.TodoId.HasValue).Select(plan => plan.TodoId!.Value).ToHashSet();
        return new(linked.Length, linked.Length - pending.Length,
            TimeSpan.FromTicks(history.Where(record => record.ProjectId == id).Sum(record => record.FocusDuration.Ticks)),
            pending.Sum(todo => Math.Max(0, (todo.EstimatedMinutes ?? 0) - spentByTodo.GetValueOrDefault(todo.Id))),
            pending.Count(todo => todo.EstimatedMinutes is null), pending.Where(todo => !scheduled.Contains(todo.Id)).ToArray());
    }
}
