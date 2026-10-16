namespace FocusBloom.Core.Todos;

public enum TodoStatus { Pending, InProgress, Completed }
public enum TodoPriority { Low, Normal, High }
public sealed record TodoFolder(Guid Id, string Name, string Category);
public sealed record TodoItem(Guid Id, Guid FolderId, string Title,
    TodoStatus Status = TodoStatus.Pending, TodoPriority Priority = TodoPriority.Normal,
    DateOnly? DueDate = null, TimeOnly? DueTime = null, string Notes = "",
    int? EstimatedMinutes = null, string ResumeNote = "", Guid? PreferredProjectId = null,
    bool Important = false, bool Urgent = false, DateTimeOffset? CompletedAt = null);
