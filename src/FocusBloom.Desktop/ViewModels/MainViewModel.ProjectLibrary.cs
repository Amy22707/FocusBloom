using System.Collections.Generic;
using System.Linq;
using FocusBloom.Core.Projects;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string? _projectLibraryScope;
    public IReadOnlyList<string> ProjectLibraryScopes { get; } = new[] { "当前文件夹", "全部文件夹" };
    public string ProjectLibraryScope
    {
        get => _projectLibraryScope ??= _store.GetSetting("projects.scope") == "全部文件夹" ? "全部文件夹" : "当前文件夹";
        set { _projectLibraryScope = value; _store.SaveSetting("projects.scope", value); Notify(); RefreshProjectLibrary(); }
    }
    public IEnumerable<FocusProject> ProjectLibraryItems => ProjectLibraryScope == "全部文件夹" ? Projects : FolderProjects;
    public string ProjectLibraryTitle => ProjectLibraryScope == "全部文件夹" ? "全部项目" : FolderTitle;
    public string ProjectLibrarySummary => $"{ProjectLibraryItems.Count()} 个项目 · 选择后可在专注页开始计时";
    public bool ProjectLibraryEmpty => !ProjectLibraryItems.Any();
    public FocusProject? SelectedLibraryProject
    {
        get => SelectedProject;
        set
        {
            if (value is null) return;
            if (SelectedFolder?.Name != value.Category) SelectedFolder = Folders.FirstOrDefault(folder => folder.Name == value.Category);
            SelectedProject = value;
        }
    }
    private void RefreshProjectLibrary()
    {
        Notify(nameof(ProjectLibraryItems)); Notify(nameof(ProjectLibraryTitle)); Notify(nameof(ProjectLibrarySummary)); Notify(nameof(ProjectLibraryEmpty));
    }
}
