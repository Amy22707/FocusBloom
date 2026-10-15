using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Desktop.Windows;

public partial class TodoEditorWindow : Window
{
    private sealed record FolderChoice(Guid Id, string Label);
    private sealed record StatusChoice(TodoStatus Value, string Name);
    private sealed record PriorityChoice(TodoPriority Value, string Name);
    private readonly MainViewModel _main;
    private readonly TodoItem? _item;
    private readonly ReminderSettingsControl _reminder;
    public TodoEditorWindow(MainViewModel main, TodoItem? item = null, bool important = false, bool urgent = false)
    {
        InitializeComponent(); WindowAppearance.Attach(this); _main = main; _item = item;
        EditorHeading.Text = item is null ? "新建待办" : "编辑待办";
        FolderSelector.ItemsSource = main.Todos.Folders.Select(folder => new FolderChoice(folder.Id, folder.Name)).ToArray();
        FolderSelector.SelectedItem = FolderSelector.Items.Cast<FolderChoice>().FirstOrDefault(folder => folder.Id == (item?.FolderId ?? main.Todos.SelectedFolder?.Id));
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
        if (FolderSelector.SelectedItem is not FolderChoice folder) { ValidationMessage.Text = "请选择文件夹。"; return; }
        TimeOnly? dueTime = null; int? estimate = null;
        if (!string.IsNullOrWhiteSpace(DueTimeInput.Text))
        {
            if (DueDateInput.SelectedDate is null || !TimeOnly.TryParseExact(DueTimeInput.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            { ValidationMessage.Text = "请设置截止日期，并按HH:mm填写时间。"; return; }
            dueTime = time;
        }
        if (!string.IsNullOrWhiteSpace(EstimateInput.Text))
        {
            if (!int.TryParse(EstimateInput.Text, out var minutes) || minutes <= 0) { ValidationMessage.Text = "预计投入须为正整数分钟。"; return; }
            estimate = minutes;
        }
        var item = new TodoItem(_item?.Id ?? Guid.NewGuid(), folder.Id, TitleInput.Text,
            ((StatusChoice)StatusSelector.SelectedItem).Value, ((PriorityChoice)PrioritySelector.SelectedItem).Value,
            DueDateInput.SelectedDate is { } date ? DateOnly.FromDateTime(date) : null, dueTime,
            NotesInput.Text.Trim(), estimate, ResumeInput.Text.Trim(), _item?.PreferredProjectId,
            ImportantInput.IsChecked == true, UrgentInput.IsChecked == true);
        if (!_reminder.TryGetOptions(out var reminder)) { ValidationMessage.Text = "请填写有效的提醒设置。"; return; }
        if (_main.Todos.SaveItem(item)) { _main.Calendar.Store.SaveReminderOptions("待办", item.Id, reminder); _main.ReloadCalendarData(); DialogResult = true; }
        else ValidationMessage.Text = _main.StatusMessage;
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        if (!_main.Todos.CanDeleteItem(_item.Id)) { ConfirmDeletionWindow.ShowNotice(this, "请先结束这项待办的计时并保存记录，再删除。"); return; }
        if (ConfirmDeletionWindow.Ask(this, $"删除待办‘{_item.Title}’？历史记录和成果仍保留。") && _main.Todos.DeleteItem(_item.Id)) DialogResult = true;
    }
}
