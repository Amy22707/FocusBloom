using System.Collections.ObjectModel;
using FocusBloom.Core.Projects;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class ProjectFolder(string name) : ObservableObject
{
    public string Name { get; } = name;
    public ObservableCollection<FocusProject> Projects { get; } = [];
    public string CountText => $"{Projects.Count} 个项目";
    public void Add(FocusProject project)
    {
        Projects.Add(project);
        Notify(nameof(CountText));
    }
}

public sealed record TimerChoice(FocusBloom.Core.Focus.TimerMode Mode, string Name);
