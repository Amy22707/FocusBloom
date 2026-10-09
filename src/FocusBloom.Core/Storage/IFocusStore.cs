using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;

namespace FocusBloom.Core.Storage;

public interface IFocusStore
{
    IReadOnlyList<FocusProject> GetProjects();
    IReadOnlyList<string> GetFolders();
    void SaveFolder(string name);
    void SaveProject(FocusProject project);
    void SaveRecord(FocusRecord record);
    IReadOnlyList<FocusRecord> GetRecords();
    string? GetSetting(string key);
    void SaveSetting(string key, string value);
}
