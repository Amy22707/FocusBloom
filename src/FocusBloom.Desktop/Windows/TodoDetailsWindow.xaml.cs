using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class TodoDetailsWindow : Window
{
    private readonly MainViewModel _main;
    private readonly Guid _id;
    private TodoRow Row => (TodoRow)DataContext;
    public TodoDetailsWindow(MainViewModel main, TodoRow row)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _main = main; _id = row.Id;
        Refresh(row);
    }
    private void Refresh(TodoRow row)
    {
        DataContext = row;
        var folder = _main.Todos.Folders.Single(item => item.Id == row.Item.FolderId);
        FolderLocation.Text = folder.Name;
    }
    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        new TodoEditorWindow(_main, Row.Item) { Owner = this }.ShowDialog();
        if (_main.Todos.Items.FirstOrDefault(item => item.Id == _id) is { } row) Refresh(row);
        else Close();
    }
    private void Focus_Click(object sender, RoutedEventArgs e)
    {
        if (!_main.CanConfigure || _main.HasUnsavedRecord)
        { ConfirmDeletionWindow.ShowNotice(this, "请先结束当前计时并保存记录，再开始另一项待办。"); return; }
        var minutes = int.TryParse(((Button)sender).Tag?.ToString(), out var value) ? value : (int?)null;
        if (new TodoStartWindow(_main, Row.Item, minutes) { Owner = this }.ShowDialog() == true) DialogResult = true;
    }
}
