using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.Windows;

namespace FocusBloom.Desktop.Views;

public sealed class PlannerView : UserControl
{
    private readonly PeriodicChecklist _checklist;
    private readonly Action<ChecklistEntry> _startFocus;
    private readonly Action<ChecklistEntry> _showHistory;
    private readonly DatePicker _date = new() { SelectedDate = DateTime.Today, Width = 150, Margin = new Thickness(8, 0, 0, 0) };
    private readonly ComboBox _period = new() { Width = 120 };
    private readonly CheckBox _split = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(0, 0, 0, 12) };
    private readonly TextBlock _heading = new() { Margin = new Thickness(0, 16, 0, 8) };
    private readonly TextBox _draft = new() { MaxLength = 500 };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private ChecklistScope _scope;
    private readonly Dictionary<ChecklistScope, string> _drafts = new();
    private bool _loaded;
    private IReadOnlyList<ChecklistEntry> _entries = [];

    public PlannerView(PeriodicChecklist checklist, Action<ChecklistEntry> startFocus, Action<ChecklistEntry> showHistory)
    {
        _checklist = checklist;
        _startFocus = startFocus;
        _showHistory = showHistory;
        Language = XmlLanguage.GetLanguage(UiText.IsEnglish ? "en-US" : "zh-CN");
        _date.Language = Language;
        var root = new DockPanel();
        Content = root;
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var title = new TextBlock { Text = UiText.Get("计划本"), Margin = new Thickness(0, 0, 0, 14) };
        title.SetResourceReference(StyleProperty, "PageTitle");
        header.Children.Add(title);
        var filters = new WrapPanel();
        header.Children.Add(filters);
        foreach (var name in new[] { "日清单", "周清单", "月清单" }) _period.Items.Add(UiText.Get(name));
        _period.Name = "PlannerPeriod";
        _date.Name = "PlannerDate";
        _split.Name = "PlannerSplit";
        _draft.Name = "PlannerDraft";
        _rows.Name = "PlannerRows";
        filters.Children.Add(_period);
        var previous = new Button { Content = "‹", Margin = new Thickness(8, 0, 0, 0), ToolTip = UiText.Get("上一页") };
        previous.Click += (_, _) => Move(-1);
        filters.Children.Add(previous);
        filters.Children.Add(_date);
        var next = new Button { Content = "›", Margin = new Thickness(8, 0, 0, 0), ToolTip = UiText.Get("下一页") };
        next.Click += (_, _) => Move(1);
        filters.Children.Add(next);
        var today = new Button { Content = UiText.Get("今天"), Margin = new Thickness(8, 0, 0, 0) };
        today.Click += (_, _) => _date.SelectedDate = DateTime.Today;
        filters.Children.Add(today);
        filters.Children.Add(_split);
        _heading.SetResourceReference(StyleProperty, "SectionTitle");
        header.Children.Add(_heading);
        _summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        header.Children.Add(_summary);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        root.Children.Add(body);
        var sheet = new DockPanel();
        var card = new Border { Child = sheet, Margin = new Thickness(0, 0, 18, 0) };
        card.SetResourceReference(StyleProperty, "Card");
        body.Children.Add(card);
        var dates = new StackPanel();
        var browseTitle = new TextBlock { Text = UiText.Get("浏览日期"), Margin = new Thickness(0, 0, 0, 12) };
        browseTitle.SetResourceReference(StyleProperty, "SectionTitle");
        dates.Children.Add(browseTitle);
        var calendar = new Calendar { Language = Language, SelectedDate = _date.SelectedDate, FirstDayOfWeek = DayOfWeek.Monday, HorizontalAlignment = HorizontalAlignment.Left };
        calendar.SetResourceReference(StyleProperty, "PlannerCalendar");
        calendar.SelectedDatesChanged += (_, _) =>
        {
            if (calendar.SelectedDate is { } day) _date.SelectedDate = day;
        };
        _date.SelectedDateChanged += (_, _) =>
        {
            calendar.SelectedDate = _date.SelectedDate;
            if (_date.SelectedDate is { } day) calendar.DisplayDate = day;
        };
        dates.Children.Add(calendar);
        var dateCard = new Border { Child = dates, VerticalAlignment = VerticalAlignment.Top };
        dateCard.SetResourceReference(StyleProperty, "Card");
        Grid.SetColumn(dateCard, 1);
        body.Children.Add(dateCard);
        var footer = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        sheet.Children.Add(footer);
        var input = new DockPanel();
        footer.Children.Add(input);
        var add = new Button { Name = "PlannerAdd", Content = UiText.Get("添加"), Margin = new Thickness(8, 0, 0, 0) };
        add.SetResourceReference(StyleProperty, "PrimaryButton");
        DockPanel.SetDock(add, Dock.Right);
        input.Children.Add(add);
        input.Children.Add(_draft);
        footer.Children.Add(_error);
        sheet.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        add.Click += (_, _) => Add();
        _draft.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        _date.SelectedDateChanged += (_, _) => Load();
        _split.Checked += (_, _) => Load();
        _split.Unchecked += (_, _) => Load();
        _period.SelectionChanged += (_, _) => Load();
        _period.SelectedIndex = 0;
    }

    private void Move(int direction)
    {
        var date = _date.SelectedDate ?? DateTime.Today;
        _date.SelectedDate = (ChecklistPeriod)_period.SelectedIndex switch
        {
            ChecklistPeriod.Month when _split.IsChecked != true => date.AddMonths(direction),
            ChecklistPeriod.Week when _split.IsChecked != true => date.AddDays(direction * 7),
            ChecklistPeriod.Month => date.AddDays(direction * 7),
            _ => date.AddDays(direction)
        };
    }

    private void Load()
    {
        if (_loaded) _drafts[_scope] = _draft.Text;
        var period = (ChecklistPeriod)_period.SelectedIndex;
        var date = DateOnly.FromDateTime(_date.SelectedDate ?? DateTime.Today);
        _split.Visibility = period == ChecklistPeriod.Day ? Visibility.Collapsed : Visibility.Visible;
        _split.Content = UiText.Get(period == ChecklistPeriod.Week ? "按天拆分" : "按周拆分");
        _scope = ChecklistScope.For(period, date, _split.IsChecked == true);
        _draft.Text = _drafts.GetValueOrDefault(_scope, "");
        _loaded = true;
        var selectedDay = _scope.Section ?? _scope.Start;
        _heading.Text = period == ChecklistPeriod.Month && _scope.Section is not null
            ? UiText.Format("本月 · {0} 起的一周", (selectedDay < _scope.Start ? _scope.Start : selectedDay).ToString("d", UiText.DateCulture))
            : period == ChecklistPeriod.Week && _scope.Section is null
                ? $"{_scope.Start.ToString("d", UiText.DateCulture)} — {_scope.Start.AddDays(6).ToString("d", UiText.DateCulture)}"
                : selectedDay.ToString(period == ChecklistPeriod.Month ? "Y" : "D", UiText.DateCulture);
        _error.Text = "";
        _entries = _checklist.Read(_scope);
        Render();
    }

    private void Add()
    {
        var text = _draft.Text.Trim();
        if (text.Length == 0) return;
        if (Save([.. _entries, new ChecklistEntry(Guid.NewGuid(), text, false)])) _draft.Clear();
        _draft.Focus();
    }

    private bool Save(ChecklistEntry[] entries)
    {
        try { _checklist.Save(_scope, entries); }
        catch (Exception error) { _error.Text = UiText.Format("清单保存失败：{0}", error.Message); Render(); return false; }
        _entries = entries;
        _error.Text = "";
        Render();
        return true;
    }

    private void Render()
    {
        _summary.Text = UiText.Format("已完成 {0} / {1}", _entries.Count(item => item.Done), _entries.Count);
        _rows.Children.Clear();
        if (_entries.Count == 0) _rows.Children.Add(new TextBlock { Text = UiText.Get("这个周期还没有清单事项。") });
        foreach (var item in _entries)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 8, 12) };
            var remove = new Button { Content = "×", ToolTip = UiText.Get("删除"), Padding = new Thickness(8, 3, 8, 3) };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += (_, _) =>
            {
                if (ConfirmDeletionWindow.Ask(Window.GetWindow(this), UiText.Format("删除计划事项「{0}」？", item.Text)))
                    Save(_entries.Where(entry => entry.Id != item.Id).ToArray());
            };
            row.Children.Add(remove);
            var history = new Button { Content = "◷", ToolTip = UiText.Get("专注记录"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
            history.Click += (_, _) => _showHistory(item);
            DockPanel.SetDock(history, Dock.Right);
            row.Children.Add(history);
            var focus = new Button { Content = "▷", ToolTip = UiText.Get("开始专注"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
            System.Windows.Automation.AutomationProperties.SetName(focus, UiText.Get("开始专注"));
            focus.Click += (_, _) => _startFocus(item);
            DockPanel.SetDock(focus, Dock.Right);
            row.Children.Add(focus);
            var edit = new Button { Content = "✎", ToolTip = UiText.Get("编辑计划事项"), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0) };
            System.Windows.Automation.AutomationProperties.SetName(edit, UiText.Get("编辑计划事项"));
            edit.Click += (_, _) =>
            {
                var editor = new PlannerItemEditorWindow(item.Text) { Owner = Window.GetWindow(this) };
                if (editor.ShowDialog() == true && editor.ItemText != item.Text)
                    Save(_entries.Select(entry => entry.Id == item.Id ? entry with { Text = editor.ItemText } : entry).ToArray());
            };
            DockPanel.SetDock(edit, Dock.Right);
            row.Children.Add(edit);
            var text = new TextBlock { Text = item.Text, TextWrapping = TextWrapping.Wrap };
            if (item.Done)
            {
                text.TextDecorations = TextDecorations.Strikethrough;
                text.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            }
            var check = new CheckBox { IsChecked = item.Done, Content = text, VerticalAlignment = VerticalAlignment.Center };
            check.Click += (_, _) => Save(_entries.Select(entry => entry.Id == item.Id ? entry with { Done = check.IsChecked == true } : entry).ToArray());
            row.Children.Add(check);
            _rows.Children.Add(row);
        }
    }
}
