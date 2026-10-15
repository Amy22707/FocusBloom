using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;

namespace FocusBloom.Core.Storage;

public interface IFocusStore
{
    IReadOnlyList<FocusProject> GetProjects(bool includeDeleted = false);
    IReadOnlyList<string> GetFolders();
    void SaveFolder(string name);
    void SaveProject(FocusProject project);
    void RenameFolder(string name, string newName);
    void DeleteFolder(string name);
    void DeleteProject(Guid id);
    void SaveRecord(FocusRecord record);
    IReadOnlyList<FocusRecord> GetRecords();
    IReadOnlyList<string> GetTodoCategories();
    void SaveTodoCategory(string name);
    void RenameTodoCategory(string name, string newName);
    void DeleteTodoCategory(string name);
    IReadOnlyList<TodoFolder> GetTodoFolders();
    void SaveTodoFolder(TodoFolder folder);
    void DeleteTodoFolder(Guid id);
    IReadOnlyList<TodoItem> GetTodos(bool includeDeleted = false);
    void SaveTodo(TodoItem todo);
    void DeleteTodo(Guid id);
    FocusOutcome? GetOutcome(Guid sessionId);
    IReadOnlyList<FocusOutcome> GetOutcomes();
    void SaveOutcome(FocusOutcome outcome);
    IReadOnlyList<FocusBloom.Core.Calendar.DateEvent> GetDateEvents();
    void SaveDateEvent(FocusBloom.Core.Calendar.DateEvent item);
    void DeleteDateEvent(Guid id);
    IReadOnlyList<FocusBloom.Core.Calendar.CalendarPlan> GetCalendarPlans();
    void SaveCalendarPlan(FocusBloom.Core.Calendar.CalendarPlan item);
    void DeleteCalendarPlan(Guid id);
    IReadOnlyList<FocusBloom.Core.Calendar.SchoolCourse> GetCourses();
    void SaveCourse(FocusBloom.Core.Calendar.SchoolCourse item);
    void DeleteCourse(Guid id);
    IReadOnlyList<FocusBloom.Core.Calendar.CourseChange> GetCourseChanges();
    void SaveCourseChange(FocusBloom.Core.Calendar.CourseChange item);
    void DeleteCourseChange(Guid id);
    FocusBloom.Core.Calendar.SchoolTerm GetSchoolTerm();
    void SaveSchoolTerm(FocusBloom.Core.Calendar.SchoolTerm term);
    FocusBloom.Core.Calendar.ReminderOptions GetReminderOptions(string kind, Guid id);
    void SaveReminderOptions(string kind, Guid id, FocusBloom.Core.Calendar.ReminderOptions options);
    bool IsReminderHandled(string key);
    void MarkReminderHandled(string key);
    IReadOnlyList<FocusBloom.Core.Goals.FocusGoal> GetGoals();
    void SaveGoal(FocusBloom.Core.Goals.FocusGoal goal);
    void DeleteGoal(Guid id);
    IReadOnlyList<FocusBloom.Core.Goals.Habit> GetHabits(bool includeDeleted = false);
    void SaveHabit(FocusBloom.Core.Goals.Habit habit);
    void DeleteHabit(Guid id);
    IReadOnlyList<FocusBloom.Core.Goals.HabitCheckIn> GetHabitCheckIns();
    void SetHabitCheckIn(Guid habitId, DateOnly date, bool completed);
    string? GetSetting(string key);
    void SaveSetting(string key, string value);
}
