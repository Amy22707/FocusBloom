using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Todos;
using FocusBloom.Core.Focus;
using FocusBloom.Desktop.ViewModels;

using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public partial class TodoStartWindow : Window
{
    private sealed record CategoryChoice(string? Value, string Name);
    private sealed record ProjectChoice(FocusProject? Project, string Name);
    private readonly MainViewModel _main;
    private readonly Func<FocusProject?, string?, bool> _start;
    private sealed class CycleDraft(MainViewModel main)
    {
        public string PomodoroFocusMinutes { get; set; } = main.PomodoroFocusMinutes;
        public string PomodoroShortRestMinutes { get; set; } = main.PomodoroShortRestMinutes;
        public string PomodoroLongRestMinutes { get; set; } = main.PomodoroLongRestMinutes;
        public string PomodoroLongRestEvery { get; set; } = main.PomodoroLongRestEvery;
        public string PomodoroRounds { get; set; } = main.PomodoroRounds;
        public void Apply(MainViewModel target)
        {
            target.PomodoroFocusMinutes = PomodoroFocusMinutes; target.PomodoroShortRestMinutes = PomodoroShortRestMinutes;
            target.PomodoroLongRestMinutes = PomodoroLongRestMinutes; target.PomodoroLongRestEvery = PomodoroLongRestEvery; target.PomodoroRounds = PomodoroRounds;
        }
    }
    private readonly CycleDraft _cycle;
    public TodoStartWindow(MainViewModel main, TodoItem todo, int? minutes = null)
        : this(main, todo.Title, todo.PreferredProjectId, minutes, (project, category) => main.StartFromTodo(todo.Id, project, category: category))
    {
    }

    public TodoStartWindow(MainViewModel main, ChecklistEntry item)
        : this(main, item.Text, null, null, (project, category) => main.StartFromPlanner(item, project, category))
    {
        Title = UiText.Get("从计划本开始专注");
        ProjectHint.Text = UiText.Get("专注记录归入所选项目，结束后可回计划本勾选这项计划。");
    }

    private TodoStartWindow(MainViewModel main, string title, Guid? preferredProjectId, int? minutes, Func<FocusProject?, string?, bool> start)
    {
        _main = main; _start = start; _cycle = new(main);
        InitializeComponent(); WindowAppearance.Attach(this);
        TodoHeading.Text = title; StartHint.Text = UiText.Get("设置本次计时，选择要归入的项目。");
        CycleSettings.DataContext = _cycle;
        DurationInput.Text = minutes?.ToString(CultureInfo.CurrentCulture) ?? main.DurationMinutes;
        RestrictionSelector.ItemsSource = main.RestrictionModes; RestrictionSelector.SelectedItem = main.SelectedRestrictionMode;
        TimerSelector.ItemsSource = main.TimerModes; TimerSelector.SelectedItem = minutes is null ? main.SelectedTimerMode : main.TimerModes.Single(choice => choice.Mode == TimerMode.Countdown);
        CategorySelector.ItemsSource = new[] { new CategoryChoice(null, "不选择 · 未分类") }.Concat(main.Folders.Select(folder => new CategoryChoice(folder.Name, folder.Name))).ToArray();
        var preferred = main.Projects.FirstOrDefault(project => project.Id == preferredProjectId);
        CategorySelector.SelectedItem = CategorySelector.Items.Cast<CategoryChoice>().FirstOrDefault(choice => choice.Value == preferred?.Category) ?? CategorySelector.Items[0];
        if (preferred is not null) ProjectSelector.SelectedItem = ProjectSelector.Items.Cast<ProjectChoice>().FirstOrDefault(choice => choice.Project?.Id == preferred.Id);
    }
    private void Timer_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CountdownArea is null || CycleSettings is null) return;
        var mode = (TimerSelector.SelectedItem as TimerChoice)?.Mode;
        CountdownArea.Visibility = mode == TimerMode.Countdown ? Visibility.Visible : Visibility.Collapsed;
        CycleSettings.Visibility = mode == TimerMode.Pomodoro ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectSelector is null) return;
        var category = (CategorySelector.SelectedItem as CategoryChoice)?.Value;
        ProjectSelector.ItemsSource = new[] { new ProjectChoice(null, "不选择 · 所选文件夹的未分类项目") }.Concat(_main.Projects.Where(project => project.Category == category).Select(project => new ProjectChoice(project, project.Name))).ToArray();
        ProjectSelector.SelectedIndex = 0;
    }
    private void Start_Click(object sender, RoutedEventArgs e)
    {
        var timer = (TimerChoice)TimerSelector.SelectedItem;
        var originalTimer = _main.SelectedTimerMode; var originalRestriction = _main.SelectedRestrictionMode;
        var originalDuration = _main.DurationMinutes; var originalCycle = new CycleDraft(_main);
        _main.SelectedTimerMode = timer; _main.SelectedRestrictionMode = (RestrictionChoice)RestrictionSelector.SelectedItem;
        _main.DurationMinutes = DurationInput.Text;
        if (timer.Mode == TimerMode.Pomodoro) _cycle.Apply(_main);
        if (_start((ProjectSelector.SelectedItem as ProjectChoice)?.Project, (CategorySelector.SelectedItem as CategoryChoice)?.Value)) DialogResult = true;
        else
        {
            _main.SelectedTimerMode = originalTimer; _main.SelectedRestrictionMode = originalRestriction;
            _main.DurationMinutes = originalDuration; originalCycle.Apply(_main);
            ValidationMessage.Text = _main.StatusMessage;
        }
    }
}
