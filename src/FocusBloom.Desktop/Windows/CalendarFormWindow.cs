using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public abstract class CalendarFormWindow : Window
{
    protected readonly MainViewModel Main;
    protected readonly Button SaveButton;
    public bool FocusRequested { get; protected set; }
    protected readonly StackPanel Fields = new();
    protected readonly TextBlock Message = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
    protected CalendarFormWindow(MainViewModel main, string title, bool editing = false)
    {
        Main = main; Title = title; Width = 520; Height = 620; MinWidth = 440; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this); NameScope.SetNameScope(this, new NameScope());
        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto }); root.RowDefinitions.Add(new()); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        var scroll = new ScrollViewer { Content = Fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) }; Grid.SetRow(footer, 2); root.Children.Add(footer);
        if (editing) { var delete = new Button { Content = "删除" }; RegisterName("DeleteButton", delete); delete.Click += (_, _) => Delete(); DockPanel.SetDock(delete, Dock.Left); footer.Children.Add(delete); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        SaveButton = new Button { Content = "保存", IsDefault = true }; SaveButton.SetResourceReference(StyleProperty, "PrimaryButton"); SaveButton.Click += (_, _) => Save(); RegisterName("SaveButton", SaveButton); buttons.Children.Add(SaveButton); footer.Children.Add(buttons);
        Message.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); Content = root;
    }
    protected void Caption(string text) { var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 12, 0, 6) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Fields.Children.Add(label); }
    protected TextBox AddTextInput(string name, string label, string value = "", bool multiline = false)
    { Caption(label); var input = new TextBox { Text = value, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden }; if (multiline) input.Height = 85; RegisterName(name, input); Fields.Children.Add(input); return input; }
    protected DatePicker DateInput(string name, string label, DateOnly? value)
    { Caption(label); var input = new DatePicker { SelectedDate = value?.ToDateTime(TimeOnly.MinValue) }; RegisterName(name, input); Fields.Children.Add(input); return input; }
    protected ComboBox Choice(string name, string label, IEnumerable items, object? selected = null, string? display = null)
    { Caption(label); var input = new ComboBox { ItemsSource = items, SelectedItem = selected, DisplayMemberPath = display ?? "" }; RegisterName(name, input); if (input.SelectedIndex < 0 && input.Items.Count > 0) input.SelectedIndex = 0; Fields.Children.Add(input); return input; }
    protected CheckBox Check(string name, string label, bool value)
    { var input = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 12, 0, 0) }; RegisterName(name, input); Fields.Children.Add(input); return input; }
    protected void Finish() => Fields.Children.Add(Message);
    protected Action FocusLinks(Func<Guid?>? todoId, Func<Guid?> projectId)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var todo = new Button { Content = "查看关联待办", Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 8, 0) };
        todo.Click += (_, _) =>
        {
            var item = Main.Todos.Items.FirstOrDefault(item => item.Id == todoId?.Invoke());
            if (item is null) { Message.Text = "请先关联一个待办。"; return; }
            if (new TodoDetailsWindow(Main, item) { Owner = this }.ShowDialog() == true) { FocusRequested = true; DialogResult = false; }
        }; if (todoId is not null) row.Children.Add(todo);
        var project = new Button { Content = "进入关联计时项目", Padding = new Thickness(10, 6, 10, 6) };
        project.Click += (_, _) =>
        {
            if (!Main.CanConfigure || Main.HasUnsavedRecord) { ConfirmDeletionWindow.ShowNotice(this, "请先结束当前计时并保存记录，再更换项目。"); return; }
            var item = Main.Projects.FirstOrDefault(item => item.Id == projectId());
            if (item is null) { Message.Text = "请先关联一个可用的计时项目。"; return; }
            Main.SelectedFolder = Main.Folders.Single(folder => folder.Name == item.Category); Main.SelectedProject = Main.FolderProjects.Single(project => project.Id == item.Id);
            Main.SelectedFocusTodo = Main.FocusTodoChoices[0]; FocusRequested = true; DialogResult = false;
        }; row.Children.Add(project); Fields.Children.Add(row);
        void Refresh() { todo.IsEnabled = todoId?.Invoke() is not null; project.IsEnabled = projectId() is not null; }
        Refresh(); return Refresh;
    }
    protected void Commit(Action action)
    { try { action(); Main.ReloadCalendarData(); DialogResult = true; } catch (Exception ex) { Message.Text = ex.Message; } }
    protected static bool ParseTime(string text, out TimeOnly? time)
    { time = null; if (string.IsNullOrWhiteSpace(text)) return true; if (!TimeOnly.TryParseExact(text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return false; time = parsed; return true; }
    protected abstract void Save();
    protected virtual void Delete() { }
}
