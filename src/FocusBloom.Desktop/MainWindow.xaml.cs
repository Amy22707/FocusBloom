using System;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Core.Storage;
using FocusBloom.Core.Restrictions;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty NavigationCollapsedProperty = DependencyProperty.Register(nameof(NavigationCollapsed), typeof(bool), typeof(MainWindow), new PropertyMetadata(false, (target, args) =>
    {
        var window = (MainWindow)target;
        window._store?.SaveSetting("ui.navigation.collapsed", args.NewValue.ToString()!);
    }));
    public bool NavigationCollapsed { get => (bool)GetValue(NavigationCollapsedProperty); set => SetValue(NavigationCollapsedProperty, value); }
    public static readonly DependencyProperty ContentFullscreenProperty = DependencyProperty.Register(nameof(ContentFullscreen), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool ContentFullscreen { get => (bool)GetValue(ContentFullscreenProperty); private set => SetValue(ContentFullscreenProperty, value); }
    private bool _fullscreen;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResizeMode;
    private Rect _previousBounds;
    private bool _previousTopmost;
    private double _previousMinWidth;
    private double _previousMinHeight;
    private WindowState _lastWindowState = WindowState.Normal;
    private bool _layoutInitialized;
    private bool _browserFullscreen;
    private readonly MainViewModel _viewModel;
    private readonly IFocusStore _store;
    private TimerFloatWindow? _timerFloat;
    private readonly StudyBrowserView _studyBrowser;
    private double _preferencesScrollOffset;
    private bool _restoringPreferencesScroll;
    private readonly Dictionary<string, TabItem> _navigationTabs = new();
    private readonly List<(Expander Section, string Key, string Keywords)> _settingsSections = new();
    private bool _filteringSettings;

    public MainWindow(MainViewModel viewModel, IFocusStore store, string? browserUserDataFolder = null)
    {
        InitializeComponent();
        WindowAppearance.Attach(this);
        _viewModel = viewModel;
        _store = store;
        foreach (var (key, tab) in new[] { ("focus", FocusTab), ("projects", ProjectsTab), ("todos", TodoTab), ("calendar", CalendarTab), ("dates", DateEventsTab), ("records", RecordsTab), ("settings", PreferencesTab), ("goals", GoalsTab) }) _navigationTabs.Add(key, tab);
        viewModel.Navigation.NavigateRequested += NavigateToSection;
        NavigationCollapsed = store.GetSetting("ui.navigation.collapsed") == "True";
        _studyBrowser = browserUserDataFolder is null ? new StudyBrowserView() : new StudyBrowserView(browserUserDataFolder);
        BrowserTab.Content = _studyBrowser;
        _studyBrowser.ReturnToFocusRequested += BrowserReturnToFocusRequested;
        _studyBrowser.FullscreenRequested += BrowserFullscreenRequested;
        DataContext = viewModel;
        RememberToggle(ProjectSidebarToggle, "ui.projects.sidebarCollapsed");
        RememberToggle((System.Windows.Controls.Primitives.ToggleButton)TodoPage.FindName("TodoSidebarToggle"), "ui.todos.sidebarCollapsed");
        var mediaSettings = (FocusMediaSettingsView)FocusMediaSettingsCard.Child;
        RememberExpansion((Expander)mediaSettings.FindName("BackgroundLibrary"), "ui.media.backgroundExpanded");
        RememberExpansion((Expander)mediaSettings.FindName("QuoteLibrary"), "ui.media.quoteExpanded");
        RememberExpansion((Expander)mediaSettings.FindName("SoundLibrary"), "ui.media.soundExpanded");
        foreach (var (section, key, words) in new[] {
            (NavigationSettingsSection, "navigation", "导航 显示 展示 顺序 排序 功能"), (AppearanceSettingsSection, "appearance", "外观 颜色 配色 明暗 深色 浅色"),
            (SizeSettingsSection, "size", "大小 缩放 卡片 预览"), (TimerSettingsSection, "timer", "计时 正计时 倒计时 番茄 循环 暂停 休息"),
            (FloatSettingsSection, "float", "浮窗 置顶 透明"), (FontSettingsSection, "fonts", "字体 中文 英文 数字"),
            (MediaSettingsSection, "media", "专注 图文 背景 图片 视频 GIF 声音 音乐 句子 文字 收藏"), (ReminderSettingsSection, "reminders", "日历 提醒 Outlook 同步"),
            (RestrictionSettingsSection, "restrictions", "严格 应用 网站 浏览器 黑名单 白名单 限制") })
        { var settingKey = "ui.settings.expanded." + key; _settingsSections.Add((section, settingKey, section.Header + " " + words)); RememberExpansion(section, settingKey); }
        var settingsPanel = (Panel)NavigationSettingsSection.Parent;
        for (var index = 0; index < _settingsSections.Count; index++) { var section = _settingsSections[index].Section; settingsPanel.Children.Remove(section); settingsPanel.Children.Insert(index, section); }
        NavigateToSection(viewModel.Navigation.Items.Single(item => item.Key == "focus").Visible ? "focus" : viewModel.Navigation.VisibleItems[0].Key);
        CalendarPage.EntryRequested += OpenCalendarEntry;
        FocusCalendarPage.EntryRequested += OpenCalendarEntry;
        viewModel.PropertyChanged += OnFocusStateChanged;
        viewModel.FocusDurationRejected += OnFocusDurationRejected;
        Closed += (_, _) => { viewModel.Navigation.NavigateRequested -= NavigateToSection; viewModel.PropertyChanged -= OnFocusStateChanged; viewModel.FocusDurationRejected -= OnFocusDurationRejected; _timerFloat?.CloseForExit(); _studyBrowser.Dispose(); };
        var savedJson = store.GetSetting(WindowLayout.SettingKey);
        var savedLayout = savedJson is null ? null : JsonSerializer.Deserialize<WindowLayout>(savedJson);
        SourceInitialized += (_, _) => { RestoreWindowLayout(savedLayout); UpdateTimerFloatVisibility(); };
        Closing += (_, _) =>
        {
            if (!_layoutInitialized) return;
            try { store.SaveSetting(WindowLayout.SettingKey, JsonSerializer.Serialize(CaptureWindowLayout())); }
            catch (Exception ex) { viewModel.ReportStatus($"窗口布局保存失败：{ex.Message}"); }
        };
        StateChanged += (_, _) =>
        {
            if (!_fullscreen && WindowState != WindowState.Minimized) _lastWindowState = WindowState;
            UpdateTimerFloatVisibility();
        };
        IsVisibleChanged += (_, _) => UpdateTimerFloatVisibility();
        Loaded += (_, _) => KeepNavigationVisible();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F11 || (e.Key == Key.Escape && _fullscreen))
            {
                if (e.Key == Key.F11 && !_fullscreen && MainTabs.SelectedItem == BrowserTab) BrowserFullscreenRequested(this, EventArgs.Empty);
                else ToggleFullscreen();
                e.Handled = true;
            }
        };
    }

    public event EventHandler? ExitRequested;
    private void NavigateToSection(string key) { MainTabs.SelectedItem = _navigationTabs[key]; _viewModel.Navigation.SetActive(key); KeepNavigationVisible(); }
    private void KeepNavigationVisible()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (MainTabs.Template.FindName("NavigationItems", MainTabs) is ItemsControl list && _viewModel.Navigation.VisibleItems.FirstOrDefault(item => item.Active) is { } active)
                (list.ItemContainerGenerator.ContainerFromItem(active) as FrameworkElement)?.BringIntoView();
        }));
    }
    private void NavigationOrder_Requested(object sender, RoutedEventArgs args)
    { var move = (ReorderEventArgs)args; _viewModel.Navigation.Move((NavigationOption)((ReorderButton)sender).DataContext, move.Direction, move.Target as NavigationOption); args.Handled = true; }
    private void SettingsSearch_Changed(object sender, TextChangedEventArgs args)
    {
        if (_settingsSections.Count == 0) return;
        var words = SettingsSearchInput.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _filteringSettings = true;
        try
        {
            foreach (var (section, key, keywords) in _settingsSections)
            { var matches = words.All(word => keywords.Contains(word, StringComparison.OrdinalIgnoreCase)); section.Visibility = matches ? Visibility.Visible : Visibility.Collapsed; section.IsExpanded = words.Length > 0 ? matches : _store.GetSetting(key) == "True"; }
            SettingsSearchEmpty.Visibility = _settingsSections.Any(item => item.Section.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { _filteringSettings = false; }
    }
    private void RecordPage_Click(object sender, RoutedEventArgs args) => _viewModel.RecordPage = ((Button)sender).Tag.ToString()!;
    private void RememberToggle(System.Windows.Controls.Primitives.ToggleButton toggle, string key)
    {
        toggle.IsChecked = _store.GetSetting(key) == "True";
        toggle.Checked += (_, _) => _store.SaveSetting(key, "True");
        toggle.Unchecked += (_, _) => _store.SaveSetting(key, "False");
    }
    private void RememberExpansion(Expander expander, string key)
    {
        expander.IsExpanded = _store.GetSetting(key) == "True";
        expander.Expanded += (_, args) => { if (!_filteringSettings && ReferenceEquals(args.Source, expander)) _store.SaveSetting(key, "True"); };
        expander.Collapsed += (_, args) => { if (!_filteringSettings && ReferenceEquals(args.Source, expander)) _store.SaveSetting(key, "False"); };
    }
    private void PreferencesScroll_Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!_restoringPreferencesScroll && PreferencesScrollViewer.IsVisible && e.VerticalChange != 0 && e.ExtentHeightChange == 0 && e.ViewportHeightChange == 0)
            _preferencesScrollOffset = PreferencesScrollViewer.VerticalOffset;
    }
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, MainTabs)) return;
        if (_viewModel is null) return;
        _viewModel.Navigation.SetActive(_navigationTabs.FirstOrDefault(item => item.Value == MainTabs.SelectedItem).Key);
        KeepNavigationVisible();
        if (_fullscreen) UpdateFullscreenContent();
        if (MainTabs.SelectedItem != PreferencesTab) return;
        _restoringPreferencesScroll = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (MainTabs.SelectedItem == PreferencesTab) PreferencesScrollViewer.ScrollToVerticalOffset(_preferencesScrollOffset);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => _restoringPreferencesScroll = false));
        }));
    }
    private void LatestOutcome_Click(object sender, RoutedEventArgs e)
    { if (_viewModel.LastSavedRecordId is { } id) new OutcomeEditorWindow(_viewModel, id) { Owner = this }.ShowDialog(); }
    private void RecordOutcome_Click(object sender, RoutedEventArgs e) => new OutcomeEditorWindow(_viewModel, ((RecordRow)((Button)sender).DataContext).Id) { Owner = this }.ShowDialog();
    private void OnFocusDurationRejected(object? sender, EventArgs e) =>
        ConfirmDeletionWindow.ShowNotice(Application.Current.Windows.OfType<Window>().LastOrDefault(window => window.IsActive) ?? this, _viewModel.StatusMessage, "专注时长至少10秒");
    private void RemoveApplication_Click(object sender, RoutedEventArgs args)
    {
        if (_viewModel.Whitelist.SelectedApplication is { } app && ConfirmDeletionWindow.Ask(this, $"从应用列表移除‘{app.Name}’？")) _viewModel.Whitelist.RemoveApplicationCommand.Execute(null);
    }
    private void RemoveWebsite_Click(object sender, RoutedEventArgs args)
    {
        if (_viewModel.Whitelist.SelectedWebsite is { } website && ConfirmDeletionWindow.Ask(this, $"从网站列表删除‘{website.Domain}’？")) _viewModel.Whitelist.RemoveWebsiteCommand.Execute(null);
    }
    private void FolderOrder_Requested(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveFolder((ProjectFolder)((ReorderButton)sender).DataContext, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as ProjectFolder);
        e.Handled = true;
    }
    private void ProjectOrder_Requested(object sender, RoutedEventArgs e)
    {
        _viewModel.MoveProject((FocusBloom.Core.Projects.FocusProject)((ReorderButton)sender).DataContext, ((ReorderEventArgs)e).Direction, ((ReorderEventArgs)e).Target as FocusBloom.Core.Projects.FocusProject);
        e.Handled = true;
    }
    private void EditFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = (ProjectFolder)((Button)sender).DataContext;
        new ProjectEditorWindow(_viewModel, folder) { Owner = this }.ShowDialog();
    }
    private void NewFolder_Click(object sender, RoutedEventArgs e) => new ProjectEditorWindow(_viewModel, newFolder: true) { Owner = this }.ShowDialog();
    private void NewProject_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Folders.Count == 0) { ConfirmDeletionWindow.ShowNotice(this, "请先新建一个文件夹，再添加项目。", "先创建文件夹"); return; }
        new ProjectEditorWindow(_viewModel, newFolder: false) { Owner = this }.ShowDialog();
    }
    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        var project = (FocusBloom.Core.Projects.FocusProject)((Button)sender).DataContext;
        new ProjectEditorWindow(_viewModel, project) { Owner = this }.ShowDialog();
    }
    public void ReturnToFocus()
    {
        _browserFullscreen = false;
        RestoreBrowserContent();
        MainTabs.SelectedIndex = 0;
        UpdateFullscreenContent();
        ShowFromTray();
    }
    public void OpenReminder(CalendarEntry target)
    {
        if (_fullscreen) ToggleFullscreen();
        ShowFromTray();
        if (target.Kind == "习惯") { MainTabs.SelectedItem = GoalsTab; OpenCalendarEntry(target); return; }
        var focus = target.Kind == "专注" || target.Kind == "安排" && _viewModel.Calendar.Store.GetCalendarPlans().Any(plan => plan.Id == target.Id && plan.ProjectId is not null);
        MainTabs.SelectedItem = focus ? RecordsTab : CalendarTab;
        var model = focus ? _viewModel.FocusCalendar : _viewModel.Calendar;
        model.Filter = "全部"; model.Mode = target.Kind == "课程" ? "课程表" : focus ? "时间轴" : "日"; model.Date = target.Date;
        if (target.Kind == "汇总") { new ReminderInboxWindow(_viewModel, OpenCalendarEntry) { Owner = this }.ShowDialog(); return; }
        OpenCalendarEntry(target);
    }
    public void OpenCalendarEntry(CalendarEntry entry)
    {
        switch (entry.Kind)
        {
            case "待办":
                var todo = _viewModel.Todos.Items.FirstOrDefault(item => item.Id == entry.Id);
                if (todo is not null && new TodoDetailsWindow(_viewModel, todo) { Owner = this }.ShowDialog() == true) ReturnToFocus();
                break;
            case "安排":
                var plan = _store.GetCalendarPlans().FirstOrDefault(item => item.Id == entry.Id);
                if (plan is not null) { var editor = new CalendarPlanWindow(_viewModel, entry.Date, plan, focusOnly: plan.ProjectId is not null) { Owner = this }; editor.ShowDialog(); if (editor.FocusRequested) ReturnToFocus(); }
                break;
            case "习惯":
                var habit = _store.GetHabits(true).FirstOrDefault(item => item.Id == entry.Id);
                if (habit is not null) new HabitHistoryWindow(_viewModel, habit) { Owner = this }.ShowDialog();
                break;
            case "课程":
                var course = _store.GetCourses().FirstOrDefault(item => item.Id == entry.Id);
                if (course is not null) { var editor = new CourseEditorWindow(_viewModel, course, entry) { Owner = this }; editor.ShowDialog(); if (editor.FocusRequested) ReturnToFocus(); }
                break;
            case "日期事项":
                var date = _store.GetDateEvents().FirstOrDefault(item => item.Id == entry.Id);
                if (date is not null) { var editor = new DateEventEditorWindow(_viewModel, date) { Owner = this }; editor.ShowDialog(); if (editor.FocusRequested) ReturnToFocus(); }
                break;
            case "专注": new OutcomeEditorWindow(_viewModel, entry.Id) { Owner = this }.ShowDialog(); break;
        }
    }
    public void DisposeBrowser() => _studyBrowser.Dispose();
    private void BrowserReturnToFocusRequested(object? sender, EventArgs e) => ReturnToFocus();
    private void OpenStudyBrowser_Click(object sender, RoutedEventArgs e) => OpenStudyBrowser();
    private void FullscreenOpenBrowserRequested(object? sender, EventArgs e) => OpenStudyBrowser();
    private void OpenStudyBrowser()
    {
        MainTabs.SelectedItem = BrowserTab;
        if (_fullscreen) { _browserFullscreen = true; UpdateFullscreenContent(); }
    }
    private void BrowserFullscreenRequested(object? sender, EventArgs e)
    {
        if (_fullscreen && _browserFullscreen) { ToggleFullscreen(); return; }
        _browserFullscreen = true;
        if (!_fullscreen) ToggleFullscreen();
        else UpdateFullscreenContent();
    }
    private void OnFocusStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_fullscreen && e.PropertyName == nameof(MainViewModel.IsFocusActive)) UpdateFullscreenContent();
        if (e.PropertyName == nameof(MainViewModel.TimerFloatVisible)) UpdateTimerFloatVisibility();
    }

    private void UpdateTimerFloatVisibility()
    {
        if (!_layoutInitialized) return;
        if (!_viewModel.TimerFloatVisible) { _timerFloat?.SaveLayout(); _timerFloat?.Hide(); return; }
        if (_timerFloat is null)
        {
            _timerFloat = new TimerFloatWindow(_viewModel, _store, ReturnToFocus);
            new WindowInteropHelper(_timerFloat).EnsureHandle();
        }
        var sameScreen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).DeviceName
            == System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(_timerFloat).Handle).DeviceName;
        if (_fullscreen && IsVisible && WindowState != WindowState.Minimized && sameScreen) _timerFloat.Hide();
        else if (!_timerFloat.IsVisible) { _timerFloat.FitToScreen(); _timerFloat.Show(); }
    }

    private void UpdateFullscreenContent()
    {
        var browser = _fullscreen && _browserFullscreen;
        if (browser && BrowserFullscreenHost.Content is null)
        {
            BrowserTab.Content = null;
            BrowserFullscreenHost.Content = _studyBrowser;
        }
        if (!browser) RestoreBrowserContent();
        var timer = _fullscreen && !browser && MainTabs.SelectedIndex == 0 && _viewModel.IsFocusActive;
        ContentFullscreen = _fullscreen && !browser && !timer;
        WorkspaceHeader.Visibility = ContentFullscreen ? Visibility.Collapsed : Visibility.Visible;
        StatusDisplay.Visibility = ContentFullscreen ? Visibility.Collapsed : Visibility.Visible;
        WorkspaceView.RowDefinitions[0].Height = new GridLength(ContentFullscreen ? 0 : 88);
        WorkspaceView.RowDefinitions[2].Height = new GridLength(ContentFullscreen ? 0 : 42);
        WorkspaceView.Visibility = timer || browser ? Visibility.Collapsed : Visibility.Visible;
        FullscreenView.Visibility = timer ? Visibility.Visible : Visibility.Collapsed;
        BrowserFullscreenView.Visibility = browser ? Visibility.Visible : Visibility.Collapsed;
        _studyBrowser.SetFullscreenPresentation(browser);
        EnterFullscreenButton.Content = _fullscreen ? "退出全屏  ↙" : "全屏  ↗";
        UpdateTimerFloatVisibility();
    }

    private void RestoreBrowserContent()
    {
        if (BrowserFullscreenHost.Content is null) return;
        BrowserFullscreenHost.Content = null;
        BrowserTab.Content = _studyBrowser;
    }
    private void SelectApplicationFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择允许操作的程序", Filter = "程序 (*.exe)|*.exe" };
        if (dialog.ShowDialog(this) != true) return;
        var name = FileVersionInfo.GetVersionInfo(dialog.FileName).FileDescription;
        ((MainViewModel)DataContext).Whitelist.AddApplication(new AllowedApplication(
            string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(dialog.FileName) : name, dialog.FileName));
    }
    private void SelectRunningApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow { Owner = this };
        if (picker.ShowDialog() == true) ((MainViewModel)DataContext).Whitelist.AddApplication(picker.SelectedApplication!);
    }

    public WindowLayout CaptureWindowLayout()
    {
        var bounds = _fullscreen ? _previousBounds
            : WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        var state = _fullscreen ? _previousState : _lastWindowState;
        return new WindowLayout(bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            state == WindowState.Maximized, _fullscreen ? _previousTopmost : Topmost);
    }

    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = _lastWindowState;
        Activate();
        ApplicationRestrictionService.ActivateForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    private void RestoreWindowLayout(WindowLayout? savedLayout)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = savedLayout is null
            ? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle)
            : System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
                (int)Math.Round(savedLayout.Left * dpi.DpiScaleX), (int)Math.Round(savedLayout.Top * dpi.DpiScaleY),
                (int)Math.Round(savedLayout.Width * dpi.DpiScaleX), (int)Math.Round(savedLayout.Height * dpi.DpiScaleY)));
        var area = screen.WorkingArea;
        var workArea = new Rect(area.Left / dpi.DpiScaleX, area.Top / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX, area.Height / dpi.DpiScaleY);
        var layout = savedLayout?.FitTo(workArea) ?? WindowLayout.CreateDefault(workArea);
        MinWidth = Math.Min(WindowLayout.MinimumWidth, workArea.Width - 32);
        MinHeight = Math.Min(WindowLayout.MinimumHeight, workArea.Height - 32);
        Left = layout.Left;
        Top = layout.Top;
        Width = layout.Width;
        Height = layout.Height;
        Topmost = layout.Topmost;
        WindowState = layout.Maximized ? WindowState.Maximized : WindowState.Normal;
        _layoutInitialized = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);
    private void ToggleFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();
    private void ReturnToWindowRequested(object? sender, EventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).EnsureHandle());
            var dpi = VisualTreeHelper.GetDpi(this);
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            _previousBounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _previousTopmost = Topmost;
            _previousMinWidth = MinWidth;
            _previousMinHeight = MinHeight;
            _fullscreen = true;
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            MinWidth = MinHeight = 0;
            Left = screen.Bounds.Left / dpi.DpiScaleX;
            Top = screen.Bounds.Top / dpi.DpiScaleY;
            Width = screen.Bounds.Width / dpi.DpiScaleX;
            Height = screen.Bounds.Height / dpi.DpiScaleY;
            Topmost = true;
            UpdateFullscreenContent();
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            Left = _previousBounds.Left;
            Top = _previousBounds.Top;
            Width = _previousBounds.Width;
            Height = _previousBounds.Height;
            MinWidth = _previousMinWidth;
            MinHeight = _previousMinHeight;
            Topmost = _previousTopmost;
            WindowState = _previousState;
            _fullscreen = false;
            _browserFullscreen = false;
            UpdateFullscreenContent();
            _lastWindowState = _previousState;
            WindowAppearance.Apply(this);
        }
    }
}
