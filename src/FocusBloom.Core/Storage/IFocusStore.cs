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
    string? GetSetting(string key);
    void SaveSetting(string key, string value);
}
