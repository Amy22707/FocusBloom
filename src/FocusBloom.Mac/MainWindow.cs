using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using FocusBloom.Core.Focus;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _data;
    private readonly SqliteFocusStore _store;
    private readonly StackPanel _nav = new() { Spacing = 4, Margin = new Thickness(12, 0) };
    private readonly StackPanel _page = new() { Spacing = 14, Margin = new Thickness(34, 26) };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(34, 4, 34, 12) };
    private readonly ScrollViewer _scroll;
    private bool _english;
    private int _section;
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    private string T(string zh, string en) => _english ? en : zh;

    public MainWindow(string data)
    {
        var smoke = Environment.GetCommandLineArgs().Contains("--smoke-test");
        if (smoke) Console.WriteLine("SMOKE: database");
        _data = data;
        _store = new SqliteFocusStore(Path.Combine(data, "focusbloom.db"));
        _english = _store.GetSetting("appearance.language") == "en";
        Title = "一刻专注 · FocusBloom";
        Width = 1100; Height = 760; MinWidth = 740; MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        _scroll = new ScrollViewer { Content = _page };
        var side = new DockPanel();
        var brand = Label("FocusBloom", 20, true);
        brand.Margin = new Thickness(20, 22, 12, 20);
        DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        side.Children.Add(new ScrollViewer { Content = _nav });
        var main = new DockPanel();
        DockPanel.SetDock(_notice, Dock.Bottom); main.Children.Add(_notice); main.Children.Add(_scroll);
        var width = double.TryParse(_store.GetSetting("mac.navigationWidth"), NumberStyles.Float, CultureInfo.InvariantCulture, out var savedWidth) ? Math.Clamp(savedWidth, 140, 360) : 176;
        var shell = new Grid { ColumnDefinitions = new ColumnDefinitions("176,5,*") };
        shell.ColumnDefinitions[0].Width = new GridLength(width);
        shell.ColumnDefinitions[0].MinWidth = 140;
        shell.ColumnDefinitions[0].MaxWidth = 360;
        var divider = new GridSplitter { Width = 5, HorizontalAlignment = HorizontalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
        divider.DragCompleted += (_, _) => _store.SaveSetting("mac.navigationWidth", shell.ColumnDefinitions[0].ActualWidth.ToString(CultureInfo.InvariantCulture));
        Grid.SetColumn(divider, 1); shell.Children.Add(divider);
        shell.Children.Add(side); Grid.SetColumn(main, 2); shell.Children.Add(main); Content = shell;
        if (smoke) Console.WriteLine("SMOKE: appearance");
        ApplyAppearance();
        ApplyFont();
        if (smoke) Console.WriteLine("SMOKE: initialize timer");
        InitializeTimer();
        if (smoke) Console.WriteLine("SMOKE: focus page");
        Navigate(0);
    }

    private bool _quitting;
    internal bool PrepareToQuit()
    {
        if (_backupBusy) { Show(); Report(T("请等待备份完成。", "Wait for the backup to finish.")); return false; }
        try
        {
            if (_timer.IsActive) { _timer.End(FocusBloom.Core.Focus.EndReason.ApplicationExit, false); UpdateTimer(true); }
            else Checkpoint(true);
            _quitting = true;
            return true;
        }
        catch (Exception error) { Show(); Report(error.Message); return false; }
    }

    private void Navigate(int page)
    {
        _section = page;
        _notice.Text = "";
        _scroll.Offset = default;
        Refresh();
    }

    private void Refresh()
    {
        Title = T("一刻专注 · FocusBloom", "FocusBloom");
        string[] titles = [T("专注计时", "Focus Timer"), T("项目库", "Projects"), T("待办事项", "Tasks"), T("计划本", "Planner"),
            T("日历与课表", "Calendar & Classes"), T("目标与习惯", "Goals & Habits"), T("心情与复盘", "Mood & Reflection"),
            T("专注记录", "Focus History"), T("偏好设置", "Settings"), T("学习浏览器", "Study Browser")];
        _nav.Children.Clear();
        for (var i = 0; i < titles.Length; i++)
        {
            var index = i;
            var button = Button(titles[i], () => Navigate(index), i == _section);
            button.HorizontalAlignment = HorizontalAlignment.Stretch;
            _nav.Children.Add(button);
        }
        _page.Children.Clear();
        foreach (var bitmap in _bitmaps) bitmap.Dispose();
        _bitmaps.Clear();
        _page.Children.Add(Label(titles[_section], 28, true));
        switch (_section)
        {
            case 0: FocusPage(); break;
            case 1: ProjectsPage(); break;
            case 2: TasksPage(); break;
            case 3: PlannerPage(); break;
            case 4: CalendarPage(); break;
            case 5: HabitsPage(); break;
            case 6: ReflectionPage(); break;
            case 7: HistoryPage(); break;
            case 8: SettingsPage(); break;
            case 9: BrowserPage(); break;
        }
    }

    private TextBlock Label(string text, double size = 14, bool strong = false) => new SelectableTextBlock()
    {
        Text = text, FontSize = size * InterfaceScale, TextWrapping = TextWrapping.Wrap, FontWeight = strong ? FontWeight.SemiBold : FontWeight.Normal
    };
    private Button Button(string label, Action action, bool primary = false) => AsyncButton(label, () => { action(); return Task.CompletedTask; }, primary);
    private Button AsyncButton(string label, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = label, Padding = new Thickness(13, 8), CornerRadius = new CornerRadius(10) };
        if (primary) button.Classes.Add("accent");
        button.Click += async (_, _) =>
        {
            try { await action(); }
            catch (Exception error) { Report(error.Message); }
        };
        return button;
    }
    private static WrapPanel Row(params Control[] controls)
    {
        var panel = new WrapPanel { ItemSpacing = 8, LineSpacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        return panel;
    }
    private static Border Card(params Control[] controls)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var control in controls) panel.Children.Add(control);
        var card = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16), BorderThickness = new Thickness(1), Child = panel };
        card.Bind(Border.BackgroundProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SystemControlBackgroundChromeMediumLowBrush"));
        card.Bind(Border.BorderBrushProperty, new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SystemControlForegroundBaseLowBrush"));
        return card;
    }
    private static TextBox Input(string value = "", bool multiline = false) => new()
    {
        Text = value, MinWidth = 150, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
        MinHeight = multiline ? 75 : 34
    };
    private StackPanel Field(string label, Control input) => new() { Spacing = 4, Children = { Label(label), input } };
    private static ComboBox Choice(IEnumerable<string> items, int selected = 0) => new() { ItemsSource = items.ToArray(), SelectedIndex = selected, MinWidth = 140 };
    private static DatePicker DateInput(DateOnly? date) => new()
    {
        SelectedDate = date is { } value ? new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue)) : null, HorizontalAlignment = HorizontalAlignment.Stretch
    };
    private static DateOnly? DateValue(DatePicker picker) => picker.SelectedDate is { } date ? DateOnly.FromDateTime(date.DateTime) : null;
    private static string Text(TextBox input) => input.Text?.Trim() ?? "";
    private string Required(TextBox input) => Text(input) is { Length: > 0 } text ? text : throw new FormatException(T("请输入名称。", "Enter a name."));
    private int Number(TextBox input, int min, int max)
    {
        if (int.TryParse(Text(input), NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) && value >= min && value <= max) return value;
        throw new FormatException(T($"请输入 {min}–{max} 之间的整数。", $"Enter a whole number between {min} and {max}."));
    }
    private void Report(string text) => _notice.Text = text;
    private void Empty() => _page.Children.Add(Label(T("暂无内容", "Nothing here yet")));
    private static string Clock(TimeSpan time) => $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    private async Task<bool> Form(string title, IReadOnlyList<Control> fields, Func<Task> save, string? confirm = null)
    {
        var error = Label("");
        var window = new Window { Title = title, Width = 480, MaxHeight = 650, SizeToContent = SizeToContent.Height,
            CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var body = new StackPanel { Spacing = 12, Margin = new Thickness(22) };
        foreach (var field in fields) body.Children.Add(field);
        var deleting = confirm == T("删除", "Delete");
        var submit = new Button { Content = confirm ?? T("保存", "Save"), Classes = { "accent" }, IsDefault = !deleting };
        var cancel = new Button { Content = T("取消", "Cancel"), IsCancel = true, IsDefault = deleting };
        cancel.Click += (_, _) => window.Close(false);
        submit.Click += async (_, _) =>
        {
            submit.IsEnabled = cancel.IsEnabled = false;
            try { await save(); window.Close(true); }
            catch (Exception failure) { error.Text = failure.Message; submit.IsEnabled = cancel.IsEnabled = true; }
        };
        body.Children.Add(error); body.Children.Add(Row(submit, cancel));
        window.Content = new ScrollViewer { Content = body };
        return await window.ShowDialog<bool>(this);
    }
    private Task<bool> Form(string title, IReadOnlyList<Control> fields, Action save, string? confirm = null) =>
        Form(title, fields, () => { save(); return Task.CompletedTask; }, confirm);
    private async Task Delete(string name, Action delete)
    {
        if (await Form(T("确认删除", "Confirm deletion"), [Label(T($"删除「{name}」？", $"Delete “{name}”?"))], delete, T("删除", "Delete"))) Refresh();
    }
    private Control DayNavigation(bool future = true)
    {
        var date = DateInput(_day);
        if (!future) date.MaxYear = new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue));
        date.SelectedDateChanged += (_, _) => { if (DateValue(date) is { } selected && (future || selected <= Today)) { _day = selected; Refresh(); } };
        var next = Button("›", () => { _day = _day.AddDays(1); Refresh(); });
        next.IsEnabled = future || _day < Today;
        return Row(Button("‹", () => { _day = _day.AddDays(-1); Refresh(); }), date, next,
            Button(T("今天", "Today"), () => { _day = Today; Refresh(); }));
    }
    private async Task<string?> OpenFile(string title, params string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title,
            FileTypeFilter = [new FilePickerFileType(title) { Patterns = patterns }] });
        return files.FirstOrDefault()?.TryGetLocalPath();
    }
    private async Task<string?> SaveFile(string title, string name, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = name, DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = ["*." + extension] }] });
        return file?.TryGetLocalPath();
    }
    private void ApplyAppearance()
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = _store.GetSetting("appearance.mode") switch
        {
            "Light" => ThemeVariant.Light, "Dark" => ThemeVariant.Dark, _ => ThemeVariant.Default
        };
        var color = Color.Parse(_store.GetSetting("mac.accent") ?? _colors[0]);
        var theme = app.Styles.OfType<FluentTheme>().Single();
        theme.Palettes[ThemeVariant.Light] = new ColorPaletteResources { Accent = color };
        theme.Palettes[ThemeVariant.Dark] = new ColorPaletteResources { Accent = color };
    }
}
