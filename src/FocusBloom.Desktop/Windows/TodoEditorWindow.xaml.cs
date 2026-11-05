using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Core.Calendar;

using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public partial class TodoEditorWindow : Window
{
    private sealed record FolderChoice(Guid Id, string Label);
    private sealed record ProjectChoice(Guid? Id, string Label);
    private sealed record StatusChoice(TodoStatus Value, string Name);
    private sealed record PriorityChoice(TodoPriority Value, string Name);
    private readonly MainViewModel _main;
    private readonly TodoItem? _item;
    private readonly ReminderSettingsControl _reminder;
    private readonly List<(TextBox Title, CheckBox Done)> _steps = [];
    public TodoEditorWindow(MainViewModel main, TodoItem? item = null, bool important = false, bool urgent = false, Guid? initialFolderId = null)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _main = main; _item = item;
        EditorHeading.Text = item is null ? "新建待办" : "编辑待办";
        FolderSelector.ItemsSource = main.Todos.Folders.Select(folder => new FolderChoice(folder.Id, folder.Name)).ToArray();
        FolderSelector.SelectedItem = FolderSelector.Items.Cast<FolderChoice>().FirstOrDefault(folder => folder.Id == (item?.FolderId ?? initialFolderId ?? main.Todos.SelectedFolder?.Id));
        var projects = main.Projects.Select(project => new ProjectChoice(project.Id, $"{project.Category} / {project.Name}")).Prepend(new(null, "不关联项目")).ToList();
        if (item?.PreferredProjectId is { } archived && projects.All(project => project.Id != archived))
            projects.Add(new(archived, "原关联项目（已移除）"));
        ProjectSelector.ItemsSource = projects;
        ProjectSelector.SelectedItem = projects.Single(project => project.Id == item?.PreferredProjectId);
        foreach (var step in item?.Steps ?? []) AddStep(step);
        StatusSelector.ItemsSource = new[] { new StatusChoice(TodoStatus.Pending, "待办"), new StatusChoice(TodoStatus.InProgress, "进行中"), new StatusChoice(TodoStatus.Completed, "已完成") };
        PrioritySelector.ItemsSource = new[] { new PriorityChoice(TodoPriority.Low, "低"), new PriorityChoice(TodoPriority.Normal, "普通"), new PriorityChoice(TodoPriority.High, "高") };
        StatusSelector.SelectedItem = StatusSelector.Items.Cast<StatusChoice>().Single(choice => choice.Value == (item?.Status ?? TodoStatus.Pending));
        PrioritySelector.SelectedItem = PrioritySelector.Items.Cast<PriorityChoice>().Single(choice => choice.Value == (item?.Priority ?? TodoPriority.Normal));
        TitleInput.Text = item?.Title ?? ""; DueDateInput.SelectedDate = item?.DueDate?.ToDateTime(TimeOnly.MinValue);
        DueTimeInput.Text = item?.DueTime?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "";
        EstimateInput.Text = item?.EstimatedMinutes?.ToString(CultureInfo.CurrentCulture) ?? "";
        NotesInput.Text = item?.Notes ?? ""; ResumeInput.Text = item?.ResumeNote ?? "";
        DeleteButton.Visibility = item is null ? Visibility.Collapsed : Visibility.Visible;
        ImportantInput.IsChecked = item?.Important ?? important; UrgentInput.IsChecked = item?.Urgent ?? urgent;
        _reminder = new(item is null ? new ReminderOptions() : main.Calendar.Store.GetReminderOptions("待办", item.Id)); ReminderArea.Children.Add(_reminder);
        DueDateInput.SelectedDateChanged += (_, _) => ReminderPreview(); DueTimeInput.TextChanged += (_, _) => ReminderPreview(); ReminderPreview();
    }
    private void ReminderPreview() => _reminder.SetOccurrence(DueDateInput.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null,
        TimeOnly.TryParseExact(DueTimeInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null);
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (FolderSelector.SelectedItem is not FolderChoice folder) { ValidationMessage.Text = UiText.Get("请选择文件夹。"); return; }
        TimeOnly? dueTime = null; int? estimate = null;
        if (!string.IsNullOrWhiteSpace(DueTimeInput.Text))
        {
            if (DueDateInput.SelectedDate is null || !TimeOnly.TryParseExact(DueTimeInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            { ValidationMessage.Text = UiText.Get("请设置截止日期，并按HH:mm填写时间。"); return; }
            dueTime = time;
        }
        if (!string.IsNullOrWhiteSpace(EstimateInput.Text))
        {
            if (!int.TryParse(EstimateInput.Text, out var minutes) || minutes <= 0) { ValidationMessage.Text = UiText.Get("预计投入须为正整数分钟。"); return; }
            estimate = minutes;
        }
        var item = new TodoItem(_item?.Id ?? Guid.NewGuid(), folder.Id, TitleInput.Text,
            ((StatusChoice)StatusSelector.SelectedItem).Value, ((PriorityChoice)PrioritySelector.SelectedItem).Value,
            DueDateInput.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null, dueTime,
            NotesInput.Text.Trim(), estimate, ResumeInput.Text.Trim(), (ProjectSelector.SelectedItem as ProjectChoice)?.Id,
            ImportantInput.IsChecked == true, UrgentInput.IsChecked == true,
            Steps: _steps.Where(step => !string.IsNullOrWhiteSpace(step.Title.Text))
                .Select(step => new TodoStep(step.Title.Text.Trim(), step.Done.IsChecked == true)).ToArray());
        if (!_reminder.TryGetOptions(out var reminder)) { ValidationMessage.Text = UiText.Get("请填写有效的提醒设置。"); return; }
        if (_main.Todos.SaveItem(item)) { _main.Calendar.Store.SaveReminderOptions("待办", item.Id, reminder); _main.ReloadCalendarData(); DialogResult = true; }
        else ValidationMessage.Text = _main.StatusMessage;
    }
    private void AddStep_Click(object sender, RoutedEventArgs e) => AddStep(new(""));
    private void AddStep(TodoStep value)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var done = new CheckBox { IsChecked = value.Completed, ToolTip = UiText.Get("完成子任务"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var title = new TextBox { Text = value.Title };
        var remove = new Button { Content = UiText.Get("移除"), Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => { _steps.Remove((title, done)); StepRows.Children.Remove(row); };
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(remove); row.Children.Add(done); row.Children.Add(title);
        _steps.Add((title, done)); StepRows.Children.Add(row);
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        if (!_main.Todos.CanDeleteItem(_item.Id)) { ConfirmDeletionWindow.ShowNotice(this, "请先结束这项待办的计时并保存记录，再删除。"); return; }
        if (ConfirmDeletionWindow.Ask(this, $"删除待办‘{_item.Title}’？历史记录和成果仍保留。") && _main.Todos.DeleteItem(_item.Id)) DialogResult = true;
    }
}
