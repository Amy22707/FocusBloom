using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private int _calendarView;
    private bool _moodMonth;
    private string? _dateGroup;
    private static DateOnly Month(DateOnly date) => new(date.Year, date.Month, 1);
    private static DateOnly MonthEnd(DateOnly date) => Month(date).AddMonths(1).AddDays(-1);

    private void CalendarPage()
    {
        var view = Choice([T("月历", "Month"), T("当天日程", "Day"), T("日期事项", "Date events"), T("课表", "Classes")], _calendarView);
        view.SelectionChanged += (_, _) => { _calendarView = view.SelectedIndex; Refresh(); };
        _page.Children.Add(Row(view, AsyncButton(T("新建日期事项", "New date event"), () => EditDate(null)),
            AsyncButton(T("安排时间", "Schedule time"), () => EditSchedule(null))));
        if (_calendarView == 2) { DateEventsPage(); return; }
        if (_calendarView == 3) { CoursesPage(); return; }
        if (_calendarView == 0)
        {
            _page.Children.Add(MonthNavigation());
            var byDay = Entries(Month(_day), MonthEnd(_day)).ToLookup(entry => entry.Date);
            _page.Children.Add(MonthGrid(date => byDay[date].Select(entry => (Control)Label(entry.Title, 11)),
                date => { _day = date; _calendarView = 1; Refresh(); }));
            _page.Children.Add(AsyncButton(T("导出本月日程", "Export month as ICS"), () => ExportCalendar(Month(_day), MonthEnd(_day))));
        }
        else
        {
            _page.Children.Add(DayNavigation());
            var entries = Entries(_day, _day);
            if (entries.Count == 0) Empty();
            foreach (var entry in entries)
            {
                var detail = (entry.Start is { } start ? $"{start:HH:mm} · " : "") + entry.Detail;
                var plan = _store.GetCalendarPlans().FirstOrDefault(p => p.Id == entry.Id);
                var actions = plan is not null ? Row(AsyncButton(T("编辑", "Edit"), () => EditSchedule(plan)),
                    AsyncButton(T("删除", "Delete"), () => Delete(plan.Title, () => _store.DeleteCalendarPlan(plan.Id)))) : new WrapPanel();
                _page.Children.Add(Card(Label(entry.Title, 16, true), Label(detail), actions));
            }
        }
    }

    private IReadOnlyList<CalendarEntry> Entries(DateOnly from, DateOnly to)
    {
        var entries = new List<CalendarEntry>(CalendarRules.CourseEntries(_store.GetSchoolTerm(), _store.GetCourses(), _store.GetCourseChanges(), from, to));
        foreach (var item in _store.GetDateEvents())
            entries.AddRange(CalendarRules.Occurrences(item, from, to).Select(day => new CalendarEntry("日期事项", item.Id, item.Title, day, item.Time, Detail: item.Notes)));
        entries.AddRange(_store.GetTodos().Where(todo => todo.DueDate is { } date && date >= from && date <= to)
            .Select(todo => new CalendarEntry("待办", todo.Id, todo.Title, todo.DueDate!.Value, todo.DueTime, Detail: todo.Notes)));
        entries.AddRange(_store.GetCalendarPlans().Where(p => p.Date >= from && p.Date <= to)
            .Select(p => new CalendarEntry("时间块", p.Id, p.Title, p.Date, p.Start, Detail: p.Notes, EndMinutes: p.Start.ToTimeSpan().TotalMinutes + p.Minutes)));
        entries.AddRange(_store.GetProjects().Where(p => p.Plan?.Deadline is { } due && due >= from && due <= to)
            .Select(p => new CalendarEntry("项目", p.Id, p.Name, p.Plan!.Deadline!.Value, Detail: p.Plan.Goal)));
        return entries.OrderBy(e => e.Date).ThenBy(e => e.Start).ToArray();
    }

    private Control MonthNavigation() => Row(Button("‹", () => { _day = Month(_day).AddMonths(-1); Refresh(); }), Label($"{_day:yyyy-MM}", 20, true),
        Button("›", () => { _day = Month(_day).AddMonths(1); Refresh(); }), Button(T("本月", "This month"), () => { _day = Today; Refresh(); }));

    private Grid MonthGrid(Func<DateOnly, IEnumerable<Control>> content, Action<DateOnly> open)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 4, RowSpacing = 4 };
        string[] weekdays = _english ? ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"] : ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
        for (var column = 0; column < weekdays.Length; column++)
        {
            var label = Label(weekdays[column]); label.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(label, column); grid.Children.Add(label);
        }
        var month = Month(_day); var start = CalendarRules.Monday(month);
        for (var index = 0; index < 42; index++)
        {
            var date = start.AddDays(index);
            var button = Button(date.Day.ToString(), () => open(date), date == Today);
            button.Padding = new Thickness(5, 2); button.HorizontalAlignment = HorizontalAlignment.Center;
            var list = new StackPanel { Spacing = 3 };
            foreach (var item in content(date)) list.Children.Add(item);
            var items = new ScrollViewer { Content = list, Height = 62, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
            var panel = Card(button, items); panel.Padding = new Thickness(5); panel.Opacity = date.Month == month.Month ? 1 : 0.55;
            Grid.SetColumn(panel, index % 7); Grid.SetRow(panel, index / 7 + 1); grid.Children.Add(panel);
        }
        return grid;
    }

    private void DateEventsPage()
    {
        var groups = new[] { T("全部分组", "All groups") }.Concat(_store.GetDateGroups()).ToArray();
        var filter = Choice(groups, Math.Max(0, Array.IndexOf(groups, _dateGroup)));
        filter.SelectionChanged += (_, _) => { _dateGroup = filter.SelectedIndex <= 0 ? null : groups[filter.SelectedIndex]; Refresh(); };
        _page.Children.Add(filter);
        var items = _store.GetDateEvents().Where(item => _dateGroup is null || item.Group == _dateGroup).ToArray();
        if (items.Length == 0) Empty();
        foreach (var group in items.GroupBy(item => item.Group))
        {
            _page.Children.Add(Label(group.Key, 17, true));
            foreach (var item in group.OrderBy(item => CalendarRules.NextDate(item, Today)))
            {
                var date = CalendarRules.NextDate(item, Today) ?? item.Date;
                var difference = date.DayNumber - Today.DayNumber;
                var count = difference == 0 ? T("今天", "Today") : difference > 0 ? T($"还有 {difference} 天", $"{difference} days left") : T($"已过去 {-difference} 天", $"{-difference} days ago");
                _page.Children.Add(Card(Label(item.Title, 17, true), Label($"{date:yyyy-MM-dd} · {count}"), Label(item.Notes),
                    Row(AsyncButton(T("编辑", "Edit"), () => EditDate(item)), AsyncButton(T("删除", "Delete"), () => Delete(item.Title, () => _store.DeleteDateEvent(item.Id))))));
            }
        }
    }

    private async Task EditDate(DateEvent? existing)
    {
        var title = Input(existing?.Title ?? ""); var date = DateInput(existing?.Date ?? _day);
        var notes = Input(existing?.Notes ?? "", true); var group = Input(existing?.Group ?? _dateGroup ?? T("未分组", "Ungrouped"));
        var mode = Choice([T("倒数日", "Countdown"), T("正数日", "Count up")], (int)(existing?.Mode ?? DateCountMode.Countdown));
        var repeat = Choice([T("不重复", "Never"), T("每天", "Daily"), T("每月", "Monthly"), T("每年", "Yearly")], (int)(existing?.Repeat ?? DateRepeat.None));
        if (await Form(T("日期事项", "Date event"), [Field(T("名称", "Name"), title), Field(T("日期", "Date"), date), Field(T("分组", "Group"), group),
            Row(Field(T("计数方式", "Counter"), mode), Field(T("重复", "Repeat"), repeat)), Field(T("备注", "Notes"), notes)], () =>
        {
            var day = DateValue(date) ?? throw new FormatException(T("请选择日期。", "Select a date."));
            var name = Required(title); var category = Required(group);
            _store.SaveDateGroup(category);
            _store.SaveDateEvent((existing ?? new DateEvent(Guid.NewGuid(), name, day)) with
            { Title = name, Date = day, Group = category, Notes = Text(notes), Mode = (DateCountMode)mode.SelectedIndex, Repeat = (DateRepeat)repeat.SelectedIndex });
        })) Refresh();
    }

    private async Task EditSchedule(CalendarPlan? existing)
    {
        var title = Input(existing?.Title ?? ""); var date = DateInput(existing?.Date ?? _day);
        var start = Input(existing?.Start.ToString("HH:mm") ?? "09:00"); var minutes = Input((existing?.Minutes ?? 30).ToString()); var notes = Input(existing?.Notes ?? "", true);
        if (await Form(T("安排时间", "Schedule time"), [Field(T("名称", "Name"), title), Field(T("日期", "Date"), date),
            Row(Field(T("开始时间 (HH:mm)", "Start time (HH:mm)"), start), Field(T("时长（分钟）", "Minutes"), minutes)), Field(T("备注", "Notes"), notes)], () =>
        {
            var day = DateValue(date) ?? throw new FormatException(T("请选择日期。", "Select a date."));
            if (!TimeOnly.TryParse(Text(start), out var time)) throw new FormatException(T("请输入有效的开始时间。", "Enter a valid start time."));
            var name = Required(title);
            _store.SaveCalendarPlan((existing ?? new CalendarPlan(Guid.NewGuid(), name, day, time)) with
                { Title = name, Date = day, Start = time, Minutes = Number(minutes, 1, 1440), Notes = Text(notes) });
        })) Refresh();
    }

    private async Task ExportCalendar(DateOnly from, DateOnly to)
    {
        if (await SaveFile(T("导出日历", "Export calendar"), $"FocusBloom-{from:yyyy-MM}", "ics") is not { } path) return;
        await File.WriteAllTextAsync(path, IcsExporter.Entries(Entries(from, to)));
        Report(path);
    }

    private void CoursesPage()
    {
        var semesters = _store.GetSchoolSemesters();
        var semester = Choice(semesters.Select(item => item.Name), Math.Max(0, semesters.ToList().FindIndex(item => item.Active)));
        semester.SelectionChanged += (_, _) => { if (semester.SelectedIndex >= 0) { _store.SelectSchoolSemester(semesters[semester.SelectedIndex].Id); Refresh(); } };
        _page.Children.Add(Row(semester, AsyncButton(T("新建学期", "New term"), async () =>
        {
            var name = Input(); var start = DateInput(Today);
            if (await Form(T("新建学期", "New term"), [Field(T("名称", "Name"), name), Field(T("开学日期", "Start date"), start)],
                () => _store.CreateSchoolSemester(Required(name), DateValue(start) ?? Today))) Refresh();
        }), AsyncButton(T("导入 CSV", "Import CSV"), async () =>
        {
            if (await OpenFile(T("选择课程 CSV", "Select class CSV"), "*.csv") is not { } path) return;
            using var reader = File.OpenText(path); var imported = CourseCsv.Read(reader, _store.GetSchoolTerm(), _store.GetCourses());
            _store.ImportCourses(imported); Refresh(); Report(T($"已导入 {imported.Count} 门课程。", $"Imported {imported.Count} classes."));
        })));
        _page.Children.Add(DayNavigation());
        var monday = CalendarRules.Monday(_day);
        var entries = CalendarRules.CourseEntries(_store.GetSchoolTerm(), _store.GetCourses(), _store.GetCourseChanges(), monday, monday.AddDays(6));
        if (entries.Count == 0) Empty();
        foreach (var group in entries.OrderBy(e => e.Date).ThenBy(e => e.Start).GroupBy(e => e.Date))
        {
            _page.Children.Add(Label(group.Key.ToString("yyyy-MM-dd"), 17, true));
            foreach (var entry in group) _page.Children.Add(Card(Label(entry.Title, 16, true), Label($"{entry.Start:HH:mm}–{entry.End:HH:mm}")));
        }
    }

    private void HabitsPage()
    {
        if (_day > Today) _day = Today;
        _page.Children.Add(DayNavigation(false));
        _page.Children.Add(Row(AsyncButton(T("新建习惯", "New habit"), () => EditHabit(null), true), AsyncButton(T("新建目标", "New goal"), () => EditGoal(null))));
        var habits = _store.GetHabits(); var checks = _store.GetHabitCheckIns(); var records = _store.GetRecords();
        if (habits.Count == 0 && _store.GetGoals().Count == 0) Empty();
        foreach (var habit in habits)
        {
            var check = new CheckBox { Content = habit.Name, IsChecked = checks.Any(c => c.HabitId == habit.Id && c.Date == _day), IsEnabled = GoalRules.Due(habit, _day) };
            check.IsCheckedChanged += (_, _) => { _store.SetHabitCheckIn(habit.Id, _day, check.IsChecked == true); Refresh(); };
            var status = GoalRules.HabitStatus(habit, checks, _day);
            _page.Children.Add(Card(check, Label(T($"本月 {status.Completed}/{status.Planned} 天 · 连续 {status.Streak} 天", $"This month {status.Completed}/{status.Planned} days · streak {status.Streak}")),
                Row(AsyncButton(T("编辑", "Edit"), () => EditHabit(habit)), AsyncButton(T("删除", "Delete"), () => Delete(habit.Name, () => _store.DeleteHabit(habit.Id))))));
        }
        foreach (var goal in _store.GetGoals())
        {
            var progress = GoalRules.Progress(goal, records, checks, _day);
            _page.Children.Add(Card(Label(goal.Name, 17, true), Label($"{progress.Value:0.#} / {goal.Target:0.#}"),
                new ProgressBar { Maximum = 1, Value = Math.Max(0, progress.Ratio), Height = 5 },
                Row(AsyncButton(T("编辑", "Edit"), () => EditGoal(goal)), AsyncButton(T("删除", "Delete"), () => Delete(goal.Name, () => _store.DeleteGoal(goal.Id))))));
        }
    }

    private async Task EditHabit(Habit? existing)
    {
        var name = Input(existing?.Name ?? ""); var start = DateInput(existing?.Start ?? _day); var until = DateInput(existing?.Until);
        var days = Enumerable.Range(1, 7).Select(day => new CheckBox { Content = _english ? new DateTime(2024, 1, day).ToString("ddd", System.Globalization.CultureInfo.InvariantCulture) : $"周{("一二三四五六日"[day - 1])}", IsChecked = existing?.Weekdays.Contains(day) ?? true }).ToArray();
        if (await Form(T("习惯", "Habit"), [Field(T("名称", "Name"), name), Field(T("重复日期", "Repeat on"), Row(days)),
            Field(T("开始日期", "Start date"), start), Field(T("结束日期", "End date"), until)], () =>
        {
            var weekdays = Enumerable.Range(1, 7).Where(day => days[day - 1].IsChecked == true).ToArray();
            if (weekdays.Length == 0) throw new FormatException(T("至少选择一天。", "Select at least one day."));
            var title = Required(name); var from = DateValue(start) ?? _day; var to = DateValue(until);
            if (to < from) throw new FormatException(T("结束日期早于开始日期。", "The end date is before the start date."));
            _store.SaveHabit((existing ?? new Habit(Guid.NewGuid(), title, weekdays, from)) with { Name = title, Weekdays = weekdays, Start = from, Until = to });
        })) Refresh();
    }

    private async Task EditGoal(FocusGoal? existing)
    {
        var name = Input(existing?.Name ?? ""); var target = Input(existing?.Target.ToString() ?? "30");
        var metric = Choice([T("专注分钟", "Focus minutes"), T("专注次数", "Sessions"), T("习惯打卡天数", "Habit days")], (int)(existing?.Metric ?? GoalMetric.Minutes));
        var cycle = Choice([T("每日", "Daily"), T("每周", "Weekly"), T("每月", "Monthly"), T("累计", "Total")], (int)(existing?.Cycle ?? GoalCycle.Daily));
        var habits = _store.GetHabits(); var habit = Choice(habits.Select(h => h.Name), Math.Max(0, habits.ToList().FindIndex(h => h.Id == existing?.HabitId)));
        var habitField = Field(T("关联习惯", "Habit"), habit); habitField.IsVisible = metric.SelectedIndex == 2;
        metric.SelectionChanged += (_, _) => habitField.IsVisible = metric.SelectedIndex == 2;
        if (await Form(T("目标", "Goal"), [Field(T("名称", "Name"), name), Field(T("目标值", "Target"), target),
            Field(T("统计方式", "Metric"), metric), Field(T("周期", "Period"), cycle), habitField], () =>
        {
            if (metric.SelectedIndex == 2 && habit.SelectedIndex < 0) throw new FormatException(T("请先创建一个习惯。", "Create a habit first."));
            var title = Required(name);
            _store.SaveGoal(new(existing?.Id ?? Guid.NewGuid(), title, (GoalMetric)metric.SelectedIndex, Number(target, 1, 100000),
                (GoalCycle)cycle.SelectedIndex, existing?.Start ?? _day, existing?.Until, existing?.ProjectId, metric.SelectedIndex == 2 ? habits[habit.SelectedIndex].Id : null));
        })) Refresh();
    }

    private string MoodName(int value) => _english ? value switch { 1 => "Low", 2 => "Tired", 3 => "Calm", 4 => "Happy", 5 => "Excited", 6 => "Irritated", _ => "" } : ReflectionRules.Mood(value).Name;
    private void ReflectionPage()
    {
        if (_day > Today) _day = Today;
        var view = Choice([T("每日记录", "Day view"), T("心情月历", "Month view")], _moodMonth ? 1 : 0);
        view.MinWidth = 165; view.SelectionChanged += (_, _) => { _moodMonth = view.SelectedIndex == 1; Refresh(); };
        _page.Children.Add(view);
        var reflections = _store.GetDailyReflections().ToDictionary(item => item.Date);
        if (_moodMonth)
        {
            _page.Children.Add(MonthNavigation());
            _page.Children.Add(MonthGrid(date => reflections.GetValueOrDefault(date)?.Mood is { } mood
                ? [new Border { CornerRadius = new CornerRadius(11), Padding = new Thickness(4), Background = new SolidColorBrush(Color.Parse(ReflectionRules.Mood(mood).Color)), Child = new TextBlock { Text = MoodName(mood), Foreground = Brushes.Black, FontSize = 11 } }] : [],
                date => { if (date <= Today) { _day = date; _moodMonth = false; Refresh(); } }));
            return;
        }
        _page.Children.Add(DayNavigation(false));
        var existing = reflections.GetValueOrDefault(_day);
        if (existing is not null)
        {
            _page.Children.Add(Card(Label(existing.Mood is { } mood ? MoodName(mood) : T("复盘", "Reflection"), 21, true), Label(existing.Feeling), Label(existing.Highlights), Label(existing.NextStep)));
            foreach (var path in existing.Photos ?? [])
                if (ReadBitmap(path) is { } photo) _page.Children.Add(new Image { Source = photo, MaxHeight = 300, Stretch = Stretch.Uniform });
        }
        else Empty();
        _page.Children.Add(Row(AsyncButton(T("编辑记录", "Edit entry"), () => EditReflection(existing), true),
            existing is null ? new TextBlock() : AsyncButton(T("删除", "Delete"), () => Delete(_day.ToString("yyyy-MM-dd"), () => _store.DeleteDailyReflection(_day)))));
    }

    private async Task EditReflection(DailyReflection? existing)
    {
        var mood = Choice(new[] { T("未选择", "No mood") }.Concat(ReflectionRules.Moods.Select(item => MoodName(item.Value))), existing?.Mood ?? 0);
        var feeling = Input(existing?.Feeling ?? "", true); var highlights = Input(existing?.Highlights ?? "", true); var next = Input(existing?.NextStep ?? "", true);
        var files = (existing?.Photos ?? []).ToList(); var photos = new StackPanel { Spacing = 5 };
        void RenderPhotos()
        {
            photos.Children.Clear();
            foreach (var path in files) photos.Children.Add(Row(Label(Path.GetFileName(path)), Button(T("移除", "Remove"), () => { files.Remove(path); RenderPhotos(); })));
        }
        RenderPhotos();
        var add = AsyncButton(T("添加图片", "Add image"), async () =>
        {
            if (await OpenFile(T("选择图片", "Select image"), "*.png", "*.jpg", "*.jpeg", "*.gif", "*.bmp") is { } path) { files.Add(path); RenderPhotos(); }
        });
        if (await Form(T("每日复盘", "Daily reflection"), [Field(T("心情", "Mood"), mood), Field(T("感受", "How you felt"), feeling),
            Field(T("值得记住的事", "Highlights"), highlights), Field(T("下一步", "Next step"), next), photos, add], () =>
        {
            var local = files.Select(path => (existing?.Photos ?? []).Contains(path) ? path : CopyMedia(path, "reflection-images")).ToArray();
            _store.SaveDailyReflection(ReflectionRules.Normalize(new(_day, mood.SelectedIndex == 0 ? null : mood.SelectedIndex,
                existing?.Tags ?? [], Text(feeling), Text(highlights), Text(next), local), Today));
        })) Refresh();
    }
}
