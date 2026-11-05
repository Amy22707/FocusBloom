namespace FocusBloom.Core.Projects;

public sealed record ProjectMilestone(string Title, bool Completed = false);
public sealed record ProjectPlan(string Goal, DateOnly? Deadline, IReadOnlyList<ProjectMilestone> Milestones);
public sealed record FocusProject(Guid Id, string Name, string Category, string Color, ProjectPlan? Plan = null)
{
    public string PlanSummary => Plan switch
    {
        { Deadline: { } deadline, Milestones.Count: > 0 } plan => $"截止 {deadline:yyyy-MM-dd} · {plan.Milestones.Count(item => item.Completed)}/{plan.Milestones.Count} 里程碑",
        { Deadline: { } deadline } => $"截止 {deadline:yyyy-MM-dd}",
        { Milestones.Count: > 0 } plan => $"{plan.Milestones.Count(item => item.Completed)}/{plan.Milestones.Count} 里程碑",
        _ => Category
    };
}
