using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Projects;
using FocusBloom.Desktop.ViewModels;

using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public partial class ProjectEditorWindow : Window
{
    private readonly MainViewModel _model;
    private readonly FocusProject? _project;
    private readonly ProjectFolder? _folder;
    private readonly bool _creating;
    private readonly bool _newFolder;
    private readonly List<(TextBox Title, CheckBox Completed)> _milestones = new();

    public ProjectEditorWindow(MainViewModel model, bool newFolder)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _creating = true; _newFolder = newFolder;
        Title = EditorHeading.Text = newFolder ? "新建文件夹" : "新建项目";
        NameCaption.Text = newFolder ? "文件夹名称" : "项目名称";
        NameInput.ToolTip = newFolder ? "例如：学习、工作、生活" : "例如：英语阅读、课程论文";
        FolderField.Visibility = newFolder ? Visibility.Collapsed : Visibility.Visible;
        PlanFields.Visibility = FolderField.Visibility;
        if (!newFolder)
        {
            PresetField.Visibility = Visibility.Visible;
            PresetSelector.ItemsSource = ProjectPreset.All;
            PresetSelector.SelectedIndex = 0;
        }
        FolderSelector.ItemsSource = model.Folders;
        FolderSelector.SelectedItem = model.SelectedFolder ?? System.Linq.Enumerable.FirstOrDefault(model.Folders);
        DeleteHint.Text = newFolder ? "用文件夹整理同类项目。" : "在这个文件夹中，记录一件想专注的事。";
        DeleteButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = UiText.Get("创建");
        Loaded += (_, _) => NameInput.Focus();
    }

    public ProjectEditorWindow(MainViewModel model, FocusProject project)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _project = project;
        NameInput.Text = project.Name;
        var store = model.Calendar.Store;
        var progress = ProjectProgress.Calculate(project.Id, DateOnly.FromDateTime(DateTime.Today), store.GetTodos(), store.GetRecords(), store.GetCalendarPlans());
        ProjectProgressPanel.Visibility = Visibility.Visible;
        ProgressSummary.Text = $"待办 {progress.CompletedCount}/{progress.TaskCount} 已完成 · 已专注 {progress.Invested.TotalMinutes:0.#} 分钟\n剩余预计 {progress.EstimatedRemainingMinutes:0.#} 分钟"
            + (progress.WithoutEstimate == 0 ? "" : $"（另有 {progress.WithoutEstimate} 项未估时）");
        UnscheduledSummary.Text = progress.Unscheduled.Count == 0 ? "没有未安排的待办。" : $"未安排 {progress.Unscheduled.Count} 项：\n"
            + string.Join("\n", progress.Unscheduled.Select(item => "· " + item.Title));
        if (project.Plan is { } plan)
        {
            GoalInput.Text = plan.Goal;
            DeadlineInput.SelectedDate = plan.Deadline?.ToDateTime(TimeOnly.MinValue);
            foreach (var milestone in plan.Milestones) AddMilestone(milestone);
        }
        FolderSelector.ItemsSource = model.Folders;
        FolderSelector.SelectedItem = System.Linq.Enumerable.FirstOrDefault(model.Folders, folder => folder.Name == project.Category);
        if (FolderSelector.SelectedItem is ProjectFolder folder && folder.Projects.Count == 1)
            DeleteHint.Text = UiText.Get("删除后保留这个空文件夹，历史专注记录仍保留。");
        if (!model.CanDeleteProject(project)) DeleteHint.Text = UiText.Get("请先结束该项目的专注并保存记录，再删除。");
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    public ProjectEditorWindow(MainViewModel model, ProjectFolder folder)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _folder = folder;
        Title = EditorHeading.Text = UiText.Get("修改文件夹");
        NameInput.Text = folder.Name;
        FolderField.Visibility = Visibility.Collapsed;
        PlanFields.Visibility = Visibility.Collapsed;
        DeleteHint.Text = $"删除文件夹会同时移除其中的 {folder.Projects.Count} 个项目，历史专注记录保留。";
        if (!model.CanDeleteFolder(folder)) DeleteHint.Text = UiText.Get("文件夹中有正在专注或记录未保存的项目，请先结束专注并保存记录，再删除。");
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (PlanFields.IsVisible && DeadlineInput.SelectedDate is null && !string.IsNullOrWhiteSpace(DeadlineInput.Text))
        { ValidationMessage.Text = UiText.Get("请选择有效的截止日期。"); return; }
        var goal = GoalInput.Text.Trim();
        var deadline = DeadlineInput.SelectedDate is { } date ? DateOnly.FromDateTime(date) : (DateOnly?)null;
        var milestones = _milestones.Where(item => !string.IsNullOrWhiteSpace(item.Title.Text))
            .Select(item => new ProjectMilestone(item.Title.Text.Trim(), item.Completed.IsChecked == true)).ToArray();
        var preset = PresetSelector.SelectedItem as ProjectPreset;
        if (preset is { Tasks.Count: > 0 })
        {
            if (goal.Length == 0) goal = preset.Goal;
            if (milestones.Length == 0) milestones = preset.Milestones.ToArray();
        }
        var plan = goal.Length != 0 || deadline is not null || milestones.Length != 0 || _project?.Plan is not null
            ? new ProjectPlan(goal, deadline, milestones) : null;
        var saved = _creating
            ? _newFolder ? _model.CreateFolder(NameInput.Text) : _model.CreateProject(NameInput.Text, FolderSelector.SelectedItem as ProjectFolder, plan, preset)
            : _project is not null
            ? _model.UpdateProject(_project, NameInput.Text, FolderSelector.SelectedItem as ProjectFolder, plan)
            : _model.RenameFolder(_folder!, NameInput.Text);
        if (saved) DialogResult = true;
        else ValidationMessage.Text = _model.StatusMessage;
    }
    private void Preset_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PresetPreview is null) return;
        PresetPreview.Text = PresetSelector.SelectedItem is ProjectPreset { Tasks.Count: > 0 } preset
            ? "将生成：" + string.Join("、", preset.Tasks.Select(item => item.Title)) + "。空白目标与里程碑沿用预设，创建后可以编辑。"
            : "";
    }
    private void AddMilestone_Click(object sender, RoutedEventArgs e) => AddMilestone(new(""));
    private void AddMilestone(ProjectMilestone value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var done = new CheckBox { IsChecked = value.Completed, ToolTip = UiText.Get("标记里程碑完成"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var title = new TextBox { Text = value.Title };
        var remove = new Button { Content = UiText.Get("移除"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => { _milestones.Remove((title, done)); MilestoneRows.Children.Remove(row); };
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove); row.Children.Add(done); row.Children.Add(title);
        _milestones.Add((title, done)); MilestoneRows.Children.Add(row);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null ? !_model.CanDeleteProject(_project) : !_model.CanDeleteFolder(_folder!))
        {
            ConfirmDeletionWindow.ShowNotice(this, "请先结束相关项目的专注并保存记录，再删除。暂停期间仍属于计时过程。历史专注记录会保留。");
            return;
        }
        var message = _project is not null
            ? $"删除项目‘{_project.Name}’？历史专注记录仍保留。"
            : $"删除文件夹‘{_folder!.Name}’及其中的 {_folder.Projects.Count} 个项目？历史专注记录仍保留。";
        if (!ConfirmDeletionWindow.Ask(this, message)) return;
        var deleted = _project is not null ? _model.DeleteProject(_project) : _model.DeleteFolder(_folder!);
        if (deleted) DialogResult = true;
        else ValidationMessage.Text = _model.StatusMessage;
    }
}
