using System.Windows;
using FocusBloom.Core.Projects;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class ProjectEditorWindow : Window
{
    private readonly MainViewModel _model;
    private readonly FocusProject? _project;
    private readonly ProjectFolder? _folder;
    private readonly bool _creating;
    private readonly bool _newFolder;

    public ProjectEditorWindow(MainViewModel model, bool newFolder)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _creating = true; _newFolder = newFolder;
        Title = EditorHeading.Text = newFolder ? "新建文件夹" : "新建项目";
        NameCaption.Text = newFolder ? "文件夹名称" : "项目名称";
        NameInput.ToolTip = newFolder ? "例如：学习、工作、生活" : "例如：英语阅读、课程论文";
        FolderField.Visibility = newFolder ? Visibility.Collapsed : Visibility.Visible;
        FolderSelector.ItemsSource = model.Folders;
        FolderSelector.SelectedItem = model.SelectedFolder ?? System.Linq.Enumerable.FirstOrDefault(model.Folders);
        DeleteHint.Text = newFolder ? "用文件夹整理同类项目。" : "在这个文件夹中，记录一件想专注的事。";
        DeleteButton.Visibility = Visibility.Collapsed;
        SaveButton.Content = "创建";
        Loaded += (_, _) => NameInput.Focus();
    }

    public ProjectEditorWindow(MainViewModel model, FocusProject project)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _project = project;
        NameInput.Text = project.Name;
        FolderSelector.ItemsSource = model.Folders;
        FolderSelector.SelectedItem = System.Linq.Enumerable.FirstOrDefault(model.Folders, folder => folder.Name == project.Category);
        if (FolderSelector.SelectedItem is ProjectFolder folder && folder.Projects.Count == 1)
            DeleteHint.Text = "删除后保留这个空文件夹，历史专注记录仍保留。";
        if (!model.CanDeleteProject(project)) DeleteHint.Text = "请先结束该项目的专注并保存记录，再删除。";
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    public ProjectEditorWindow(MainViewModel model, ProjectFolder folder)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _folder = folder;
        Title = EditorHeading.Text = "修改文件夹";
        NameInput.Text = folder.Name;
        FolderField.Visibility = Visibility.Collapsed;
        DeleteHint.Text = $"删除文件夹会同时移除其中的 {folder.Projects.Count} 个项目，历史专注记录保留。";
        if (!model.CanDeleteFolder(folder)) DeleteHint.Text = "文件夹中有正在专注或记录未保存的项目，请先结束专注并保存记录，再删除。";
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var saved = _creating
            ? _newFolder ? _model.CreateFolder(NameInput.Text) : _model.CreateProject(NameInput.Text, FolderSelector.SelectedItem as ProjectFolder)
            : _project is not null
            ? _model.UpdateProject(_project, NameInput.Text, FolderSelector.SelectedItem as ProjectFolder)
            : _model.RenameFolder(_folder!, NameInput.Text);
        if (saved) DialogResult = true;
        else ValidationMessage.Text = _model.StatusMessage;
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
