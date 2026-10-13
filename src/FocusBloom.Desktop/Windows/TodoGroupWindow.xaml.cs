using System.Windows;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class TodoGroupWindow : Window
{
    private readonly MainViewModel _main;
    private readonly TodoFolder? _folder;
    public TodoGroupWindow(MainViewModel main, TodoFolder? folder = null)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _main = main; _folder = folder;
        GroupHeading.Text = folder is null ? "新建文件夹" : "编辑文件夹";
        NameInput.Text = folder?.Name ?? "";
        DeleteButton.Visibility = folder is null ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var saved = _main.Todos.SaveFolder(_folder, NameInput.Text, _folder?.Category ?? "未分类") is not null;
        if (saved) DialogResult = true; else ValidationMessage.Text = _main.StatusMessage;
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_folder is null) return;
        if (!_main.Todos.CanDeleteFolder(_folder)) { ConfirmDeletionWindow.ShowNotice(this, "文件夹中有正在计时或记录未保存的待办，请先结束并保存，再删除。"); return; }
        if (!ConfirmDeletionWindow.Ask(this, "删除这个文件夹及其中的待办？历史记录和成果仍保留。")) return;
        if (_main.Todos.DeleteFolder(_folder)) DialogResult = true;
    }
}
