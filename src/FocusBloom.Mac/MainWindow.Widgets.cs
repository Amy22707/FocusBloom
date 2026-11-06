using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Statistics;
using FocusBloom.Core.Todos;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private sealed record WidgetPreferences(bool Enabled = false, string View = "列表", string Period = "日", bool Topmost = false,
        double Transparency = 0, double Width = 340, double Height = 350, double? Left = null, double? Top = null,
        string BackgroundMode = "跟随主题", string BackgroundColor = "#E8EEEB", string? BackgroundImage = null)
    {
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
    }
    private sealed class WidgetWindow : Window
    {
        internal DateOnly Day = Today;
        internal StackPanel Body = new() { Spacing = 10, Margin = new Thickness(14) };
        internal string Version = "";
        internal Avalonia.Media.Imaging.Bitmap? BackgroundBitmap;
    }
    private readonly Dictionary<string, WidgetWindow> _widgets = [];
    private bool _closingWidgets;
    private DateTimeOffset _lastWidgetUpdate;
    private readonly string[] _widgetKeys = ["Timer", "Focus", "Todos", "Dates", "Habits", "Mood", "Calendar"];
    private string WidgetName(string kind) => kind switch
    {
        "Timer" => T("专注计时", "Focus Timer"), "Focus" => T("专注时长", "Focus Time"), "Todos" => T("待办事项", "Tasks"),
        "Dates" => T("日期事项", "Date Events"), "Habits" => T("习惯打卡", "Habits"), "Mood" => T("心情月历", "Mood Calendar"), _ => T("日历", "Calendar")
    };
    private WidgetPreferences ReadWidget(string key) => _store.GetSetting("widgets." + key) is { } saved ? JsonSerializer.Deserialize<WidgetPreferences>(saved, Json) ?? new() : new();
    private void SaveWidget(string key, WidgetPreferences value) => _store.SaveSetting("widgets." + key, JsonSerializer.Serialize(value));
    private Control WidgetSettings()
    {
        var body = new StackPanel { Spacing = 12 };
        foreach (var kind in _widgetKeys)
        {
            var prefs = ReadWidget(kind);
            var enabled = new CheckBox { Content = WidgetName(kind), IsChecked = prefs.Enabled };
            enabled.IsCheckedChanged += (_, _) => { SaveWidget(kind, ReadWidget(kind) with { Enabled = enabled.IsChecked == true }); RefreshWidgets(true); };
            body.Children.Add(Row(enabled, AsyncButton(T("设置", "Settings"), () => EditWidget(kind))));
        }
        return Card(Label(T("桌面组件", "Desktop Widgets"), 17, true), body);
    }
    private async Task EditWidget(string key)
    {
        var prefs = ReadWidget(key);
        var topmost = new CheckBox { Content = T("置顶", "Keep on top"), IsChecked = prefs.Topmost };
        var opacity = new Slider { Minimum = 0, Maximum = 70, Value = prefs.Transparency, Width = 240 };
        var width = Input(prefs.Width.ToString("0")); var height = Input(prefs.Height.ToString("0"));
        var views = key switch
        {
            "Habits" => new[] { "日", "周", "月" }, "Calendar" => ["月", "周", "日"], "Mood" => ["月", "日"],
            "Focus" => ["数字", "热力图"], "Todos" => ["列表", "四象限"], _ => ["列表"]
        };
        var view = Choice(views.Select(WidgetViewName), Math.Max(0, Array.IndexOf(views, prefs.View)));
        var color = Input(prefs.BackgroundColor);
        var background = prefs.BackgroundImage;
        var photo = AsyncButton(T("选择背景图片", "Choose background image"), async () =>
        {
            if (await OpenFile(T("选择图片", "Choose image"), ImagePatterns) is { } path) background = CopyMedia(path, "widget-images");
        });
        if (await Form(WidgetName(key), [topmost, Field(T("视图", "View"), view), Field(T("透明度", "Transparency"), opacity),
            Row(Field(T("宽度", "Width"), width), Field(T("高度", "Height"), height)), Field(T("背景颜色", "Background color"), color), photo], () =>
        {
            if (!Color.TryParse(Text(color), out _)) throw new FormatException(T("请输入颜色，例如 #E8EEEB。", "Enter a color such as #E8EEEB."));
            SaveWidget(key, prefs with { Topmost = topmost.IsChecked == true, Transparency = opacity.Value,
                Width = Number(width, 260, 1600), Height = Number(height, 240, 1200), View = views[view.SelectedIndex], BackgroundColor = Text(color),
                BackgroundImage = background, BackgroundMode = background is null ? "纯色" : "图片" });
        })) { CloseWidget(key); RefreshWidgets(true); }
    }
    private string WidgetViewName(string view) => !_english ? view : view switch
    { "日" => "Day", "周" => "Week", "月" => "Month", "数字" => "Numbers", "热力图" => "Heatmap", "四象限" => "Quadrants", _ => "List" };
    private void CloseWidget(string key)
    {
        if (!_widgets.Remove(key, out var window)) return;
        _closingWidgets = true;
        try { window.Close(); } finally { _closingWidgets = false; }
    }
    private void CloseWidgets()
    {
        foreach (var kind in _widgets.Keys.ToArray()) CloseWidget(kind);
    }
    private void RefreshWidgets(bool force = false)
    {
        var now = DateTimeOffset.Now;
        if (!force && now - _lastWidgetUpdate < TimeSpan.FromSeconds(2)) return;
        _lastWidgetUpdate = now;
        var version = _widgetKeys.Any(key => ReadWidget(key).Enabled) ? WidgetVersion() : "";
        foreach (var kind in _widgetKeys)
        {
            var prefs = ReadWidget(kind);
            if (!prefs.Enabled) { CloseWidget(kind); continue; }
            if (!_widgets.TryGetValue(kind, out var window))
            {
                window = new WidgetWindow { Title = WidgetName(kind), Width = prefs.Width, Height = prefs.Height, MinWidth = 260, MinHeight = 240,
                    Topmost = prefs.Topmost, Opacity = 1 - prefs.Transparency / 100, ShowInTaskbar = false, WindowDecorations = Avalonia.Controls.WindowDecorations.BorderOnly };
                if (prefs.Left is { } left && prefs.Top is { } top)
                {
                    var point = new PixelPoint((int)left, (int)top);
                    if (window.Screens.All.Any(screen => screen.WorkingArea.Contains(point))) window.Position = point;
                }
                var content = new DockPanel();
                var header = Row(Label(WidgetName(kind), 15, true), Button("×", () => { SaveWidget(kind, ReadWidget(kind) with { Enabled = false }); RefreshWidgets(true); }));
                header.Margin = new Thickness(12, 10); header.PointerPressed += (_, args) =>
                { if (args.GetCurrentPoint(header).Properties.IsLeftButtonPressed && args.Source is TextBlock) window.BeginMoveDrag(args); };
                DockPanel.SetDock(header, Dock.Top); content.Children.Add(header); content.Children.Add(new ScrollViewer { Content = window.Body });
                var backdrop = new Border { Background = new SolidColorBrush(Color.TryParse(prefs.BackgroundColor, out var selected) ? selected : Color.Parse("#E8EEEB")), Child = content };
                if (prefs.BackgroundImage is { } path && ReadBitmap(path, false) is { } bitmap)
                { window.BackgroundBitmap = bitmap; backdrop.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }; }
                window.Content = backdrop;
                var opened = window;
                window.Closed += (_, _) =>
                {
                    opened.BackgroundBitmap?.Dispose();
                    SaveWidget(kind, ReadWidget(kind) with { Width = opened.Width, Height = opened.Height, Left = opened.Position.X, Top = opened.Position.Y });
                    if (!_closingWidgets) { _widgets.Remove(kind); SaveWidget(kind, ReadWidget(kind) with { Enabled = false }); }
                };
                _widgets.Add(kind, window); window.Show(); force = true;
            }
            if (force || kind == "Timer" || window.Version != version)
            {
                window.Body.Children.Clear(); RenderWidget(kind, window, prefs); window.Version = version;
            }
        }
    }
    private string WidgetVersion() => JsonSerializer.Serialize(new
    {
        Todos = _store.GetTodos(), Dates = _store.GetDateEvents(), Checks = _store.GetHabitCheckIns(), Habits = _store.GetHabits(),
        Mood = _store.GetDailyReflections(), Plans = _store.GetCalendarPlans(), Records = _store.GetRecords().Count, Day = Today, English = _english
    });
    private void RenderWidget(string kind, WidgetWindow window, WidgetPreferences prefs)
    {
        var body = window.Body;
        if (kind == "Timer")
        {
            body.Children.Add(Label(_focusTitle.Length == 0 ? T("准备开始", "Ready") : _focusTitle, 18, true));
            body.Children.Add(Label(Clock(_timer.DisplayTime), 36, true));
            body.Children.Add(Row(Button(T("打开", "Open"), () => { Show(); Activate(); Navigate(0); }),
                Button(T("暂停／继续", "Pause / Resume"), () => { if (_timer.State == FocusBloom.Core.Focus.FocusState.Paused) _timer.Resume(); else _timer.Pause(); UpdateTimer(true); })));
            return;
        }
        if (kind is "Habits" or "Calendar" or "Mood")
        {
            body.Children.Add(Row(Button("‹", () => { window.Day = prefs.View == "月" ? window.Day.AddMonths(-1) : prefs.View == "周" ? window.Day.AddDays(-7) : window.Day.AddDays(-1); RefreshWidgets(true); }),
                Label(window.Day.ToString("yyyy-MM-dd")), Button("›", () => { if (kind != "Calendar" && window.Day >= Today) return;
                    window.Day = prefs.View == "月" ? window.Day.AddMonths(1) : prefs.View == "周" ? window.Day.AddDays(7) : window.Day.AddDays(1); if (kind != "Calendar" && window.Day > Today) window.Day = Today; RefreshWidgets(true); })));
        }
        switch (kind)
        {
            case "Focus":
                var from = prefs.Period == "周" ? CalendarRules.Monday(Today) : prefs.Period == "月" ? Month(Today) : Today;
                var stats = FocusStatistics.Build(_store.GetRecords(), new(from, Today));
                body.Children.Add(Label(Clock(stats.Duration), 30, true));
                if (prefs.View == "热力图") foreach (var day in stats.Days) body.Children.Add(Label($"{day.Date:MM-dd}   {day.Duration.TotalMinutes:0} min"));
                break;
            case "Todos":
                foreach (var todo in _store.GetTodos().OrderBy(item => item.Status == TodoStatus.Completed))
                {
                    if (prefs.View == "四象限") body.Children.Add(Label(T((todo.Important ? "重要" : "不重要") + " · " + (todo.Urgent ? "紧急" : "不紧急"), (todo.Important ? "Important" : "Not important") + " · " + (todo.Urgent ? "Urgent" : "Not urgent")), 11));
                    var check = new CheckBox { Content = todo.Title, IsChecked = todo.Status == TodoStatus.Completed };
                    check.IsCheckedChanged += (_, _) => { _store.SaveTodo(todo with { Status = check.IsChecked == true ? TodoStatus.Completed : TodoStatus.Pending, CompletedAt = check.IsChecked == true ? DateTimeOffset.Now : null }); RefreshWidgets(true); };
                    body.Children.Add(check);
                }
                break;
            case "Dates":
                foreach (var item in _store.GetDateEvents().OrderBy(item => CalendarRules.NextDate(item, Today)))
                {
                    var date = CalendarRules.NextDate(item, Today) ?? item.Date; var delta = date.DayNumber - Today.DayNumber;
                    body.Children.Add(Label(item.Title, 15, true));
                    body.Children.Add(Label(delta >= 0 ? T($"还有 {delta} 天", $"{delta} days left") : T($"已过去 {-delta} 天", $"{-delta} days ago")));
                }
                break;
            case "Habits":
                var start = prefs.View == "月" ? Month(window.Day) : prefs.View == "周" ? CalendarRules.Monday(window.Day) : window.Day;
                var finish = prefs.View == "月" ? MonthEnd(window.Day) : prefs.View == "周" ? start.AddDays(6) : start;
                var checks = _store.GetHabitCheckIns();
                foreach (var habit in _store.GetHabits())
                {
                    body.Children.Add(Label(habit.Name, 15, true));
                    var row = new WrapPanel { ItemSpacing = 5, LineSpacing = 5 };
                    for (var day = start; day <= finish; day = day.AddDays(1))
                    {
                        var date = day;
                        var check = new CheckBox { Content = date.Day.ToString(), IsEnabled = date <= Today && GoalRules.Due(habit, date),
                            IsChecked = checks.Any(item => item.HabitId == habit.Id && item.Date == date) };
                        check.IsCheckedChanged += (_, _) => { _store.SetHabitCheckIn(habit.Id, date, check.IsChecked == true); RefreshWidgets(true); };
                        row.Children.Add(check);
                    }
                    body.Children.Add(row);
                }
                break;
            case "Mood":
                var entries = _store.GetDailyReflections().Where(item => prefs.View == "日" ? item.Date == window.Day : Month(item.Date) == Month(window.Day));
                foreach (var item in entries) body.Children.Add(Label($"{item.Date:MM-dd} · {(item.Mood is { } mood ? MoodName(mood) : "")}\n{item.Feeling}"));
                body.Children.Add(Button(T("打开记录", "Open entry"), () => { _day = window.Day; _moodMonth = prefs.View == "月"; Show(); Activate(); Navigate(6); }));
                break;
            case "Calendar":
                var first = prefs.View == "月" ? Month(window.Day) : prefs.View == "周" ? CalendarRules.Monday(window.Day) : window.Day;
                var last = prefs.View == "月" ? MonthEnd(window.Day) : prefs.View == "周" ? first.AddDays(6) : first;
                foreach (var entry in Entries(first, last)) body.Children.Add(Label($"{entry.Date:MM-dd} {entry.Start:HH:mm}\n{entry.Title}"));
                break;
        }
    }
}
