using Avalonia.Controls;
using Avalonia.Media;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private readonly string[] _colors = ["#427B72", "#527B9A", "#8B709B", "#AA735C", "#A17F48"];
    private Guid? _taskFolder;
    private ChecklistPeriod _period;
    private bool _subdivide;

    private void ProjectsPage()
    {
        _page.Children.Add(AsyncButton(T("新建项目", "New project"), () => EditProject(null), true));
        foreach (var group in _store.GetProjects().GroupBy(item => item.Category))
        {
            _page.Children.Add(Label(group.Key, 17, true));
            foreach (var project in group)
            {
                var time = _store.GetRecords().Where(record => record.ProjectId == project.Id).Sum(record => record.FocusDuration.TotalMinutes);
                var detail = project.Plan?.Deadline is { } deadline ? $"{deadline:yyyy-MM-dd} · " : "";
                _page.Children.Add(Card(Label(project.Name, 17, true), Label(detail + T($"{time:0} 分钟", $"{time:0} min")),
                    Row(AsyncButton(T("专注", "Focus"), () => StartFocus(project: project)), AsyncButton(T("编辑", "Edit"), () => EditProject(project)),
                        project.Id == SqliteFocusStore.DefaultProjectId ? new TextBlock() : AsyncButton(T("删除", "Delete"), () => Delete(project.Name, () => _store.DeleteProject(project.Id))))));
            }
        }
    }

    private async Task EditProject(FocusProject? existing)
    {
        var name = Input(existing?.Name ?? ""); var group = Input(existing?.Category ?? T("个人", "Personal"));
        var goal = Input(existing?.Plan?.Goal ?? "", true); var deadline = DateInput(existing?.Plan?.Deadline);
        if (await Form(T("项目", "Project"), [Field(T("名称", "Name"), name), Field(T("分组", "Group"), group),
            Field(T("目标", "Goal"), goal), Field(T("截止日期", "Due date"), deadline)], () =>
        {
            var title = Required(name); var category = Required(group);
            var plan = new ProjectPlan(Text(goal), DateValue(deadline), existing?.Plan?.Milestones ?? []);
            _store.SaveFolder(category);
            _store.SaveProject(new(existing?.Id ?? Guid.NewGuid(), title, category, existing?.Color ?? _colors[0], plan));
        })) Refresh();
    }

    private void TasksPage()
    {
        var folders = _store.GetTodoFolders();
        var selected = _taskFolder is { } id ? folders.ToList().FindIndex(f => f.Id == id) + 1 : 0;
        var filter = Choice(new[] { T("全部文件夹", "All folders") }.Concat(folders.Select(f => f.Name)), selected);
        filter.SelectionChanged += (_, _) => { _taskFolder = filter.SelectedIndex > 0 ? folders[filter.SelectedIndex - 1].Id : null; Refresh(); };
        _page.Children.Add(Row(filter, AsyncButton(T("新建待办", "New task"), () => EditTask(null), true),
            AsyncButton(T("新建文件夹", "New folder"), NewTaskFolder)));
        var tasks = _store.GetTodos().Where(todo => _taskFolder is null || todo.FolderId == _taskFolder).ToArray();
        if (tasks.Length == 0) Empty();
        foreach (var group in tasks.GroupBy(todo => todo.FolderId))
        {
            _page.Children.Add(Label(folders.FirstOrDefault(f => f.Id == group.Key)?.Name ?? T("未分组", "Ungrouped"), 17, true));
            foreach (var todo in group.OrderBy(todo => todo.Status == TodoStatus.Completed).ThenBy(todo => todo.DueDate ?? DateOnly.MaxValue))
            {
                var done = todo.Status == TodoStatus.Completed;
                var check = new CheckBox { IsChecked = done, Content = new TextBlock { Text = todo.Title, TextDecorations = done ? TextDecorations.Strikethrough : null } };
                check.IsCheckedChanged += (_, _) =>
                {
                    _store.SaveTodo(todo with { Status = check.IsChecked == true ? TodoStatus.Completed : TodoStatus.Pending,
                        CompletedAt = check.IsChecked == true ? DateTimeOffset.Now : null }); Refresh();
                };
                _page.Children.Add(Card(check, Label(string.Join(" · ", new[] { todo.DueDate?.ToString("yyyy-MM-dd"), todo.Notes }.Where(t => !string.IsNullOrWhiteSpace(t)))),
                    Row(AsyncButton(T("专注", "Focus"), () => StartFocus(todo: todo)), AsyncButton(T("编辑", "Edit"), () => EditTask(todo)),
                        AsyncButton(T("删除", "Delete"), () => Delete(todo.Title, () => _store.DeleteTodo(todo.Id))))));
            }
        }
    }

    private async Task NewTaskFolder()
    {
        var name = Input(); var category = Input(T("个人", "Personal"));
        if (await Form(T("新建文件夹", "New folder"), [Field(T("名称", "Name"), name), Field(T("分类", "Category"), category)], () =>
        {
            var group = Required(category); var title = Required(name);
            var id = Guid.NewGuid(); _store.SaveTodoCategory(group); _store.SaveTodoFolder(new(id, title, group)); _taskFolder = id;
        })) Refresh();
    }

    private async Task EditTask(TodoItem? existing)
    {
        var folders = _store.GetTodoFolders();
        if (folders.Count == 0) { await NewTaskFolder(); folders = _store.GetTodoFolders(); }
        if (folders.Count == 0) return;
        var name = Input(existing?.Title ?? ""); var notes = Input(existing?.Notes ?? "", true); var date = DateInput(existing?.DueDate);
        var folder = Choice(folders.Select(f => f.Name), Math.Max(0, folders.ToList().FindIndex(f => f.Id == (existing?.FolderId ?? _taskFolder))));
        var important = new CheckBox { Content = T("重要", "Important"), IsChecked = existing?.Important ?? false };
        var urgent = new CheckBox { Content = T("紧急", "Urgent"), IsChecked = existing?.Urgent ?? false };
        if (await Form(T("待办事项", "Task"), [Field(T("名称", "Name"), name), Field(T("文件夹", "Folder"), folder),
            Field(T("截止日期", "Due date"), date), Field(T("备注", "Notes"), notes), Row(important, urgent)], () =>
        {
            var title = Required(name); var folderId = folders[folder.SelectedIndex].Id;
            var task = existing ?? new TodoItem(Guid.NewGuid(), folderId, title);
            _store.SaveTodo(task with { Title = title, FolderId = folderId, Notes = Text(notes), DueDate = DateValue(date),
                Important = important.IsChecked == true, Urgent = urgent.IsChecked == true });
        })) Refresh();
    }

    private void PlannerPage()
    {
        var period = Choice([T("日计划", "Daily"), T("周计划", "Weekly"), T("月计划", "Monthly")], (int)_period);
        period.SelectionChanged += (_, _) => { _period = (ChecklistPeriod)period.SelectedIndex; Refresh(); };
        var split = new CheckBox { Content = _period == ChecklistPeriod.Month ? T("按周", "By week") : T("按天", "By day"),
            IsChecked = _subdivide, IsVisible = _period != ChecklistPeriod.Day };
        split.IsCheckedChanged += (_, _) => { _subdivide = split.IsChecked == true; Refresh(); };
        var scope = ChecklistScope.For(_period, _day, _subdivide);
        var store = new PeriodicChecklist(_store); var entries = store.Read(scope);
        _page.Children.Add(DayNavigation());
        _page.Children.Add(Row(period, split, AsyncButton(T("添加计划", "Add plan"), () => EditPlan(null), true)));
        if (entries.Count == 0) Empty();
        foreach (var entry in entries)
        {
            var check = new CheckBox { IsChecked = entry.Done, Content = new TextBlock { Text = entry.Text,
                TextDecorations = entry.Done ? TextDecorations.Strikethrough : null, TextWrapping = TextWrapping.Wrap } };
            check.IsCheckedChanged += (_, _) => { store.Save(scope, store.Read(scope).Select(item => item.Id == entry.Id ? item with { Done = check.IsChecked == true } : item)); Refresh(); };
            var actions = Row(AsyncButton(T("专注", "Focus"), () => StartFocus(plan: entry)), AsyncButton(T("编辑", "Edit"), () => EditPlan(entry)),
                AsyncButton(T("删除", "Delete"), () => Delete(entry.Text, () => store.Save(scope, store.Read(scope).Where(item => item.Id != entry.Id)))));
            _page.Children.Add(Card(check, actions));
        }
        async Task EditPlan(ChecklistEntry? existing)
        {
            var text = Input(existing?.Text ?? "");
            if (await Form(T("计划事项", "Plan item"), [Field(T("内容", "Description"), text)], () =>
            {
                var title = Required(text); var list = store.Read(scope);
                store.Save(scope, existing is null ? list.Append(new(Guid.NewGuid(), title, false))
                    : list.Select(item => item.Id == existing.Id ? item with { Text = title } : item));
            })) Refresh();
        }
    }

    private void HistoryPage()
    {
        var from = DateInput(_day.AddDays(-6)); var until = DateInput(_day);
        var summary = new StackPanel { Spacing = 8 };
        void Load()
        {
            summary.Children.Clear();
            var filter = new StatisticsFilter(DateValue(from) ?? Today.AddDays(-6), DateValue(until) ?? Today);
            var snapshot = FocusStatistics.Build(_store.GetRecords(), filter);
            summary.Children.Add(Card(Label(Clock(snapshot.Duration), 24, true), Label(T($"{snapshot.Count} 次专注 · {snapshot.ActiveDays} 天", $"{snapshot.Count} sessions · {snapshot.ActiveDays} active days"))));
            foreach (var item in snapshot.Projects)
                summary.Children.Add(Card(Label(ProjectName(item.ProjectId), 15, true), Label(Clock(item.Duration))));
            foreach (var item in snapshot.Sessions)
            {
                var record = item.Record;
                var name = record.PlannerTitle ?? (record.TodoId is { } todo ? _store.GetTodos(true).FirstOrDefault(item => item.Id == todo)?.Title : null) ?? ProjectName(record.ProjectId);
                var outcome = _store.GetOutcome(record.Id);
                summary.Children.Add(Card(Label(name, 17, true), Label($"{record.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} · {Clock(item.IncludedDuration)}"),
                    Label(outcome?.Summary ?? ""), AsyncButton(T("编辑成果", "Edit outcome"), async () =>
                    {
                        var result = Input(outcome?.Summary ?? "", true); var next = Input(outcome?.NextStep ?? "", true);
                        if (await Form(T("专注成果", "Session outcome"), [Field(T("成果", "Outcome"), result), Field(T("下一步", "Next step"), next)],
                            () => _store.SaveOutcome(new(record.Id, Text(result), outcome?.Quantity, outcome?.Unit ?? "", Text(next))))) Load();
                    })));
            }
        }
        _page.Children.Add(Row(Field(T("从", "From"), from), Field(T("到", "To"), until)));
        from.SelectedDateChanged += (_, _) => Load(); until.SelectedDateChanged += (_, _) => Load();
        _page.Children.Add(summary); Load();
    }
}
