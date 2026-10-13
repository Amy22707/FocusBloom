using System.IO;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Focus;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using FocusBloom.Core.Restrictions;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Views;
using Microsoft.Web.WebView2.Wpf;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class DesktopFlowTests
{
    [Fact]
    public void WindowBindingsOrganizeProjectsSwitchAppearanceAndRunBothTimerModes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "FocusBloomUiTests", Guid.NewGuid().ToString());
            MainWindow? window = null;
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/FocusBloom;component/Resources/Theme.xaml", UriKind.Relative)
                });
                var brandLogo = (BitmapSource)application.FindResource("BrandLogo");
                Assert.True(brandLogo.PixelWidth > 0);
                using (var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/FocusBloom;component/Resources/Branding/FocusBloom.ico"))!.Stream)
                using (var icon = new System.Drawing.Icon(iconStream)) Assert.True(icon.Width >= 16);
                var loading = new LoadingWindow();
                Assert.Same(brandLogo, ((Image)loading.FindName("LoadingLogo")).Source);
                var loadingContent = (FrameworkElement)loading.Content;
                loadingContent.Measure(new Size(460, 450));
                loadingContent.Arrange(new Rect(0, 0, 460, 450));
                loadingContent.UpdateLayout();
                Assert.Contains(VisibleTexts(loadingContent), text => text == "一刻专注");
                Assert.Contains(VisibleTexts(loadingContent), text => text == "把时间留给喜欢的事。");
                SavePreview(loadingContent, Brushes.Transparent, "loading-page.png", 460, 450);
                loading.Close();
                var store = new SqliteFocusStore(Path.Combine(directory, "test.db"));
                var clock = new ManualClock();
                var viewModel = new MainViewModel(store, new FocusTimer(clock));
                viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(m => m.Mode == AppearanceMode.Light);
                window = new MainWindow(viewModel, store, Path.Combine(directory, "main-browser"));
                Assert.Same(brandLogo, window.Icon);
                Assert.Same(brandLogo, ((Image)window.FindName("HeaderLogo")).Source);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = 120;
                window.Top = 90;
                Assert.Equal("fog-blue", viewModel.SelectedPalette.Id);
                Assert.Equal(Color.FromRgb(246, 247, 248), ((SolidColorBrush)window.Background).Color);
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1240, 750));
                content.Arrange(new Rect(0, 0, 1240, 750));
                content.UpdateLayout();
                FlushBindings();

                var tabs = (TabControl)window.FindName("MainTabs");
                tabs.SelectedIndex = 1;
                content.UpdateLayout();
                FlushBindings();
                Assert.Null(window.FindName("FolderNameInput"));
                Assert.Null(window.FindName("ProjectNameInput"));
                window.Show(); FlushBindings();
                void CreateFromPopup(string buttonName, string name)
                {
                    Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                    {
                        var dialog = Application.Current.Windows.OfType<ProjectEditorWindow>().Single();
                        Assert.Equal(Visibility.Collapsed, ((Button)dialog.FindName("DeleteButton")).Visibility);
                        ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert.False(string.IsNullOrEmpty(((TextBlock)dialog.FindName("ValidationMessage")).Text));
                        ((TextBox)dialog.FindName("NameInput")).Text = name;
                        ((Button)dialog.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }));
                    ((Button)window.FindName(buttonName)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    FlushBindings();
                }
                CreateFromPopup("AddFolderButton", "学习");
                CreateFromPopup("AddProjectButton", "英语阅读");
                CreateFromPopup("AddProjectButton", "高等数学");
                CreateFromPopup("AddFolderButton", "生活");
                CreateFromPopup("AddProjectButton", "阅读");
                CreateFromPopup("AddFolderButton", "工作");
                Assert.Empty(viewModel.FolderProjects);
                var folders = (ListBox)window.FindName("FolderList");
                folders.SelectedItem = viewModel.Folders.Single(f => f.Name == "学习");
                FlushBindings();
                Assert.Equal(2, viewModel.FolderProjects.Count);
                Assert.Equal("英语阅读", viewModel.SelectedProject!.Name);
                content.UpdateLayout();
                SavePreview(content, window.Background, "projects-page.png");
                VerifyProjectCardLayout(window, viewModel, content);

                tabs.SelectedIndex = 0;
                content.UpdateLayout();
                FlushBindings();
                var mode = (ComboBox)window.FindName("TimerModeSelector");
                Assert.Equal(3, mode.Items.Count);
                mode.SelectedIndex = 1;
                FlushBindings();
                content.UpdateLayout();
                Assert.True(viewModel.Countdown);
                Assert.Contains(VisibleTexts(mode), t => t == "倒计时");
                Assert.Contains(VisibleTexts((ComboBox)window.FindName("FocusFolderSelector")), t => t == "学习");
                Assert.Contains(VisibleTexts((ComboBox)window.FindName("ProjectSelector")), t => t == "英语阅读");
                mode.SelectedIndex = 0;
                FlushBindings();
                Assert.False(viewModel.Countdown);

                var start = (Button)window.FindName("StartButton");
                Assert.True(start.IsEnabled);
                AssertVisibleInViewport(start, content);
                var enterFullscreen = (Button)window.FindName("EnterFullscreenButton");
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WorkspaceView")).Visibility);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("FullscreenView")).Visibility);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                viewModel.Media.SelectedBackground = viewModel.Media.Images.First(image => !image.IsVideo).Asset;
                start.Command.Execute(null);
                var sessionImage = viewModel.Media.Background;
                var sessionQuote = viewModel.Media.QuoteText;
                Assert.NotNull(sessionImage);
                clock.Advance(TimeSpan.FromMinutes(1));
                viewModel.Tick();
                FlushBindings();
                Assert.Equal("00:01:00", ((TextBlock)window.FindName("TimerDisplay")).Text);
                Assert.False(start.IsEnabled);
                SavePreview(content, window.Background, "focus-page.png");
                var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).EnsureHandle());
                var dpi = VisualTreeHelper.GetDpi(window);
                var windowBounds = new Rect(window.Left, window.Top, window.Width, window.Height);
                var windowStyle = window.WindowStyle;
                var resizeMode = window.ResizeMode;
                var minimumWidth = window.MinWidth;
                var minimumHeight = window.MinHeight;
                ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var fullscreen = (UserControl)window.FindName("FullscreenView");
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)window.FindName("WorkspaceView")).Visibility);
                Assert.Equal(Visibility.Visible, fullscreen.Visibility);
                Assert.Same(viewModel, fullscreen.DataContext);
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.Equal(screen.Bounds.Left, window.Left * dpi.DpiScaleX, precision: 5);
                Assert.Equal(screen.Bounds.Top, window.Top * dpi.DpiScaleY, precision: 5);
                Assert.Equal(screen.Bounds.Width, window.Width * dpi.DpiScaleX, precision: 5);
                Assert.Equal(screen.Bounds.Height, window.Height * dpi.DpiScaleY, precision: 5);
                content.Measure(new Size(1240, 750));
                content.Arrange(new Rect(0, 0, 1240, 750));
                content.UpdateLayout();
                FlushBindings();
                Assert.Equal("00:01:00", ((TextBlock)fullscreen.FindName("FullscreenTimerDisplay")).Text);
                Assert.Equal(sessionQuote, ((TextBlock)fullscreen.FindName("FullscreenQuoteDisplay")).Text);
                Assert.Same(sessionImage, viewModel.Media.Background);
                SavePreview(content, window.Background, "fullscreen-focus.png");
                ((Button)fullscreen.FindName("FullscreenOpenBrowserButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BrowserFullscreenView")).Visibility);
                Assert.Equal(Visibility.Collapsed, fullscreen.Visibility);
                var browserInFullscreen = (StudyBrowserView)((ContentControl)window.FindName("BrowserFullscreenHost")).Content;
                Assert.Same(viewModel, browserInFullscreen.DataContext);
                Assert.Contains("返回全屏", ((Button)browserInFullscreen.FindName("BrowserReturnToTimerButton")).Content.ToString());
                ((Button)browserInFullscreen.FindName("BrowserReturnToTimerButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, fullscreen.Visibility);
                Assert.Equal("00:01:00", viewModel.TimerText);
                ((Button)fullscreen.FindName("FullscreenPauseButton")).Command.Execute(null);
                clock.Advance(TimeSpan.FromMinutes(2));
                viewModel.Tick();
                FlushBindings();
                Assert.Equal("00:01:00", ((TextBlock)fullscreen.FindName("FullscreenTimerDisplay")).Text);
                ((Button)fullscreen.FindName("FullscreenResumeButton")).Command.Execute(null);
                Assert.Same(sessionImage, viewModel.Media.Background);
                Assert.Equal(sessionQuote, viewModel.Media.QuoteText);
                ((Button)fullscreen.FindName("ReturnToWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(windowBounds, new Rect(window.Left, window.Top, window.Width, window.Height));
                Assert.Equal(windowStyle, window.WindowStyle);
                Assert.Equal(resizeMode, window.ResizeMode);
                Assert.False(window.Topmost);
                Assert.Equal(minimumWidth, window.MinWidth);
                Assert.Equal(minimumHeight, window.MinHeight);
                Assert.Equal(Visibility.Collapsed, fullscreen.Visibility);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WorkspaceView")).Visibility);
                Assert.Equal("00:01:00", viewModel.TimerText);
                tabs.SelectedIndex = 4;
                content.UpdateLayout();
                FlushBindings();
                var palettes = (ListBox)window.FindName("PaletteSelector");
                Assert.Equal(6, palettes.Items.Count);
                palettes.SelectedItem = viewModel.Palettes.Single(p => p.Id == "pink");
                var appearanceMode = (ComboBox)window.FindName("AppearanceModeSelector");
                appearanceMode.SelectedItem = viewModel.AppearanceModes.Single(m => m.Mode == AppearanceMode.Dark);
                FlushBindings();
                content.UpdateLayout();
                Assert.Equal("00:01:00", viewModel.TimerText);
                Assert.Equal("pink", store.GetSetting("appearance.palette"));
                Assert.Same(sessionImage, viewModel.Media.Background);
                Assert.Equal(sessionQuote, viewModel.Media.QuoteText);
                Assert.Equal("Dark", store.GetSetting("appearance.mode"));
                Assert.Equal(Color.FromRgb(25, 29, 37), ((SolidColorBrush)window.Background).Color);
                Assert.All(TextElements(palettes), text => Assert.Equal(Color.FromRgb(229, 233, 240), ((SolidColorBrush)text.Foreground).Color));
                SavePreview(content, window.Background, "appearance-dark.png");
                VerifyDarkThemeSurfaces(window, viewModel, content);
                tabs.SelectedIndex = 0;
                content.UpdateLayout();
                FlushBindings();
                SavePreview(content, window.Background, "focus-dark.png");
                VerifyFocusFonts(window, viewModel, store, content);
                var pause = (Button)window.FindName("PauseButton");
                Assert.True(pause.IsEnabled);
                pause.Command.Execute(null);
                clock.Advance(TimeSpan.FromMinutes(2));
                viewModel.Tick();
                Assert.Equal("00:01:00", viewModel.TimerText);
                var resume = (Button)window.FindName("ResumeButton");
                FlushBindings();
                Assert.True(resume.IsEnabled);
                resume.Command.Execute(null);
                clock.Advance(TimeSpan.FromMinutes(1));
                ((Button)window.FindName("EndButton")).Command.Execute(null);
                FlushBindings();
                Assert.Single(viewModel.Records);
                Assert.Equal("00:02:00", viewModel.Records[0].Duration);
                Assert.True(start.IsEnabled);
                Assert.False(viewModel.HasUnsavedRecord);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WorkspaceView")).Visibility);
                Assert.Equal(Visibility.Collapsed, fullscreen.Visibility);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                var reopened = new MainViewModel(new SqliteFocusStore(Path.Combine(directory, "test.db")));
                Assert.Equal("00:02:00", Assert.Single(reopened.Records).Duration);
                Assert.Equal("pink", reopened.SelectedPalette.Id);
                Assert.Equal(AppearanceMode.Dark, reopened.SelectedAppearanceMode.Mode);
                Assert.Equal(4, reopened.Folders.Count);
                Assert.Single(reopened.Folders, f => f.Name == "工作" && f.Projects.Count == 0);
                viewModel.SelectedPalette = viewModel.Palettes[0];
                viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(m => m.Mode == AppearanceMode.Light);
                tabs.SelectedIndex = 3;
                content.UpdateLayout();
                FlushBindings();
                SavePreview(content, window.Background, "records-page.png");
                VerifyRecordGridTheme(window, viewModel, content);
                tabs.SelectedIndex = 0;
                content.Measure(new Size(1000, 630));
                content.Arrange(new Rect(0, 0, 1000, 630));
                content.UpdateLayout();
                AssertVisibleInViewport(start, content);
                mode.SelectedIndex = 1;
                viewModel.DurationMinutes = "0.2";
                viewModel.RestMinutes = "1";
                FlushBindings();
                start.Command.Execute(null);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, fullscreen.Visibility);
                clock.Advance(TimeSpan.FromSeconds(15));
                viewModel.Tick();
                Assert.True(viewModel.ShowSkipRest);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("WorkspaceView")).Visibility);
                Assert.Equal(Visibility.Collapsed, fullscreen.Visibility);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                viewModel.SkipRestCommand.Execute(null);
                viewModel.RestMinutes = "0";
                Assert.Equal(2, store.GetRecords().Count);
                var countdown = store.GetRecords().First();
                Assert.Equal(TimerMode.Countdown, countdown.Mode);
                Assert.Equal(TimeSpan.FromSeconds(12), countdown.FocusDuration);
                Assert.Equal(EndReason.CountdownElapsed, countdown.EndReason);
                viewModel.SelectedTimerMode = viewModel.TimerModes[0];
                viewModel.Whitelist.Domain = "example.edu";
                viewModel.Whitelist.SaveWebsiteCommand.Execute(null);
                viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(m => m.Strict);
                viewModel.StartCommand.Execute(null);
                viewModel.Whitelist.Websites.Clear();
                viewModel.Whitelist.Websites.Add(new WebsiteRule("other.edu", true));
                Assert.True(viewModel.CanNavigateWebsite("https://example.edu/"));
                Assert.False(viewModel.CanNavigateWebsite("https://other.edu/"));
                clock.Advance(TimeSpan.FromSeconds(10));
                viewModel.PauseCommand.Execute(null);
                Assert.True(viewModel.RestrictionsActive);
                var blockedPreview = new ApplicationBlockedWindow { DataContext = viewModel };
                blockedPreview.SetApplication("记事本");
                var blockedContent = (FrameworkElement)blockedPreview.Content;
                blockedContent.Measure(new Size(1240, 750));
                blockedContent.Arrange(new Rect(0, 0, 1240, 750));
                blockedContent.UpdateLayout();
                SavePreview(blockedContent, window.Background, "application-blocked.png");
                blockedPreview.Close();
                clock.Advance(TimeSpan.FromMinutes(1));
                Assert.True(viewModel.PrepareExit());
                Assert.False(viewModel.RestrictionsActive);
                Assert.True(store.GetRecords().First().Strict);
                VerifyProjectManagement(window, viewModel, store, clock);
                VerifyProjectNamesAndLastDeletion(window, viewModel, store, clock, directory);
                VerifyProjectSelectionPreference(viewModel, directory);
                VerifyShortFocusRecords(window, viewModel, directory);
                VerifyOrdering(viewModel, directory);
                VerifyCustomQuoteRetention(viewModel, directory);
                RunWithDispatcher(() => VerifyGifBackgroundAsync(window, viewModel, store, clock, directory, "background-animation.gif"));
                RunWithDispatcher(() => VerifyGifBackgroundAsync(window, viewModel, store, clock, directory, "alto-minho-waterfall.gif"));
                RunWithDispatcher(() => VerifyThumbnailLoadingAsync(viewModel, directory));
                RunWithDispatcher(() => VerifyVideoBackgroundAsync(window, viewModel, store, clock, directory, "loop-background.mp4"));
                RunWithDispatcher(() => VerifyVideoBackgroundAsync(window, viewModel, store, clock, directory, "loop-background.wmv"));
                RunWithDispatcher(() => VerifyDefaultVideoAsync(window, viewModel));
                RunWithDispatcher(() => VerifyWindowRestrictionAsync(window, viewModel, directory));
                RunWithDispatcher(() => VerifyBrowserAsync(window, viewModel, directory));
                VerifyTimerPreferences(window, viewModel, store, clock);
                VerifyPomodoro(window, viewModel, store, clock);
                VerifyTimerFloat(window, viewModel, store, clock);
                VerifyTodosAndOutcomes(window, viewModel, store, clock);
                VerifyQuadrants(window, viewModel, store, clock);
                VerifyRestrictionSnapshots(viewModel, store);
                VerifyKeepAwake(viewModel, clock, store);
                VerifyFocusMedia(window, viewModel, store, clock, directory);
                window.Width = Math.Max(window.MinWidth, window.Width - 80);
                window.Height = Math.Max(window.MinHeight, window.Height - 40);
                window.Topmost = true;
                var resizedLayout = window.CaptureWindowLayout();
                CancelEventHandler hideToTray = (_, e) => { e.Cancel = true; window.Hide(); };
                window.Closing += hideToTray;
                window.Close();
                Assert.Equal(resizedLayout, JsonSerializer.Deserialize<WindowLayout>(store.GetSetting(WindowLayout.SettingKey)!));
                window.Closing -= hideToTray;
                var restarted = new MainWindow(viewModel, store);
                new WindowInteropHelper(restarted).EnsureHandle();
                Assert.Equal(resizedLayout, restarted.CaptureWindowLayout());
                restarted.Close();
                ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                window.Close();
                Assert.Equal(resizedLayout, JsonSerializer.Deserialize<WindowLayout>(store.GetSetting(WindowLayout.SettingKey)!));
                application.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                window?.Close();
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void VerifyProjectCardLayout(MainWindow window, MainViewModel viewModel, FrameworkElement content)
    {
        var projects = viewModel.FolderProjects.ToArray();
        var selected = viewModel.SelectedProject;
        var projectList = (ListBox)window.FindName("ProjectList");
        try
        {
            var samples = projects.Concat(Enumerable.Range(0, 5).Select(index => new FocusBloom.Core.Projects.FocusProject(Guid.NewGuid(), $"测试 {index + 3}", "学习", "#9683BE"))).ToArray();
            foreach (var size in new[] { new Size(1000, 630), new Size(1240, 750) })
            {
                Size? singleCardSize = null;
                foreach (var count in new[] { 1, 3, 7 })
                {
                    viewModel.FolderProjects.Clear();
                    foreach (var project in samples.Take(count)) viewModel.FolderProjects.Add(project);
                    content.Measure(size); content.Arrange(new Rect(new Point(), size)); content.UpdateLayout(); FlushBindings();
                    var item = (ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(0);
                    Assert.Equal(120, item.ActualHeight, 3);
                    if (singleCardSize is null) singleCardSize = item.RenderSize;
                    else
                    {
                        Assert.Equal(singleCardSize.Value.Width, item.ActualWidth, 3);
                        Assert.Equal(singleCardSize.Value.Height, item.ActualHeight, 3);
                    }
                    Assert.All(Enumerable.Range(0, count), index => Assert.Equal(120, ((ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(index)).ActualHeight, 3));
                    if (size.Width == 1240 && count == 1) SavePreview(content, window.Background, "project-single-compact.png");
                }
                content.Measure(size); content.Arrange(new Rect(new Point(), size)); content.UpdateLayout(); FlushBindings();
                Assert.InRange(((FrameworkElement)window.FindName("NewProjectArea")).ActualHeight, 0, 40);
                var first = (ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(0);
                var third = (ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(2);
                var fourth = (ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(3);
                Assert.Equal(first.TransformToAncestor(projectList).Transform(new Point()).Y, third.TransformToAncestor(projectList).Transform(new Point()).Y, 3);
                Assert.True(fourth.TransformToAncestor(projectList).Transform(new Point()).Y > first.TransformToAncestor(projectList).Transform(new Point()).Y);
                var scroll = (ScrollViewer)projectList.Template.FindName("PART_ScrollViewer", projectList);
                scroll.ScrollToBottom(); content.UpdateLayout(); FlushBindings();
                var final = (ListBoxItem)projectList.ItemContainerGenerator.ContainerFromIndex(6);
                var finalBounds = final.TransformToAncestor(scroll).TransformBounds(new Rect(final.RenderSize));
                Assert.True(finalBounds.Bottom <= scroll.ViewportHeight + 1, "最后一行项目应可完整滚入列表。");
                scroll.ScrollToTop(); content.UpdateLayout(); FlushBindings();
            }
            projectList.SelectedIndex = 2; FlushBindings(); Assert.Same(viewModel.FolderProjects[2], viewModel.SelectedProject);
            viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
            content.UpdateLayout(); FlushBindings(); SavePreview(content, window.Background, "projects-three-columns.png");
        }
        finally
        {
            viewModel.FolderProjects.Clear(); foreach (var project in projects) viewModel.FolderProjects.Add(project);
            viewModel.SelectedProject = selected;
            viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Light);
        }
    }

    private static void VerifyRecordGridTheme(MainWindow window, MainViewModel viewModel, FrameworkElement content)
    {
        var grid = (DataGrid)window.FindName("RecordsGrid");
        var records = viewModel.Records.ToArray();
        try
        {
            for (var index = 0; index < 5; index++) viewModel.Records.Add(records[0] with { Project = $"记录 {index + 2}" });
            foreach (var mode in new[] { AppearanceMode.Dark, AppearanceMode.Light, AppearanceMode.Dark })
            {
                viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == mode);
                grid.UnselectAll(); content.UpdateLayout(); FlushBindings();
                for (var index = 0; index < 6; index++)
                {
                    var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(index);
                    Assert.NotNull(row);
                    var expected = (SolidColorBrush)Application.Current.FindResource(row.IsMouseOver ? "HoverBrush" : index % 2 == 0 ? "CardBrush" : "FieldBrush");
                    Assert.Equal(expected.Color, ((SolidColorBrush)row.Background).Color);
                    Assert.All(Descendants<DataGridCell>(row), cell => Assert.Equal(((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, ((SolidColorBrush)cell.Foreground).Color));
                }
                grid.SelectedIndex = 1; grid.Focus(); content.UpdateLayout(); FlushBindings();
                var selectedRow = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(1);
                Assert.Equal(((SolidColorBrush)Application.Current.FindResource("SelectedBrush")).Color, ((SolidColorBrush)selectedRow.Background).Color);
                grid.UnselectAll(); content.UpdateLayout(); FlushBindings();
                SavePreview(content, window.Background, mode == AppearanceMode.Dark ? "records-dark.png" : "records-light.png");
            }
        }
        finally
        {
            viewModel.Records.Clear(); foreach (var record in records) viewModel.Records.Add(record);
            viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Light);
        }
    }

    private static void VerifyDarkThemeSurfaces(MainWindow window, MainViewModel viewModel, FrameworkElement content)
    {
        Assert.True(Appearance.IsDark); Assert.True(WindowAppearance.Apply(window));


        var apps = (ListBox)window.FindName("RestrictionApplicationList");
        var sites = (ListBox)window.FindName("RestrictionWebsiteList");
        var savedApps = viewModel.Whitelist.Applications.ToArray();
        var savedSites = viewModel.Whitelist.Websites.ToArray();
        viewModel.Whitelist.Applications.Add(new AllowedApplication("设置", @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe"));
        viewModel.Whitelist.Applications.Add(new AllowedApplication("Google Chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe"));
        viewModel.Whitelist.Websites.Add(new WebsiteRule("amy22707.github.io", true));
        viewModel.Whitelist.Websites.Add(new WebsiteRule("chatgpt.com", true));
        apps.SelectedIndex = 1;
        foreach (var list in new[] { apps, sites })
        {
            list.Measure(new Size(820, 180)); list.Arrange(new Rect(0, 0, 820, 180)); list.UpdateLayout(); FlushBindings();
            var surface = (Border)list.Template.FindName("ListSurface", list);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)surface.Background).Color);
            Assert.Equal(Colors.Transparent, ((SolidColorBrush)((ScrollViewer)list.Template.FindName("PART_ScrollViewer", list)).Background).Color);
            Assert.All(TextElements(list).Where(element => !element.Text.Contains(@"C:\")), element => Assert.Equal(Color.FromRgb(229, 233, 240), ((SolidColorBrush)element.Foreground).Color));
        }
        SavePreview(apps, (Brush)Application.Current.FindResource("CardBrush"), "application-list-dark.png", 820, 180);
        SavePreview(sites, (Brush)Application.Current.FindResource("CardBrush"), "website-list-dark.png", 820, 180);
        var picker = new ApplicationPickerWindow();
        try
        {
        new WindowInteropHelper(picker).EnsureHandle();
        Assert.True(WindowAppearance.Apply(picker));
        viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Light);
        Assert.False(Appearance.IsDark); Assert.True(WindowAppearance.Apply(window));

        Assert.True(WindowAppearance.Apply(picker));
        viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark);
        viewModel.RefreshSystemTheme();
        Assert.True(Appearance.IsDark); Assert.True(WindowAppearance.Apply(window));
        }
        finally { picker.Close(); }
        viewModel.Whitelist.Applications.Clear(); foreach (var app in savedApps) viewModel.Whitelist.Applications.Add(app);
        viewModel.Whitelist.Websites.Clear(); foreach (var site in savedSites) viewModel.Whitelist.Websites.Add(site);
        content.UpdateLayout(); FlushBindings();
    }


    private static void VerifyFocusFonts(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, FrameworkElement content)
    {
        var typography = viewModel.Typography;
        var image = viewModel.Media.Background; var quote = viewModel.Media.QuoteText; var timer = viewModel.TimerText;
        var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedIndex = 4;
        content.UpdateLayout(); FlushBindings();
        var settings = (FocusFontSettingsView)((Border)window.FindName("FocusFontSettingsCard")).Child;
        Assert.Same(typography, settings.DataContext);
        var preferencesScroll = (ScrollViewer)window.FindName("PreferencesScrollViewer");
        preferencesScroll.ScrollToVerticalOffset(preferencesScroll.ScrollableHeight / 2);
        FlushBindings();
        var offsetBeforeWheel = preferencesScroll.VerticalOffset;
        var fontSample = TextElements((ListBox)settings.FindName("ChineseFontSelector")).First(text => text.Text == typography.ChineseFonts[0].Preview);
        fontSample.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        FlushBindings();
        Assert.True(preferencesScroll.VerticalOffset > offsetBeforeWheel, "滚轮位于字体卡片时应继续向下滚动设置页面。");
        var offsetAfterWheel = preferencesScroll.VerticalOffset;
        fontSample.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        FlushBindings();
        Assert.True(preferencesScroll.VerticalOffset < offsetAfterWheel, "字体卡片上的滚轮也应支持向上滚动。");
        ((ListBox)settings.FindName("ChineseFontSelector")).SelectedItem = typography.ChineseFonts.Single(font => font.Family == "SimSun");
        ((ListBox)settings.FindName("EnglishFontSelector")).SelectedItem = typography.EnglishFonts.Single(font => font.Family == "Georgia");
        var mechanical = typography.TimerFonts.FirstOrDefault(font => font.Family == "OCR A Extended") ?? typography.TimerFonts.Single(font => font.Family == "Bahnschrift");
        ((ListBox)settings.FindName("TimerFontSelector")).SelectedItem = mechanical;
        FlushBindings();
        Assert.Contains("SimSun", ((TextBlock)window.FindName("FocusQuoteDisplay")).FontFamily.Source);
        Assert.Contains("Georgia", ((TextBlock)window.FindName("FocusQuoteDisplay")).FontFamily.Source);
        Assert.Equal(mechanical.Family, ((TextBlock)window.FindName("TimerDisplay")).FontFamily.Source);
        var fullscreen = (FullscreenFocusView)window.FindName("FullscreenView");
        Assert.Equal(((TextBlock)window.FindName("FocusQuoteDisplay")).FontFamily.Source, ((TextBlock)fullscreen.FindName("FullscreenQuoteDisplay")).FontFamily.Source);
        Assert.Equal(mechanical.Family, ((TextBlock)fullscreen.FindName("FullscreenTimerDisplay")).FontFamily.Source);
        Assert.Same(image, viewModel.Media.Background); Assert.Equal(quote, viewModel.Media.QuoteText); Assert.Equal(timer, viewModel.TimerText);
        Assert.True(viewModel.IsFocusActive);
        var reopened = new FocusTypographyViewModel(store, _ => { });
        Assert.Equal("SimSun", reopened.Chinese.Family); Assert.Equal("Georgia", reopened.English.Family); Assert.Equal(mechanical.Family, reopened.Timer.Family);
        settings.Measure(new Size(820, double.PositiveInfinity));
        settings.Arrange(new Rect(0, 0, 820, settings.DesiredSize.Height)); settings.UpdateLayout();
        SavePreview(settings, window.Background, "focus-font-settings.png", 820, (int)Math.Ceiling(settings.ActualHeight));
        tabs.SelectedIndex = 0; content.UpdateLayout(); FlushBindings();
        SavePreview(content, window.Background, "focus-custom-fonts.png");
        typography.ResetCommand.Execute(null); FlushBindings();
        Assert.Equal("Microsoft YaHei", new FocusTypographyViewModel(store, _ => { }).Chinese.Family);
        Assert.Equal("Segoe UI", ((TextBlock)window.FindName("TimerDisplay")).FontFamily.Source);
    }

    private static async Task VerifyGifBackgroundAsync(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock, string directory, string fixture)
    {
        var media = model.Media;
        var previousBackground = media.SelectedBackground;
        var gifPath = Path.Combine(directory, "动态背景-" + fixture);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), gifPath);
        media.ImportImages([gifPath]);
        var card = media.Images.Single(image => image.Asset.File == gifPath);
        Assert.True(card.IsGif); Assert.NotNull(card.Thumbnail); Assert.Equal("自定义", card.Origin);
        card.Favorite = true;
        media.SelectedBackground = card.Asset;
        using (var restored = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(card.Asset.Id, restored.SelectedBackground.Id);
            Assert.Equal(gifPath, restored.BackgroundGifUri!.LocalPath);
            Assert.True(restored.Images.Single(image => image.Asset.Id == card.Asset.Id).Favorite);
        }
        var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedIndex = 0;
        model.SelectedTimerMode = model.TimerModes[0]; model.SelectedRestrictionMode = model.RestrictionModes[0]; model.RestMinutes = "0";
        model.StartCommand.Execute(null); window.UpdateLayout(); FlushBindings();
        var normal = (FocusBackgroundView)window.FindName("FocusBackground");
        var normalImage = (Image)normal.FindName("GifImage");
        string? animationError = null;
        normalImage.AddHandler(XamlAnimatedGif.AnimationBehavior.ErrorEvent, new XamlAnimatedGif.AnimationErrorEventHandler((_, args) => animationError = args.Exception.ToString()), true);
        await WaitForAsync(() => XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage) is not null);
        await WaitForAsync(() => normalImage.Source is BitmapSource && Pixel(normalImage).Any(value => value != 0));
        var first = Pixel(normalImage);
        await WaitForAsync(() => !Pixel(normalImage).SequenceEqual(first));
        if (fixture == "background-animation.gif") await WaitForAsync(() => Pixel(normalImage).SequenceEqual(first));
        await Task.Delay(fixture == "background-animation.gif" ? 850 : 1500);
        var later = Pixel(normalImage);
        await WaitForAsync(() => !Pixel(normalImage).SequenceEqual(later));
        Assert.Equal(card.Asset.Id, media.CurrentBackgroundId);
        if (fixture == "alto-minho-waterfall.gif")
        {
            Assert.Equal(972, ((BitmapSource)normalImage.Source).PixelWidth);
            Assert.Equal(728, ((BitmapSource)normalImage.Source).PixelHeight);
            SavePreview((FrameworkElement)window.Content, window.Background, "gif-waterfall-focus.png");
        }
        model.PauseCommand.Execute(null);
        later = Pixel(normalImage);
        await WaitForAsync(() => !Pixel(normalImage).SequenceEqual(later));
        model.ResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(3)); model.Tick();
        ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var fullscreen = (FullscreenFocusView)window.FindName("FullscreenView");
        var fullBackground = (FocusBackgroundView)fullscreen.FindName("FullscreenBackground");
        var fullImage = (Image)fullBackground.FindName("GifImage");
        await WaitForAsync(() => XamlAnimatedGif.AnimationBehavior.GetAnimator(fullImage) is not null);
        await WaitForAsync(() => fullImage.Source is BitmapSource && Pixel(fullImage).Any(value => value != 0));
        Assert.Null(XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage));
        Assert.Equal(media.BackgroundGifUri, fullBackground.AnimatedUri);
        later = Pixel(fullImage); await WaitForAsync(() => !Pixel(fullImage).SequenceEqual(later));
        Assert.Equal(card.Asset.Id, media.CurrentBackgroundId); Assert.Equal("00:00:03", model.TimerText);
        if (fixture == "alto-minho-waterfall.gif") SavePreview((FrameworkElement)window.Content, window.Background, "gif-waterfall-fullscreen.png");
        ((Button)fullscreen.FindName("ReturnToWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage) is not null);
        Assert.Null(XamlAnimatedGif.AnimationBehavior.GetAnimator(fullImage));
        tabs.SelectedIndex = 1; FlushBindings();
        Assert.Null(XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage));
        tabs.SelectedIndex = 0; window.UpdateLayout(); FlushBindings();
        await WaitForAsync(() => XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage) is not null);
        window.Hide(); FlushBindings(); Assert.Null(XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage));
        window.Show(); window.UpdateLayout(); FlushBindings();
        await WaitForAsync(() => XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage) is not null);
        media.SelectedBackground = media.BackgroundChoices.Single(asset => asset.Id == "theme");
        Assert.Equal(gifPath, media.BackgroundGifUri!.LocalPath);
        model.EndCommand.Execute(null);
        model.StartCommand.Execute(null); FlushBindings();
        Assert.Null(media.BackgroundGifUri); Assert.Null(XamlAnimatedGif.AnimationBehavior.GetAnimator(normalImage));
        model.EndCommand.Execute(null); media.RemoveImage(card); media.SelectedBackground = previousBackground;
        File.Delete(gifPath);

        byte[] Pixel(Image image)
        {
            Assert.True(image.Source is BitmapSource, animationError ?? $"GIF 背景未显示图像：{fixture}；{model.StatusMessage}");
            var bitmap = (BitmapSource)image.Source;
            var width = Math.Min(32, bitmap.PixelWidth); var height = Math.Min(32, bitmap.PixelHeight);
            var pixel = new byte[width * height * 4];
            bitmap.CopyPixels(new Int32Rect((bitmap.PixelWidth - width) / 2, (bitmap.PixelHeight - height) * 3 / 4, width, height), pixel, width * 4, 0);
            return pixel;
        }
    }

    private static void VerifyProjectManagement(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock)
    {
        var previousProject = model.SelectedProject!;
        model.NewFolderName = "待整理"; model.AddFolderCommand.Execute(null);
        model.NewProjectName = "草稿"; model.AddProjectCommand.Execute(null);
        var project = model.SelectedProject!;
        model.NewProjectName = "保留项目"; model.AddProjectCommand.Execute(null);
        var sibling = model.SelectedProject!;
        var source = model.SelectedFolder!;
        model.NewFolderName = "归档位置"; model.AddFolderCommand.Execute(null);
        var destination = model.SelectedFolder!;
        model.SelectedFolder = source;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1;
        window.UpdateLayout(); FlushBindings();
        Assert.Contains(FindButtons((ListBox)window.FindName("FolderList")), button => button.ToolTip?.ToString() == "修改或删除文件夹");
        Assert.Contains(FindButtons((ListBox)window.FindName("ProjectList")), button => button.ToolTip?.ToString() == "修改或删除项目");
        Assert.False(model.RenameFolder(source, ""));
        Assert.False(model.RenameFolder(source, destination.Name));
        var folderEditor = new ProjectEditorWindow(model, source) { Owner = window };
        Assert.Equal(Visibility.Collapsed, ((FrameworkElement)folderEditor.FindName("FolderField")).Visibility);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((TextBox)folderEditor.FindName("NameInput")).Text = "课程草稿";
            ((Button)folderEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(folderEditor.ShowDialog());
        source = model.SelectedFolder!;
        Assert.Equal("课程草稿", source.Name); Assert.Equal(2, source.Projects.Count);
        Assert.All(source.Projects, item => Assert.Equal(source.Name, item.Category));
        project = model.Projects.Single(item => item.Id == project.Id);
        model.SelectedProject = project;
        model.SelectedTimerMode = model.TimerModes[0]; model.SelectedRestrictionMode = model.RestrictionModes[0]; model.RestMinutes = "0";
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(30)); model.Tick();
        Assert.False(model.CanDeleteProject(project)); Assert.False(model.DeleteProject(project));
        Assert.False(model.CanDeleteFolder(source)); Assert.False(model.DeleteFolder(source));
        var projectEditor = new ProjectEditorWindow(model, project) { Owner = window };
        Assert.True(((Button)projectEditor.FindName("DeleteButton")).IsEnabled);
        model.SelectedAppearanceMode = model.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((TextBox)projectEditor.FindName("NameInput")).Text = "整理后的项目";
            ((ComboBox)projectEditor.FindName("FolderSelector")).SelectedItem = model.Folders.Single(folder => folder.Name == destination.Name);
            projectEditor.UpdateLayout();
            var editorContent = (FrameworkElement)projectEditor.Content;
            SavePreview(editorContent, projectEditor.Background, "project-editor-dark.png", (int)projectEditor.ActualWidth, (int)projectEditor.ActualHeight);
            ((Button)projectEditor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(projectEditor.ShowDialog());
        Assert.Equal(project.Id, model.SelectedProject!.Id); Assert.Equal(destination.Name, model.SelectedFolder!.Name);
        Assert.Equal("整理后的项目", model.ActiveProjectName); Assert.Equal("00:00:30", model.TimerText);
        Assert.Single(model.Folders.Single(folder => folder.Name == source.Name).Projects);
        destination = model.SelectedFolder;
        Assert.False(model.DeleteFolder(destination));
        clock.Advance(TimeSpan.FromSeconds(30)); model.EndCommand.Execute(null);
        project = model.SelectedProject;
        Assert.True(model.DeleteProject(project)); Assert.Empty(model.FolderProjects);
        Assert.True(model.DeleteFolder(model.Folders.Single(folder => folder.Name == source.Name)));
        Assert.True(model.DeleteFolder(model.Folders.Single(folder => folder.Name == destination.Name)));
        Assert.DoesNotContain(model.Projects, item => item.Id == project.Id || item.Id == sibling.Id);
        Assert.Contains(model.Records, row => row.Project == "整理后的项目" && row.Duration == "00:01:00");
        Assert.Equal(project.Id, store.GetRecords().First(record => record.ProjectId == project.Id).ProjectId);
        model.SelectedFolder = model.Folders.Single(folder => folder.Name == previousProject.Category);
        model.SelectedProject = model.Projects.Single(item => item.Id == previousProject.Id);
        model.SelectedAppearanceMode = model.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Light);
    }

    private static void VerifyProjectNamesAndLastDeletion(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock, string directory)
    {
        var previous = model.SelectedProject!;
        model.NewFolderName = "Reports"; model.AddFolderCommand.Execute(null);
        model.NewProjectName = "Report"; model.AddProjectCommand.Execute(null);
        var project = model.SelectedProject!;
        var folderCount = model.Folders.Count; var projectCount = model.Projects.Count;
        model.NewFolderName = " reports "; model.AddFolderCommand.Execute(null);
        Assert.Equal(folderCount, model.Folders.Count); Assert.Contains("同名", model.StatusMessage);
        model.NewProjectName = " report "; model.AddProjectCommand.Execute(null);
        Assert.Equal(projectCount, model.Projects.Count); Assert.Contains("同名", model.StatusMessage);
        model.NewProjectName = "Notes"; model.AddProjectCommand.Execute(null);
        var notes = model.SelectedProject!;
        Assert.False(model.UpdateProject(notes, " report ", model.SelectedFolder));
        Assert.Equal("Notes", model.Projects.Single(item => item.Id == notes.Id).Name);
        Assert.True(model.DeleteProject(notes));
        model.NewFolderName = "Archive"; model.AddFolderCommand.Execute(null);
        model.NewProjectName = "Report"; model.AddProjectCommand.Execute(null);
        var other = model.SelectedProject!;
        Assert.NotEqual(project.Id, other.Id);
        Assert.False(model.RenameFolder(model.SelectedFolder!, " reports "));
        Assert.False(model.UpdateProject(project, "Report", model.SelectedFolder));
        Assert.Equal("Reports", model.Projects.Single(item => item.Id == project.Id).Category);
        Assert.True(model.UpdateProject(project, "Moved", model.SelectedFolder));
        Assert.True(model.UpdateProject(model.SelectedProject!, "Report", model.Folders.Single(folder => folder.Name == "Reports")));
        project = model.SelectedProject!;
        Assert.Single(model.FolderProjects);
        model.SelectedTimerMode = model.TimerModes[0]; model.SelectedRestrictionMode = model.RestrictionModes[0]; model.RestMinutes = "0";
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(12)); model.PauseCommand.Execute(null);
        FlushBindings();
        Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("FocusConfigurationHint")).Visibility);
        Assert.Equal(Visibility.Visible, ((TextBlock)window.FindName("RestrictionEditHint")).Visibility);
        void DeleteFromEditor(ProjectEditorWindow editor, bool allowed, bool confirm)
        {
            editor.Owner = window;
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                Assert.True(((Button)editor.FindName("DeleteButton")).IsEnabled);
                Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
                {
                    var dialog = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single();
                    Assert.Equal(allowed ? Visibility.Visible : Visibility.Collapsed, ((Button)dialog.FindName("ConfirmDeletionButton")).Visibility);
                    Assert.Equal(allowed ? Visibility.Collapsed : Visibility.Visible, ((Button)dialog.FindName("NoticeButton")).Visibility);
                    if (!allowed) Assert.Contains("先结束", ((TextBlock)dialog.FindName("DeletionMessage")).Text);
                    if (allowed && confirm) ((Button)dialog.FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    else dialog.DialogResult = false;
                }));
                ((Button)editor.FindName("DeleteButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!allowed || !confirm) editor.DialogResult = false;
            }));
            Assert.Equal(allowed && confirm, editor.ShowDialog());
        }
        DeleteFromEditor(new ProjectEditorWindow(model, project), false, false);
        DeleteFromEditor(new ProjectEditorWindow(model, model.SelectedFolder!), false, false);
        Assert.True(model.IsFocusActive); Assert.Single(model.FolderProjects);
        model.EndCommand.Execute(null);
        FlushBindings();
        Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("FocusConfigurationHint")).Visibility);
        Assert.Equal(Visibility.Collapsed, ((TextBlock)window.FindName("RestrictionEditHint")).Visibility);
        DeleteFromEditor(new ProjectEditorWindow(model, project), true, false);
        Assert.Single(model.FolderProjects);
        DeleteFromEditor(new ProjectEditorWindow(model, project), true, true);
        Assert.Empty(model.FolderProjects); Assert.Null(model.SelectedProject); Assert.True(model.IsFolderEmpty);
        Assert.Contains(model.Folders, folder => folder.Name == "Reports");
        Assert.Contains(model.Records, record => record.Project == "Report" && record.Duration == "00:00:12");
        var reopened = new MainViewModel(new SqliteFocusStore(Path.Combine(directory, "test.db")));
        Assert.Empty(reopened.Folders.Single(folder => folder.Name == "Reports").Projects);
        reopened.Media.Dispose();
        DeleteFromEditor(new ProjectEditorWindow(model, model.Folders.Single(folder => folder.Name == "Archive")), true, true);
        Assert.DoesNotContain(model.Projects, item => item.Id == other.Id);
        Assert.True(model.DeleteFolder(model.Folders.Single(folder => folder.Name == "Reports")));
        model.SelectedFolder = model.Folders.Single(folder => folder.Name == previous.Category);
        model.SelectedProject = model.Projects.Single(item => item.Id == previous.Id);
        model.NewProjectName = ""; model.NewFolderName = "";

    }

    private static void VerifyProjectSelectionPreference(MainViewModel original, string directory)
    {
        var path = Path.Combine(directory, "selection.db");
        var store = new SqliteFocusStore(path);
        var model = new MainViewModel(store);
        model.NewFolderName = "上次选择"; model.AddFolderCommand.Execute(null);
        model.NewProjectName = "第一项"; model.AddProjectCommand.Execute(null);
        var first = model.SelectedProject!;
        model.NewProjectName = "第二项"; model.AddProjectCommand.Execute(null);
        var second = model.SelectedProject!;
        model.SelectedProject = first; model.SelectedProject = second;
        var restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Equal("上次选择", restored.SelectedFolder!.Name); Assert.Equal(second.Id, restored.SelectedProject!.Id);
        var restoredWindow = new MainWindow(restored, store, Path.Combine(directory, "selection-browser"));
        new WindowInteropHelper(restoredWindow).EnsureHandle();
        var content = (FrameworkElement)restoredWindow.Content;
        content.Measure(new Size(1240, 750)); content.Arrange(new Rect(0, 0, 1240, 750)); content.UpdateLayout(); FlushBindings();
        Assert.Same(restored.SelectedFolder, ((ComboBox)restoredWindow.FindName("FocusFolderSelector")).SelectedItem);
        Assert.Same(restored.SelectedProject, ((ComboBox)restoredWindow.FindName("ProjectSelector")).SelectedItem);
        restoredWindow.Close(); restored.Media.Dispose();
        Assert.True(model.RenameFolder(model.SelectedFolder!, "改名后"));
        model.NewFolderName = "移动目标"; model.AddFolderCommand.Execute(null);
        Assert.True(model.UpdateProject(model.Projects.Single(project => project.Id == second.Id), "第二项新版", model.SelectedFolder));
        restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Equal(second.Id, restored.SelectedProject!.Id); Assert.Equal("第二项新版", restored.SelectedProject.Name);
        Assert.Equal("移动目标", restored.SelectedFolder!.Name); restored.Media.Dispose();
        store.DeleteProject(second.Id);
        restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Equal("移动目标", restored.SelectedFolder!.Name); Assert.Null(restored.SelectedProject); restored.Media.Dispose();
        store.DeleteFolder("移动目标");
        restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Same(restored.Folders.First(folder => folder.Projects.Count > 0), restored.SelectedFolder);
        Assert.Same(restored.SelectedFolder!.Projects.First(), restored.SelectedProject);
        restored.NewFolderName = "留空"; restored.AddFolderCommand.Execute(null); restored.Media.Dispose();
        restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Equal("留空", restored.SelectedFolder!.Name); Assert.Null(restored.SelectedProject); restored.Media.Dispose();
        model.Media.Dispose();

        var historyStore = new SqliteFocusStore(Path.Combine(directory, "selection-history.db"));
        historyStore.SaveFolder("之前的专注");
        var historyProject = new FocusBloom.Core.Projects.FocusProject(Guid.NewGuid(), "最近使用", "之前的专注", "#7B97A6");
        historyStore.SaveProject(historyProject);
        var clock = new ManualClock(); var timer = new FocusTimer(clock);
        timer.Start(new FocusOptions(historyProject.Id, TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero));
        clock.Advance(TimeSpan.FromSeconds(5)); timer.End(); historyStore.SaveRecord(timer.LastRecord!);
        restored = new MainViewModel(historyStore);
        Assert.Equal(historyProject.Id, restored.SelectedProject!.Id); Assert.Equal(historyProject.Category, restored.SelectedFolder!.Name);
        restored.Media.Dispose(); original.RefreshSystemTheme();
    }

    private static void VerifyOrdering(MainViewModel original, string directory)
    {
        var path = Path.Combine(directory, "ordering.db"); var store = new SqliteFocusStore(path);
        var clock = new ManualClock(); var model = new MainViewModel(store, new FocusTimer(clock));
        model.NewFolderName = "排序一"; model.AddFolderCommand.Execute(null);
        foreach (var name in new[] { "项目一", "项目二", "项目三" }) { model.NewProjectName = name; model.AddProjectCommand.Execute(null); }
        model.NewFolderName = "排序二"; model.AddFolderCommand.Execute(null);
        model.NewProjectName = "另一项目"; model.AddProjectCommand.Execute(null);
        var folder = model.Folders.Single(item => item.Name == "排序一");
        model.SelectedFolder = folder; model.SelectedProject = folder.Projects[1];
        var selected = model.SelectedProject!;
        var window = new MainWindow(model, store, Path.Combine(directory, "ordering-browser"));
        new WindowInteropHelper(window).EnsureHandle();
        window.Show();
        var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedIndex = 1;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1240, 750)); content.Arrange(new Rect(0, 0, 1240, 750)); content.UpdateLayout(); FlushBindings();
        ReorderButton Handle(DependencyObject parent, object data) => Descendants<ReorderButton>(parent).Single(button => ReferenceEquals(button.DataContext, data));
        void MenuMove(ReorderButton handle, string title)
        {
            handle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.True(handle.ContextMenu.IsOpen);
            handle.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == title).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            handle.ContextMenu.IsOpen = false; window.UpdateLayout(); FlushBindings();
        }
        void Drop(ReorderButton handle, ItemsControl owner, FrameworkElement target)
        {
            var before = owner.Items.Cast<object>().ToArray();
            var sourceContainer = (FrameworkElement)owner.ItemContainerGenerator.ContainerFromItem(handle.DataContext);
            var originalTransform = sourceContainer.RenderTransform; var originalOpacity = sourceContainer.Opacity;
            var payloadType = typeof(ReorderButton).GetNestedType("DragItem", BindingFlags.NonPublic)!;
            var payload = Activator.CreateInstance(payloadType, handle, owner)!;
            var data = new DataObject("FocusBloom.ItemOrder", payload);
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            DragEventArgs Args(RoutedEvent routed) => (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, target, new Point(10, 10)]);
            var over = Args(DragDrop.DragOverEvent); over.RoutedEvent = DragDrop.DragOverEvent; target.RaiseEvent(over);
            Assert.Equal(DragDropEffects.Move, over.Effects);
            Assert.Equal(before, owner.Items.Cast<object>()); Assert.Equal(0.16, sourceContainer.Opacity);
            RunWithDispatcher(async () => await Task.Delay(200));
            Assert.IsType<TransformGroup>(sourceContainer.RenderTransform);
            var previewObject = payloadType.GetProperty("Preview")!.GetValue(payload)!;
            Assert.NotNull(previewObject.GetType().GetProperty("Target")!.GetValue(previewObject));
            Assert.NotNull(previewObject.GetType().GetField("_ghost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(previewObject));
            SavePreview(window, window.Background, "ordering-drag-preview.png", (int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight));
            ((IDisposable)previewObject).Dispose(); payloadType.GetProperty("Preview")!.SetValue(payload, null);
            Assert.Equal(before, owner.Items.Cast<object>()); Assert.Same(originalTransform, sourceContainer.RenderTransform);
            Assert.Equal(originalOpacity, sourceContainer.Opacity);
            over = Args(DragDrop.DragOverEvent); over.RoutedEvent = DragDrop.DragOverEvent; target.RaiseEvent(over);
            var drop = Args(DragDrop.DropEvent); drop.RoutedEvent = DragDrop.DropEvent; target.RaiseEvent(drop);
            Assert.True(drop.Handled); FlushBindings();
            Assert.Same(originalTransform, sourceContainer.RenderTransform); Assert.Equal(originalOpacity, sourceContainer.Opacity);
        }
        var folders = (ListBox)window.FindName("FolderList"); var projects = (ListBox)window.FindName("ProjectList");
        Assert.True(ReorderButton.GetEnableDrop(folders)); Assert.True(ReorderButton.GetEnableDrop(projects));
        MenuMove(Handle(folders, folder), "移到最前"); Assert.Same(folder, model.Folders[0]);
        var grip = Handle(folders, folder);
        Assert.Empty(Descendants<Border>(grip));
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)Descendants<Grid>(grip).First().Background).Color);
        SavePreview(content, window.Background, "ordering-projects.png");
        Assert.Same(folder, model.SelectedFolder); Assert.Same(selected, model.SelectedProject);
        model.SelectedTimerMode = model.TimerModes[0]; model.SelectedRestrictionMode = model.RestrictionModes[0]; model.RestMinutes = "0";
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(12)); model.Tick();
        var source = model.FolderProjects[2]; var target = model.FolderProjects[0];
        Drop(Handle(projects, source), projects, (FrameworkElement)projects.ItemContainerGenerator.ContainerFromItem(target));
        Assert.Same(source, model.FolderProjects[0]); Assert.Same(selected, model.SelectedProject); Assert.Equal("00:00:12", model.TimerText);
        Assert.Equal(selected.Name, model.ActiveProjectName);
        model.EndCommand.Execute(null);
        var folderOrder = model.Folders.Select(item => item.Name).ToArray(); var projectOrder = model.FolderProjects.Select(item => item.Id).ToArray();
        var restored = new MainViewModel(new SqliteFocusStore(path));
        Assert.Equal(folderOrder, restored.Folders.Select(item => item.Name)); Assert.Equal(projectOrder, restored.FolderProjects.Select(item => item.Id)); restored.Media.Dispose();
        Assert.True(model.RenameFolder(folder, "排序一改名")); Assert.Equal("排序一改名", model.Folders[0].Name);
        model.NewProjectName = "新项目"; model.AddProjectCommand.Execute(null); Assert.Equal("新项目", model.FolderProjects.Last().Name);
        tabs.SelectedIndex = 4; window.UpdateLayout(); FlushBindings();
        var settings = (FocusMediaSettingsView)((Border)window.FindName("FocusMediaSettingsCard")).Child;
        var media = model.Media; media.SelectedBackground = media.Images[0].Asset; media.SelectedQuote = media.Quotes[0].Quote; media.SelectedSound = media.Sounds[0].Asset;
        model.StartCommand.Execute(null); var scene = media.Background; var quote = media.QuoteText;
        foreach (var name in new[] { "BackgroundLibrary", "QuoteLibrary", "SoundLibrary" }) ((Expander)settings.FindName(name)).IsExpanded = true;
        content.Measure(new Size(1240, 750)); content.Arrange(new Rect(0, 0, 1240, 750)); content.UpdateLayout(); FlushBindings();
        var images = (ItemsControl)settings.FindName("BackgroundCards"); var quotes = (ItemsControl)settings.FindName("QuoteCards"); var sounds = (ItemsControl)settings.FindName("SoundCards");
        Assert.True(ReorderButton.GetEnableDrop(images)); Assert.True(ReorderButton.GetEnableDrop(quotes)); Assert.True(ReorderButton.GetEnableDrop(sounds));
        var image = media.Images[2]; var text = media.Quotes[2]; var sound = media.Sounds[2];
        Drop(Handle(images, image), images, (FrameworkElement)images.ItemContainerGenerator.ContainerFromItem(media.Images[0]));
        MenuMove(Handle(quotes, text), "移到最前"); MenuMove(Handle(sounds, sound), "移到最后");
        Assert.Same(image, media.Images[0]); Assert.Same(text, media.Quotes[0]); Assert.Same(sound, media.Sounds.Last());
        Assert.Same(scene, media.Background); Assert.Equal(quote, media.QuoteText);
        Assert.Equal(media.Sounds.Select(item => item.Asset.Id), media.QuickSounds.Where(item => item.Id.Length > 0).Select(item => item.Id));
        media.Images[0].Favorite = true; media.Images[2].Favorite = true; media.ShowFavoriteImages = true;
        var favorite = media.VisibleImages.Last(); media.MoveImage(favorite, OrderMove.First);
        Assert.Same(favorite, media.VisibleImages.First()); media.ShowFavoriteImages = false;
        using (var loaded = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(media.Images.Select(item => item.Asset.Id), loaded.Images.Select(item => item.Asset.Id));
            Assert.Equal(media.Quotes.Select(item => item.Quote.Id), loaded.Quotes.Select(item => item.Quote.Id));
            Assert.Equal(media.Sounds.Select(item => item.Asset.Id), loaded.Sounds.Select(item => item.Asset.Id));
        }
        var beforeRestore = media.Quotes.Select(item => item.Quote.Id).ToArray(); media.RestoreQuotes();
        Assert.Equal(beforeRestore, media.Quotes.Select(item => item.Quote.Id));
        var localImage = Path.Combine(directory, "rename-background.jpg"); File.Copy(media.Images[0].Asset.File, localImage);
        media.ImportImages([localImage]); var custom = media.Images.Single(item => item.Asset.File == localImage);
        var thumbnail = custom.Thumbnail; custom.Favorite = true; custom.Excluded = true;
        Assert.False(media.RenameImage(media.Images.First(item => !item.Custom), "不能改默认"));
        Assert.False(media.RenameImage(custom, " "));
        var editor = new RenameBackgroundWindow(media, custom) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((TextBox)editor.FindName("NameInput")).Text = "我的背景显示名";
            ((Button)editor.FindName("SaveNameButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(editor.ShowDialog()); Assert.Equal("我的背景显示名", custom.Name);
        Assert.Equal(localImage, custom.Asset.File); Assert.True(File.Exists(localImage));
        Assert.Same(thumbnail, custom.Thumbnail); Assert.True(custom.Favorite); Assert.True(custom.Excluded);
        Assert.Equal(quote, media.QuoteText); Assert.Same(scene, media.Background);
        using (var renamed = new FocusMediaViewModel(store, _ => { })) Assert.Equal(custom.Name, renamed.Images.Single(item => item.Asset.Id == custom.Asset.Id).Name);
        SavePreview(content, window.Background, "ordering-media.png");
        model.EndCommand.Execute(null); window.Close(); media.Dispose(); original.RefreshSystemTheme();
    }

    private static void VerifyCustomQuoteRetention(MainViewModel original, string directory)
    {
        var store = new SqliteFocusStore(Path.Combine(directory, "custom-quote-retention.db"));
        var existing = new[]
        {
            new FocusQuote("existing-one", "诗万首，酒千觞。几曾着眼看侯王？", "朱敦儒", "《鹧鸪天·西都作》", Custom: true),
            new FocusQuote("existing-two", "日拱一卒，功不唐捐。", Custom: true)
        };
        var webJson = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        store.SaveSetting("focus.media", JsonSerializer.Serialize(new MediaPreferences { CustomQuotes = existing.ToList() }, webJson));
        using (var media = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(existing, media.CustomQuotes);
            media.NewQuoteText = "我当然不会试图摘月，我要月亮奔我而来。";
            media.NewQuoteAuthor = "萨布丽娜（奥黛丽·赫本饰）"; media.NewQuoteWork = "《龙凤配》";
            media.SaveQuoteCommand.Execute(null);
            Assert.Equal(3, media.CustomQuotes.Count());
            media.Volume = 31;
        }
        using (var reloaded = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(3, reloaded.CustomQuotes.Count());
            Assert.All(existing, quote => Assert.Contains(quote, reloaded.CustomQuotes));
            reloaded.Quotes.Single(card => card.Quote.Id == existing[0].Id).Favorite = true;
        }
        using (var savedAgain = new FocusMediaViewModel(store, _ => { }))
            Assert.All(existing, quote => Assert.Contains(quote, savedAgain.CustomQuotes));
        original.RefreshSystemTheme();
    }

    private static void VerifyShortFocusRecords(MainWindow window, MainViewModel original, string directory)
    {
        var store = new SqliteFocusStore(Path.Combine(directory, "short-focus.db"));
        var clock = new ManualClock(); var model = new MainViewModel(store, new FocusTimer(clock));
        model.RestMinutes = "0"; model.SelectedRestrictionMode = model.RestrictionModes.Single(mode => mode.Strict);
        var notices = 0;
        model.ShortFocusDiscarded += (_, _) => notices++;
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromMilliseconds(9999)); model.EndCommand.Execute(null);
        Assert.Empty(store.GetRecords()); Assert.Empty(model.Records); Assert.False(model.HasUnsavedRecord);
        Assert.False(model.RestrictionsActive); Assert.Equal(1, notices); Assert.Contains("不足10秒", model.StatusMessage);
        model.Tick(); model.Tick(); Assert.Equal(1, notices);
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(5)); model.PauseCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(10)); model.EndCommand.Execute(null);
        Assert.Empty(store.GetRecords()); Assert.Equal(2, notices);
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(10)); model.EndCommand.Execute(null);
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(store.GetRecords()).FocusDuration); Assert.Equal(2, notices);
        model.SelectedTimerMode = model.TimerModes.Single(mode => mode.Mode == TimerMode.Countdown); model.DurationMinutes = "0.1";
        var rejected = 0;
        model.FocusDurationRejected += (_, _) => rejected++;
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(9)); model.Tick();
        Assert.Single(store.GetRecords()); Assert.Equal(2, notices); Assert.Equal(1, rejected); Assert.False(model.IsFocusActive);
        model.DurationMinutes = (10d / 60).ToString(System.Globalization.CultureInfo.CurrentCulture);
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(10)); model.Tick();
        Assert.Equal(2, store.GetRecords().Count); Assert.Equal(2, notices); Assert.False(model.HasUnsavedRecord);
        model.SelectedTimerMode = model.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
        model.PomodoroFocusMinutes = "0.16665";
        model.StartCommand.Execute(null);
        Assert.Equal(2, rejected); Assert.False(model.IsFocusActive);
        model.DefaultTimerMode = model.SelectedTimerMode;
        var priorPreference = store.GetSetting("timer.mode");
        model.SaveSettingsCommand.Execute(null);
        Assert.Equal(3, rejected); Assert.Equal(priorPreference, store.GetSetting("timer.mode"));
        Assert.Null(store.GetSetting("pomodoro.focusMinutes"));
        model.PomodoroFocusMinutes = (10d / 60).ToString(System.Globalization.CultureInfo.CurrentCulture);
        model.PomodoroShortRestMinutes = model.PomodoroLongRestMinutes = "0";
        model.PomodoroRounds = "1";
        model.SaveSettingsCommand.Execute(null);
        model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(10)); model.Tick();
        Assert.Equal(3, store.GetRecords().Count); Assert.Equal(3, rejected);
        model.DefaultTimerMode = model.TimerModes[0]; model.SaveSettingsCommand.Execute(null);
        model.SelectedTimerMode = model.TimerModes[0]; model.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True(model.PrepareExit()); Assert.Equal(3, store.GetRecords().Count); Assert.Equal(3, notices);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var notice = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single();
            Assert.Equal("本次专注未计入", ((TextBlock)notice.FindName("DeletionHeading")).Text);
            Assert.Equal(Visibility.Collapsed, ((Button)notice.FindName("ConfirmDeletionButton")).Visibility);
            Assert.Equal(Visibility.Visible, ((Button)notice.FindName("NoticeButton")).Visibility);
            notice.DialogResult = false;
        }));
        ConfirmDeletionWindow.ShowNotice(window, "本次有效专注不足10秒，未计入专注时长和记录。", "本次专注未计入");
        model.Media.Dispose(); original.RefreshSystemTheme();
    }

    private static async Task VerifyVideoBackgroundAsync(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock, string directory, string fixture)
    {
        var media = model.Media;
        var previous = media.SelectedBackground;
        var folder = Path.Combine(directory, fixture + "-folder"); Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "视频背景" + Path.GetExtension(fixture));
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), path);
        media.ImportFolder(folder);
        var card = media.Images.Single(image => image.Asset.File == path);
        Assert.True(card.IsVideo); Assert.Equal("视频循环", card.FormatLabel); Assert.Equal("自定义", card.Origin);
        var thumbnailLoading = card.LoadThumbnailAsync();
        Assert.Same(thumbnailLoading, card.LoadThumbnailAsync());
        await thumbnailLoading; Assert.NotNull(card.Thumbnail); Assert.True(card.Thumbnail.IsFrozen);
        Assert.Equal("", card.ThumbnailStatus);
        var cachedThumbnail = card.Thumbnail; await card.LoadThumbnailAsync(); Assert.Same(cachedThumbnail, card.Thumbnail);
        var preview = new Image { Source = card.Thumbnail, Width = 320, Height = 180, Stretch = Stretch.Uniform };
        preview.Measure(new Size(320, 180)); preview.Arrange(new Rect(0, 0, 320, 180));
        SavePreview(preview, Brushes.Black, "video-thumbnail" + Path.GetExtension(fixture) + ".png", 320, 180);
        card.Favorite = true;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0;
        window.UpdateLayout(); FlushBindings();
        var normal = (FocusBackgroundView)window.FindName("FocusBackground");
        var player = (MediaElement)normal.FindName("VideoPlayer");
        var loops = 0; Exception? error = null;
        RoutedEventHandler ended = (_, _) => loops++;
        EventHandler<ExceptionRoutedEventArgs> failed = (_, args) => error = args.ErrorException;
        player.MediaEnded += ended; player.MediaFailed += failed;
        media.SelectedBackground = card.Asset;
        model.SelectedTimerMode = model.TimerModes[0]; model.SelectedRestrictionMode = model.RestrictionModes[0]; model.RestMinutes = "0";
        model.StartCommand.Execute(null); window.UpdateLayout(); FlushBindings();
        await WaitForAsync(() => player.NaturalVideoWidth > 0 || error is not null);
        Assert.Null(error); Assert.True(media.HasBackground); Assert.Null(media.BackgroundGifUri);
        Assert.Equal(path, media.BackgroundVideoUri!.LocalPath);
        Assert.True(player.IsMuted); Assert.Equal(0, player.Volume); Assert.True(player.HasVideo); Assert.True(player.HasAudio);
        Assert.Equal(fixture.EndsWith(".mp4") ? 1920 : 640, player.NaturalVideoWidth);
        await WaitForAsync(() => loops >= 2 || error is not null); Assert.Null(error);
        model.PauseCommand.Execute(null);
        var beforePause = loops; await WaitForAsync(() => loops > beforePause); model.ResumeCommand.Execute(null);
        using (var restored = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(card.Asset.Id, restored.SelectedBackground.Id);
            Assert.Equal(path, restored.BackgroundVideoUri!.LocalPath);
            Assert.True(restored.Images.Single(image => image.Asset.Id == card.Asset.Id).Favorite);
        }
        clock.Advance(TimeSpan.FromSeconds(2)); model.Tick();
        ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var fullscreen = (FullscreenFocusView)window.FindName("FullscreenView");
        var full = (FocusBackgroundView)fullscreen.FindName("FullscreenBackground");
        var fullPlayer = (MediaElement)full.FindName("VideoPlayer");
        await WaitForAsync(() => fullPlayer.NaturalVideoWidth > 0);
        Assert.Null(player.Source); Assert.Equal(media.BackgroundVideoUri, full.VideoUri);
        Assert.True(fullPlayer.IsMuted); Assert.Equal(0, fullPlayer.Volume);
        Assert.Equal("00:00:02", model.TimerText);
        ((Button)fullscreen.FindName("ReturnToWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => player.Source is not null && player.NaturalVideoWidth > 0);
        Assert.Null(fullPlayer.Source);
        window.Hide(); FlushBindings(); Assert.Null(player.Source);
        window.Show(); window.UpdateLayout(); FlushBindings(); await WaitForAsync(() => player.Source is not null && player.NaturalVideoWidth > 0);
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1; FlushBindings(); Assert.Null(player.Source);
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0; window.UpdateLayout(); FlushBindings();
        await WaitForAsync(() => player.Source is not null && player.NaturalVideoWidth > 0);
        media.SelectedBackground = media.BackgroundChoices.Single(asset => asset.Id == "theme"); Assert.Equal(path, media.BackgroundVideoUri!.LocalPath);
        model.EndCommand.Execute(null); model.StartCommand.Execute(null); FlushBindings();
        Assert.Null(media.BackgroundVideoUri); Assert.Null(player.Source);
        model.EndCommand.Execute(null); media.RemoveImage(card); media.SelectedBackground = previous;
        player.MediaEnded -= ended; player.MediaFailed -= failed;
    }

    private static async Task VerifyDefaultVideoAsync(MainWindow window, MainViewModel model)
    {
        var media = model.Media; var previous = media.SelectedBackground;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0;
        var player = (MediaElement)((FocusBackgroundView)window.FindName("FocusBackground")).FindName("VideoPlayer");
        var loops = 0; Exception? error = null;
        RoutedEventHandler ended = (_, _) => loops++;
        EventHandler<ExceptionRoutedEventArgs> failed = (_, args) => error = args.ErrorException;
        player.MediaEnded += ended; player.MediaFailed += failed;
        try
        {
            media.SelectedBackground = media.Images.Single(image => image.Asset.Id == "clackamas-river").Asset;
            model.StartCommand.Execute(null); window.UpdateLayout(); FlushBindings();
            await WaitForAsync(() => player.NaturalVideoWidth > 0 || error is not null);
            Assert.Null(error); Assert.Equal(1920, player.NaturalVideoWidth); Assert.Equal(1080, player.NaturalVideoHeight);
            Assert.False(player.HasAudio); Assert.True(player.IsMuted);
            SavePreview((FrameworkElement)window.Content, window.Background, "default-video-focus.png");
            await WaitForAsync(() => loops > 0 || error is not null); Assert.Null(error);
        }
        finally
        {
            model.EndCommand.Execute(null); media.SelectedBackground = previous;
            player.MediaEnded -= ended; player.MediaFailed -= failed;
        }
    }

    private static async Task VerifyThumbnailLoadingAsync(MainViewModel model, string directory)
    {
        var card = new FocusImage(model.Media.Images.First(image => !image.IsVideo).Asset, () => { });
        var uiThread = Environment.CurrentManagedThreadId;
        var notified = false;
        card.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(FocusImage.Thumbnail)) return;
            Assert.Equal(uiThread, Environment.CurrentManagedThreadId);
            notified = true;
        };
        Assert.Null(card.Thumbnail);
        Assert.Equal("加载图片…", card.ThumbnailStatus);
        var loading = card.LoadThumbnailAsync();
        Assert.False(loading.IsCompleted);
        Assert.Same(loading, card.LoadThumbnailAsync());
        var responded = false;
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => responded = true, DispatcherPriority.Input);
        Assert.True(responded);
        await loading;
        Assert.True(notified); Assert.NotNull(card.Thumbnail); Assert.True(card.Thumbnail.IsFrozen);
        Assert.Equal("", card.ThumbnailStatus);
        var cached = card.Thumbnail;
        await card.LoadThumbnailAsync(); Assert.Same(cached, card.Thumbnail);
        var missing = new FocusImage(new FocusBloom.Desktop.ViewModels.FocusAsset("missing", "不可用图片", Path.Combine(directory, "missing.jpg")), () => { });
        await missing.LoadThumbnailAsync(); Assert.Null(missing.Thumbnail); Assert.Equal("图片不可用", missing.ThumbnailStatus);
        var missingVideo = new FocusImage(new FocusAsset("missing-video", "不可用视频", Path.Combine(directory, "missing.mp4")), () => { });
        await missingVideo.LoadThumbnailAsync(); Assert.Null(missingVideo.Thumbnail); Assert.Equal("视频预览不可用", missingVideo.ThumbnailStatus);
        await Task.WhenAll(model.Media.Images.Select(image => image.LoadThumbnailAsync()));
        Assert.NotNull(model.Media.Images.Single(image => image.Asset.Id == "clackamas-river").Thumbnail);
    }

    private static void VerifyFocusMedia(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, ManualClock clock, string directory)
    {
        var media = viewModel.Media;
        Assert.Equal(7, media.Images.Count);
        var defaultVideo = media.Images.Single(image => image.Asset.Id == "clackamas-river");
        Assert.Equal("默认", defaultVideo.Origin); Assert.True(defaultVideo.IsVideo);
        Assert.Equal("BLM Oregon & Washington", defaultVideo.Asset.Author); Assert.Equal("CC BY 2.0", defaultVideo.Asset.License);
        Assert.True(File.Exists(defaultVideo.Asset.File));
        var promotedId = "52fe0a3e-bc6a-4388-8dca-ecee5ea9bbf7";
        var existingPreferences = new SqliteFocusStore(Path.Combine(directory, "existing-media.db"));
        existingPreferences.SaveSetting("focus.media", JsonSerializer.Serialize(new
        {
            CustomQuotes = new[] { new { Id = promotedId, Text = "对未来的真正慷慨，是把一切献给现在。", Custom = true } },
            EditedDefaultQuotes = new[] { new { Id = "default-0", Text = "旧版修改", Custom = false } },
            RemovedQuoteIds = media.Quotes.Select(card => card.Quote.Id).ToArray(),
            RemovedImageIds = new[] { "lake" }, RemovedSoundIds = new[] { "rain" }
        }));
        using (var upgraded = new FocusMediaViewModel(existingPreferences, _ => { }))
        {
            Assert.Equal(25, upgraded.Quotes.Count); Assert.Empty(upgraded.CustomQuotes);
            Assert.Single(upgraded.Quotes, card => card.Quote.Id == promotedId);
            Assert.All(upgraded.Quotes, card => Assert.False(card.Custom));
            Assert.Equal(media.Quotes[0].Quote.Text, upgraded.Quotes[0].Quote.Text);
            Assert.Contains(upgraded.Images, image => image.Asset.Id == "lake");
            Assert.Contains(upgraded.Sounds, sound => sound.Asset.Id == "rain");
        }
        Assert.Equal(6, media.SoundChoices.Count);
        Assert.Equal(25, media.Quotes.Count);
        Assert.All(media.Images, image => Assert.Equal("默认", image.Origin));
        Assert.All(media.Sounds, sound => Assert.Equal("默认", sound.Origin));
        Assert.All(media.Quotes, quote => { Assert.Equal("默认", quote.Origin); Assert.NotEmpty(quote.Credit); });
        Assert.Equal("William Shakespeare", media.QuoteChoices.Single(quote => quote.Id == "default-2").Author);
        Assert.Equal("北岛", media.QuoteChoices.Single(quote => quote.Id == "default-7").Author);
        var tomorrow = media.QuoteChoices.Single(quote => quote.Id == "default-14");
        Assert.Equal("After all, tomorrow is another day.", tomorrow.Text);
        Assert.Equal("Margaret Mitchell", tomorrow.Author); Assert.Equal("Gone with the Wind", tomorrow.Work);
        var tomorrowCredit = new QuoteAttributionText { Quote = tomorrow };
        Assert.Equal(FontStyles.Italic, tomorrowCredit.Inlines.OfType<System.Windows.Documents.Run>().Single(run => run.Text == tomorrow.Work).FontStyle);
        Assert.Equal("《颂歌集》", media.QuoteChoices.Single(quote => quote.Id == "default-9").Work);
        Assert.All(media.Images.Where(image => !image.IsVideo), image => Assert.NotNull(image.Thumbnail));
        Assert.All(media.SoundChoices.Where(sound => sound.File.Length > 0), sound => Assert.True(File.Exists(sound.File)));
        var localFolder = Path.Combine(directory, "pictures");
        Directory.CreateDirectory(localFolder);
        var importedPath = Path.Combine(localFolder, "my-background.jpg");
        File.Copy(media.Images[0].Asset.File, importedPath);
        media.ImportFolder(localFolder);
        Assert.Equal("自定义", media.Images.Single(image => image.Asset.File == importedPath).Origin);
        var invalidPath = Path.Combine(directory, "invalid.png");
        File.WriteAllText(invalidPath, "not an image");
        media.ImportImages([invalidPath]);
        Assert.DoesNotContain(media.Images, image => image.Asset.File == invalidPath);

        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 4;
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1240, 750)); content.Arrange(new Rect(0, 0, 1240, 750)); content.UpdateLayout(); FlushBindings();
        var settings = (FocusMediaSettingsView)((Border)window.FindName("FocusMediaSettingsCard")).Child;
        Assert.Same(media, settings.DataContext);
        var backgroundLibrary = (Expander)settings.FindName("BackgroundLibrary");
        var quoteLibrary = (Expander)settings.FindName("QuoteLibrary");
        var soundLibrary = (Expander)settings.FindName("SoundLibrary");
        Assert.False(backgroundLibrary.IsExpanded); Assert.False(quoteLibrary.IsExpanded); Assert.False(soundLibrary.IsExpanded);
        SavePreview(content, window.Background, "focus-library-collapsed.png");
        ((System.Windows.Controls.Primitives.ToggleButton)VisualTreeHelper.GetChild((StackPanel)VisualTreeHelper.GetChild(backgroundLibrary, 0), 0)).IsChecked = true;
        content.UpdateLayout(); FlushBindings(); Assert.True(backgroundLibrary.IsExpanded);
        FindButtons((ItemsControl)settings.FindName("BackgroundCards")).Single(button => button.Content?.ToString() == "使用" && button.DataContext is FocusImage image && image.Asset.File == importedPath).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FlushBindings();
        Assert.Equal(importedPath, media.SelectedBackground.File);
        media.NewQuoteText = "读完这一页，再走下一步。"; media.NewQuoteAuthor = "我的摘录"; media.NewQuoteWork = "学习手记";
        media.NewQuoteTranslator = "译者"; media.NewQuoteSource = "个人记录"; media.SaveQuoteCommand.Execute(null);
        var quote = Assert.Single(media.CustomQuotes);
        Assert.Equal("自定义", media.Quotes.Single(card => card.Quote.Id == quote.Id).Origin);
        backgroundLibrary.IsExpanded = false; quoteLibrary.IsExpanded = true; content.UpdateLayout(); FlushBindings();
        FindButtons((ItemsControl)settings.FindName("QuoteCards")).Single(button => button.Content?.ToString() == "使用" && button.DataContext is FocusQuoteCard card && card.Quote.Id == quote.Id).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        media.Images[0].Favorite = true;
        media.Images[1].Excluded = true;
        media.Volume = 23; media.Muted = true;
        media.SelectedSound = media.SoundChoices.Single(sound => sound.Id == "rain");
        FlushBindings(); content.UpdateLayout();
        SavePreview(content, window.Background, "focus-quotes-library.png");
        quoteLibrary.IsExpanded = false; soundLibrary.IsExpanded = true; content.UpdateLayout(); FlushBindings();
        FindButtons((ItemsControl)settings.FindName("SoundCards")).Single(button => button.Content?.ToString() == "使用" && button.DataContext is FocusSoundCard card && card.Asset.Id == "rain").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        SavePreview(content, window.Background, "focus-sounds-library.png");
        soundLibrary.IsExpanded = false; backgroundLibrary.IsExpanded = true; content.UpdateLayout(); FlushBindings();
        SavePreview(content, window.Background, "focus-media-settings.png");
        using (var restored = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.Equal(importedPath, restored.SelectedBackground.File);
            Assert.Equal(quote.Id, restored.SelectedQuote.Id);
            Assert.Contains("学习手记", restored.SelectedQuote.Attribution);
            Assert.Equal("rain", restored.SelectedSound.Id);
            Assert.Equal(23, restored.Volume); Assert.True(restored.Muted);
            Assert.True(restored.Images[0].Favorite); Assert.True(restored.Images[1].Excluded);
        }
        VerifyMediaScrollAndTheme(window, viewModel, settings, content);

        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0;
        viewModel.SelectedTimerMode = viewModel.TimerModes[0]; viewModel.RestMinutes = "0";
        viewModel.StartCommand.Execute(null);
        Assert.Equal(quote.Text, media.QuoteText);
        var image = media.Background;
        var imageId = media.CurrentBackgroundId;
        media.SelectedBackground = media.BackgroundChoices.Single(asset => asset.Id == "theme");
        media.SelectedQuote = media.QuoteChoices[0];
        media.EditQuote(quote); media.NewQuoteText = "下一次的新句子"; media.SaveQuoteCommand.Execute(null);
        Assert.Same(image, media.Background); Assert.Equal(quote.Text, media.QuoteText);
        var normalKeepOn = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("FocusKeepScreenOnButton");
        normalKeepOn.IsChecked = true;
        FlushBindings();
        Assert.True(viewModel.KeepScreenOn);
        ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("FocusSoundPanelButton")).IsChecked = true;
        FlushBindings();
        Assert.Same(media, ((FocusAudioControls)((Border)((System.Windows.Controls.Primitives.Popup)window.FindName("FocusSoundPopup")).Child).Child).DataContext);
        VerifySoundScrolling((FocusAudioControls)((Border)((System.Windows.Controls.Primitives.Popup)window.FindName("FocusSoundPopup")).Child).Child, media, "sound-selector-window.png");
        ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("FocusSoundPanelButton")).IsChecked = false;
        var hasAudioDevice = NAudio.Wave.WaveOut.DeviceCount > 0;
        if (hasAudioDevice) { media.ToggleAudioCommand.Execute(null); Assert.True(media.IsPlaying); }
        viewModel.PauseCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(10)); viewModel.Tick();
        Assert.Same(image, media.Background); Assert.Equal(imageId, media.CurrentBackgroundId);
        if (hasAudioDevice) Assert.True(media.IsPlaying);
        viewModel.ResumeCommand.Execute(null);
        ((Button)window.FindName("EnterFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Same(image, media.Background);
        var fullscreen = (FullscreenFocusView)window.FindName("FullscreenView");
        var fullscreenKeepOn = (System.Windows.Controls.Primitives.ToggleButton)fullscreen.FindName("FullscreenKeepScreenOnButton");
        Assert.True(fullscreenKeepOn.IsChecked);
        fullscreenKeepOn.IsChecked = false;
        FlushBindings();
        Assert.False(viewModel.KeepScreenOn);
        Assert.False(normalKeepOn.IsChecked);
        ((System.Windows.Controls.Primitives.ToggleButton)fullscreen.FindName("SoundPanelButton")).IsChecked = true;
        FlushBindings();
        Assert.Same(media, ((FocusAudioControls)((Border)((System.Windows.Controls.Primitives.Popup)fullscreen.FindName("SoundPopup")).Child).Child).DataContext);
        VerifySoundScrolling((FocusAudioControls)((Border)((System.Windows.Controls.Primitives.Popup)fullscreen.FindName("SoundPopup")).Child).Child, media, "sound-selector-fullscreen.png");
        ((System.Windows.Controls.Primitives.ToggleButton)fullscreen.FindName("SoundPanelButton")).IsChecked = false;
        ((Button)fullscreen.FindName("ReturnToWindowButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        viewModel.EndCommand.Execute(null);
        viewModel.StartCommand.Execute(null);
        Assert.Null(media.Background);
        Assert.NotEqual(quote.Text, media.QuoteText);
        viewModel.EndCommand.Execute(null);
        media.RemoveQuote(media.CustomQuotes.Single());
        var quoteFile = Path.Combine(directory, "quotes.txt"); File.WriteAllLines(quoteFile, ["第一句", "第二句"]);
        media.ImportQuotes(quoteFile); Assert.Equal(2, media.CustomQuotes.Count());
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var legacyQuoteFile = Path.Combine(directory, "legacy-quotes.txt");
        File.WriteAllBytes(legacyQuoteFile, Encoding.GetEncoding("GB18030").GetBytes("山重水复疑无路\r\n柳暗花明又一村"));
        media.ImportQuotes(legacyQuoteFile);
        Assert.Contains(media.CustomQuotes, quote => quote.Text == "山重水复疑无路");
        Assert.Contains(media.CustomQuotes, quote => quote.Text == "柳暗花明又一村");
        var bomQuoteFile = Path.Combine(directory, "bom-quotes.txt"); File.WriteAllText(bomQuoteFile, "带标记的句子", new UTF8Encoding(true));
        media.ImportQuotes(bomQuoteFile); Assert.Contains(media.CustomQuotes, quote => quote.Text == "带标记的句子");
        var lockedQuoteFile = Path.Combine(directory, "locked-quotes.txt"); File.WriteAllText(lockedQuoteFile, "无法读取的句子");
        using (new FileStream(lockedQuoteFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var beforeLocked = media.CustomQuotes.Count();
            media.ImportQuotes(lockedQuoteFile);
            Assert.Equal(beforeLocked, media.CustomQuotes.Count());
            Assert.Contains("无法读取文字列表", viewModel.StatusMessage);
        }
        media.SelectedBackground = media.BackgroundChoices[0]; media.FavoritesOnly = false;
        media.SelectedQuote = media.QuoteChoices[0];
        viewModel.StartCommand.Execute(null);
        var randomImage = media.CurrentBackgroundId; var randomQuote = media.CurrentQuoteId;
        viewModel.EndCommand.Execute(null); viewModel.StartCommand.Execute(null);
        Assert.NotEqual(randomImage, media.CurrentBackgroundId); Assert.NotEqual(randomQuote, media.CurrentQuoteId);
        viewModel.EndCommand.Execute(null);
        foreach (var item in media.Images) item.Excluded = true;
        viewModel.StartCommand.Execute(null); Assert.Null(media.Background); viewModel.EndCommand.Execute(null);
        foreach (var item in media.Images) item.Excluded = false;
        if (hasAudioDevice)
        {
            FlushBindings();
            Assert.True(media.IsPlaying, $"声音应持续播放：{viewModel.StatusMessage}；音源：{media.SelectedSound.Id}");
            media.ToggleAudioCommand.Execute(null); Assert.False(media.IsPlaying);
        }
        if (hasAudioDevice) { media.ToggleAudioCommand.Execute(null); Assert.True(media.IsPlaying); }
        var removedSound = media.Sounds.Single(sound => sound.Asset.Id == "rain");
        removedSound.Favorite = true;
        media.ShowFavoriteSounds = true; Assert.Single(media.VisibleSounds);
        media.RemoveSound(removedSound); Assert.Contains(removedSound, media.Sounds);
        var customSoundPath = Path.Combine(directory, "my-rain.mp3"); File.Copy(removedSound.Asset.File, customSoundPath);
        media.ImportSound(customSoundPath); var customSound = media.Sounds.Single(card => card.Asset.File == customSoundPath);
        media.RemoveSound(customSound); Assert.False(media.IsPlaying); Assert.True(media.NoSound);
        var favoriteQuote = media.Quotes.First(card => !card.Custom);
        favoriteQuote.Favorite = true; media.ShowFavoriteQuotes = true; Assert.Single(media.VisibleQuotes);
        media.QuoteFavoritesOnly = true; media.SelectedQuote = media.QuoteChoices[0];
        viewModel.StartCommand.Execute(null); Assert.Equal(favoriteQuote.Text, media.QuoteText);
        media.RemoveQuote(favoriteQuote.Quote); Assert.Contains(favoriteQuote, media.Quotes);
        favoriteQuote.Excluded = true; Assert.Equal(favoriteQuote.Text, media.QuoteText);
        viewModel.EndCommand.Execute(null); viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.IsFocusActive); Assert.Equal("", media.QuoteText); viewModel.EndCommand.Execute(null);
        var removedImage = media.Images.Single(image => image.Asset.File == importedPath);
        media.RemoveImage(removedImage); Assert.True(File.Exists(importedPath));
        var savedFavoriteQuote = media.Quotes.First(card => !card.Custom);
        savedFavoriteQuote.Favorite = true;
        media.Sounds.Single(sound => sound.Asset.Id == "brown").Favorite = true;
        using (var restored = new FocusMediaViewModel(store, _ => { }))
        {
            Assert.DoesNotContain(restored.Images, image => image.Asset.File == importedPath);
            Assert.Contains(restored.Sounds, sound => sound.Asset.Id == "rain");
            Assert.DoesNotContain(restored.Sounds, sound => sound.Asset.File == customSoundPath);
            Assert.True(restored.Quotes.Single(card => card.Quote.Id == favoriteQuote.Quote.Id).Excluded);
            Assert.True(restored.Quotes.Single(card => card.Quote.Id == savedFavoriteQuote.Quote.Id).Favorite);
            Assert.True(restored.Sounds.Single(sound => sound.Asset.Id == "brown").Favorite);
        }
        media.RestoreSounds(); media.RestoreQuotes(); media.RestoreImages();
        Assert.Equal(5, media.Sounds.Count); Assert.Equal(25, media.Quotes.Count(card => !card.Custom));
        foreach (var card in media.Quotes.ToArray()) { if (card.Custom) media.RemoveQuote(card.Quote); else card.Excluded = true; }
        viewModel.StartCommand.Execute(null); Assert.Equal("", media.QuoteText); viewModel.EndCommand.Execute(null);
        foreach (var card in media.Quotes) card.Excluded = false;
        media.QuoteFavoritesOnly = false; media.ShowFavoriteQuotes = false; media.ShowFavoriteSounds = false;
        var originalQuote = media.QuoteChoices.Single(item => item.Id == "default-2");
        media.EditQuote(originalQuote); Assert.Equal("", media.NewQuoteText);
        media.RemoveQuote(originalQuote); Assert.Contains(originalQuote, media.QuoteChoices);
        media.RemoveImage(media.Images[0]); Assert.Equal(7, media.Images.Count);
        media.RemoveSound(media.Sounds[0]); Assert.Equal(5, media.Sounds.Count);
        quoteLibrary.IsExpanded = true; backgroundLibrary.IsExpanded = false; soundLibrary.IsExpanded = false;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 4;
        content.UpdateLayout(); FlushBindings();
        Assert.All(FindButtons((ItemsControl)settings.FindName("QuoteCards")).Where(button => button.Content?.ToString() is "编辑" or "删除" && button.DataContext is FocusQuoteCard { Custom: false }), button => Assert.Equal(Visibility.Collapsed, button.Visibility));
        var styled = new QuoteAttributionText { Quote = originalQuote };
        Assert.Equal(FontStyles.Italic, styled.Inlines.OfType<System.Windows.Documents.Run>().Single(run => run.Text == "The Tempest").FontStyle);
        var numbered = new QuoteAttributionText { Quote = new FocusQuote("check-numbered", "文本", "George Orwell", "1984") };
        Assert.Equal(FontStyles.Italic, numbered.Inlines.OfType<System.Windows.Documents.Run>().Single(run => run.Text == "1984").FontStyle);
        var chinese = new QuoteAttributionText { Quote = media.QuoteChoices.Single(item => item.Id == "default-0") };
        Assert.Equal(FontStyles.Normal, chinese.Inlines.OfType<System.Windows.Documents.Run>().Single(run => run.Text == "《病隙碎笔》").FontStyle);
        media.NewQuoteText = "可以编辑的个人句子"; media.NewQuoteAuthor = "我的署名"; media.NewQuoteWork = "My Notebook";
        media.SaveQuoteCommand.Execute(null);
        var personal = media.CustomQuotes.Single();
        content.UpdateLayout(); FlushBindings();
        var page = (ScrollViewer)window.FindName("PreferencesScrollViewer");
        var cardObject = media.Quotes.Single(card => card.Quote.Id == personal.Id);
        var personalItem = (FrameworkElement)((ItemsControl)settings.FindName("QuoteCards")).ItemContainerGenerator.ContainerFromItem(cardObject);
        personalItem.BringIntoView(); content.UpdateLayout(); FlushBindings();
        var priorOffset = page.VerticalOffset;
        FindButtons((ItemsControl)settings.FindName("QuoteCards")).Single(button => button.Content?.ToString() == "编辑" && button.DataContext == cardObject).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        FlushBindings();
        media.NewQuoteText = "修改后的个人句子"; media.SaveQuoteCommand.Execute(null); content.UpdateLayout(); FlushBindings();
        Assert.InRange(Math.Abs(page.VerticalOffset - priorOffset), 0, 2);
        using (var restored = new FocusMediaViewModel(store, _ => { })) Assert.Equal("修改后的个人句子", restored.CustomQuotes.Single().Text);
        void AnswerDeletion(bool approve) => Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var dialog = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single();
            if (approve) ((Button)dialog.FindName("ConfirmDeletionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else dialog.DialogResult = false;
        }));
        AnswerDeletion(false); Assert.False(ConfirmDeletionWindow.Ask(window, "取消删除验证"));
        content.UpdateLayout(); FlushBindings();
        var delete = FindButtons((ItemsControl)settings.FindName("QuoteCards")).Single(button => button.Content?.ToString() == "删除" && button.DataContext is FocusQuoteCard card && card.Quote.Id == personal.Id);
        AnswerDeletion(false); delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Single(media.CustomQuotes);
        AnswerDeletion(true); delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Empty(media.CustomQuotes);
        media.Dispose();
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    private static void VerifyMediaScrollAndTheme(MainWindow window, MainViewModel viewModel, FocusMediaSettingsView settings, FrameworkElement content)
    {
        var page = (ScrollViewer)window.FindName("PreferencesScrollViewer");
        viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
        var galleries = new[] { ("BackgroundLibrary", "BackgroundCards"), ("QuoteLibrary", "QuoteCards"), ("SoundLibrary", "SoundCards") };
        foreach (var (sectionName, cardsName) in galleries)
        {
            foreach (var (name, _) in galleries) ((Expander)settings.FindName(name)).IsExpanded = name == sectionName;
            page.ScrollToTop(); content.UpdateLayout(); FlushBindings();
            var cards = (ItemsControl)settings.FindName(cardsName);
            Assert.Empty(Descendants<ScrollViewer>(cards));
            var favoriteControls = Descendants<CheckBox>(settings).Where(box => box.IsVisible && box.Content?.ToString()?.Contains("收藏") == true).ToArray();
            Assert.NotEmpty(favoriteControls);
            Assert.All(favoriteControls, box => Assert.Equal(((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, ((SolidColorBrush)box.Foreground).Color));
            var firstCard = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(0);
            var finalCard = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(cards.Items.Count - 1);
            if (cardsName != "BackgroundCards")
            {
                var secondCard = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(1);
                var thirdCard = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(2);
                var nextRow = (FrameworkElement)cards.ItemContainerGenerator.ContainerFromIndex(3);
                Assert.Equal(firstCard.TransformToAncestor(cards).Transform(new Point()).Y, thirdCard.TransformToAncestor(cards).Transform(new Point()).Y, 3);
                Assert.True(secondCard.TransformToAncestor(cards).Transform(new Point()).X > firstCard.TransformToAncestor(cards).Transform(new Point()).X);
                Assert.True(nextRow.TransformToAncestor(cards).Transform(new Point()).Y > firstCard.TransformToAncestor(cards).Transform(new Point()).Y);
            }
            finalCard.BringIntoView(); content.UpdateLayout(); FlushBindings();
            var finalBounds = finalCard.TransformToAncestor(page).TransformBounds(new Rect(finalCard.RenderSize));
            Assert.True(finalBounds.Bottom <= page.ViewportHeight + 1, "素材最后一行应能完整滚入页面。");
            var before = page.VerticalOffset;
            finalCard.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
            FlushBindings(); Assert.True(page.VerticalOffset < before);
            var afterUp = page.VerticalOffset;
            finalCard.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
            FlushBindings(); Assert.True(page.VerticalOffset > afterUp);
            page.ScrollToTop(); content.UpdateLayout(); FlushBindings();
            SavePreview(content, window.Background, cardsName + "-dark.png");
        }
        foreach (var (name, _) in galleries) ((Expander)settings.FindName(name)).IsExpanded = false;
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 1;
        content.UpdateLayout(); FlushBindings();
        Assert.InRange(((FrameworkElement)window.FindName("NewFolderArea")).ActualHeight, 0, 40);
        SavePreview(content, window.Background, "projects-compact-dark.png");
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 4;
        viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Light);
        Assert.All(favoriteCheckBoxes(settings), box => Assert.Equal(((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color, ((SolidColorBrush)box.Foreground).Color));
        static IEnumerable<CheckBox> favoriteCheckBoxes(DependencyObject root) => Descendants<CheckBox>(root).Where(box => box.Content?.ToString()?.Contains("收藏") == true);
    }

    private static void VerifyKeepAwake(MainViewModel viewModel, ManualClock clock, SqliteFocusStore store)
    {
        using var power = new KeepAwakeService(viewModel);
        viewModel.KeepScreenOn = true;
        Assert.False(power.Active);
        viewModel.StartCommand.Execute(null);
        Assert.True(power.Active);
        viewModel.PauseCommand.Execute(null);
        Assert.True(power.Active);
        viewModel.KeepScreenOn = false;
        Assert.False(power.Active);
        viewModel.KeepScreenOn = true;
        viewModel.ResumeCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.EndCommand.Execute(null);
        Assert.False(power.Active);
        Assert.Equal("True", store.GetSetting("power.keepScreenOn"));
        viewModel.KeepScreenOn = false;
    }

    private static void VerifyRestrictionSnapshots(MainViewModel viewModel, SqliteFocusStore store)
    {
        Assert.Null(viewModel.Whitelist.UpsertWebsite("example.edu", true));
        viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
        viewModel.StartCommand.Execute(null);
        Assert.True(viewModel.RestrictionsActive);
        Assert.Contains(viewModel.SessionWebsites, website => website.Domain == "example.edu");
        viewModel.PauseCommand.Execute(null);
        Assert.True(viewModel.RestrictionsActive);
        viewModel.EndCommand.Execute(null);
        Assert.False(viewModel.RestrictionsActive);
        viewModel.Whitelist.WebsiteMode = viewModel.Whitelist.ListModes.Single(mode => mode.Mode == AccessListMode.Blacklist);
        Assert.Empty(viewModel.Whitelist.Websites);
        Assert.Null(viewModel.Whitelist.UpsertWebsite("distraction.test", true));
        var restoredRules = new RestrictionSettingsViewModel(store, _ => { });
        Assert.Equal(AccessListMode.Blacklist, restoredRules.WebsiteMode.Mode);
        Assert.Contains(restoredRules.Websites, website => website.Domain == "distraction.test");
        restoredRules.WebsiteMode = restoredRules.ListModes[0];
        Assert.Contains(restoredRules.Websites, website => website.Domain == "example.edu");
        viewModel.Whitelist.ApplicationMode = viewModel.Whitelist.ListModes.Single(mode => mode.Mode == AccessListMode.Blacklist);
        viewModel.Whitelist.AddApplication(new AllowedApplication("浏览器", "C:\\Chrome\\chrome.exe"));
        viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
        viewModel.StartCommand.Execute(null);
        Assert.Equal(AccessListMode.Blacklist, viewModel.SessionWebsiteMode);
        Assert.True(viewModel.CanNavigateWebsite("https://example.edu/"));
        Assert.False(viewModel.CanNavigateWebsite("https://distraction.test/"));
        Assert.False(ApplicationAccess.IsAllowed(2, 1, "chrome", "C:\\Chrome\\chrome.exe", viewModel.SessionApplications, viewModel.SessionApplicationMode));
        viewModel.Whitelist.WebsiteMode = viewModel.Whitelist.ListModes[0];
        Assert.Equal(AccessListMode.Blacklist, viewModel.SessionWebsiteMode);
        Assert.False(viewModel.CanNavigateWebsite("https://distraction.test/"));
        viewModel.EndCommand.Execute(null);
        Assert.Contains(viewModel.Whitelist.Websites, website => website.Domain == "example.edu");
        viewModel.Whitelist.ApplicationMode = viewModel.Whitelist.ListModes[0];
        Assert.Empty(viewModel.Whitelist.Applications);
        viewModel.DefaultRestrictionMode = viewModel.RestrictionModes[0];
        viewModel.SaveSettingsCommand.Execute(null);
        viewModel.SelectedRestrictionMode = viewModel.RestrictionModes[0];
    }
    private static void VerifyTimerFloat(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock)
    {
        var oldAppearance = model.SelectedAppearanceMode;
        var mainTopmost = window.Topmost;
        model.SelectedTimerMode = model.TimerModes[0]; model.RestMinutes = "0";
        model.SelectedRestrictionMode = model.RestrictionModes.Single(mode => mode.Strict);
        model.Media.SelectedBackground = model.Media.Images.First(image => !image.Custom && !image.IsVideo && !image.IsGif).Asset;
        var count = store.GetRecords().Count;
        ((CheckBox)window.FindName("TimerFloatToggle")).IsChecked = true;
        FlushBindings();
        var floating = Application.Current.Windows.OfType<TimerFloatWindow>().Single();
        Assert.True(floating.IsVisible); Assert.Null(floating.Owner); Assert.False(floating.ShowInTaskbar);
        Assert.Same(model, floating.DataContext); Assert.True(floating.Topmost);
        Assert.Equal("True", store.GetSetting("widget.timer.visible"));
        var start = (Button)floating.FindName("FloatStartButton");
        start.Command.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(15)); model.Tick(); FlushBindings();
        Assert.Equal(model.TimerText, ((TextBlock)floating.FindName("FloatTimer")).Text);
        Assert.Equal("00:00:15", model.TimerText);
        VerifyFloatingActions(floating);
        var quote = model.Media.CurrentQuote; var background = model.Media.Background;
        var ownWindow = ForegroundApplications.FromWindow(new WindowInteropHelper(floating).Handle)!;
        Assert.True(ApplicationAccess.IsAllowed(ownWindow.ProcessId, Environment.ProcessId, ownWindow.ProcessName,
            ownWindow.ExecutablePath, model.SessionApplications, model.SessionApplicationMode));
        ((Button)floating.FindName("FloatPauseButton")).Command.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(2)); model.Tick(); FlushBindings();
        Assert.Equal(FocusState.Paused, model.State); Assert.True(model.RestrictionsActive);
        Assert.Same(quote, model.Media.CurrentQuote); Assert.Same(background, model.Media.Background);
        Assert.Equal(Visibility.Visible, ((Button)floating.FindName("FloatResumeButton")).Visibility);
        VerifyFloatingActions(floating);
        window.Hide(); clock.Advance(TimeSpan.FromSeconds(1)); model.Tick(); FlushBindings();
        Assert.True(floating.IsVisible); Assert.False(window.IsVisible);
        Assert.Equal(model.PhaseText, ((TextBlock)floating.FindName("FloatPhase")).Text);
        ((Button)floating.FindName("OpenMainButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(window.IsVisible);
        var fullscreen = (Button)window.FindName("EnterFullscreenButton");
        fullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(floating.IsVisible); Assert.True(model.TimerFloatVisible);
        fullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(floating.IsVisible); Assert.Equal(FocusState.Paused, model.State);
        floating.Width = 240; floating.Height = 180;
        model.TimerFloatUseBackground = false;
        model.SelectedAppearanceMode = model.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Light);
        floating.UpdateLayout(); FlushBindings();
        Assert.Equal(Visibility.Collapsed, ((FocusBackgroundView)floating.FindName("FloatBackground")).Visibility);
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource("TextBrush")).Color,
            ((SolidColorBrush)((TextBlock)floating.FindName("FloatTimer")).Foreground).Color);
        AssertVisibleInViewport((Button)floating.FindName("FloatResumeButton"), (FrameworkElement)floating.Content);
        AssertVisibleInViewport((Button)floating.FindName("OpenMainButton"), (FrameworkElement)floating.Content);
        SavePreview((FrameworkElement)floating.Content, floating.Background, "timer-float-light.png", 240, 180);
        model.SelectedAppearanceMode = model.AppearanceModes.Single(mode => mode.Mode == AppearanceMode.Dark);
        FlushBindings();
        Assert.Equal(((SolidColorBrush)Application.Current.FindResource("CardBrush")).Color,
            ((SolidColorBrush)((Border)floating.FindName("FloatSurface")).Background).Color);
        SavePreview((FrameworkElement)floating.Content, floating.Background, "timer-float-dark.png", 240, 180);
        ((MenuItem)floating.FindName("CurrentBackgroundItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        FlushBindings();
        Assert.True(model.TimerFloatUseBackground);
        Assert.Same(background, ((FocusBackgroundView)floating.FindName("FloatBackground")).BackgroundImage);
        Assert.Equal(Visibility.Visible, ((FocusBackgroundView)floating.FindName("FloatBackground")).Visibility);
        Assert.Equal(Colors.White, ((SolidColorBrush)((TextBlock)floating.FindName("FloatTimer")).Foreground).Color);
        Assert.Same(background, model.Media.Background); Assert.Same(quote, model.Media.CurrentQuote);
        Assert.Equal(new Thickness(0), ((Border)floating.FindName("FloatSurface")).BorderThickness);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)floating.Background).Color);
        Assert.Null(floating.FindName("LockButton")); Assert.Null(floating.FindName("PinButton"));
        SavePreview((FrameworkElement)floating.Content, floating.Background, "timer-float-image.png", 240, 180);
        ((Button)floating.FindName("OptionsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(((MenuItem)floating.FindName("CurrentBackgroundItem")).IsChecked);
        var transparencySlider = (Slider)floating.FindName("TransparencySlider");
        transparencySlider.Value = 45; FlushBindings();
        Assert.Equal(45, model.TimerFloatTransparency);
        Assert.True(floating.AllowsTransparency); Assert.Equal(0.55, floating.Opacity);
        var transparentFrame = new RenderTargetBitmap((int)floating.ActualWidth, (int)floating.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        transparentFrame.Render(floating);
        var pixel = new byte[4];
        transparentFrame.CopyPixels(new Int32Rect((int)floating.ActualWidth / 2, 5, 1, 1), pixel, 4, 0);
        Assert.InRange(pixel[3], (byte)138, (byte)142);
        transparentFrame.CopyPixels(new Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[3]);
        ((Button)floating.FindName("OptionsButton")).ContextMenu.IsOpen = false;
        floating.Width = 360; floating.Height = 260;
        floating.Left += 24; floating.Top -= 30;
        floating.FitToScreen();
        ((MenuItem)floating.FindName("PinItem")).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.False(floating.Topmost); Assert.Equal(mainTopmost, window.Topmost);
        Assert.Equal(ResizeMode.CanResize, floating.ResizeMode);
        var savedLayout = floating.CaptureLayout();
        Assert.Equal(savedLayout, JsonSerializer.Deserialize<TimerFloatLayout>(store.GetSetting(TimerFloatLayout.SettingKey)!));
        var reloaded = new MainViewModel(store);
        Assert.True(reloaded.TimerFloatVisible); Assert.True(reloaded.TimerFloatUseBackground);
        Assert.Equal(45, reloaded.TimerFloatTransparency);
        var oldLayout = System.Text.Json.Nodes.JsonNode.Parse(store.GetSetting(TimerFloatLayout.SettingKey)!)!.AsObject();
        oldLayout["PositionLocked"] = true;
        store.SaveSetting(TimerFloatLayout.SettingKey, oldLayout.ToJsonString());
        var restored = new TimerFloatWindow(model, store, window.ReturnToFocus);
        new WindowInteropHelper(restored).EnsureHandle();
        Assert.Equal(savedLayout, restored.CaptureLayout());
        Assert.Equal(0.55, restored.Opacity);
        Assert.Equal(ResizeMode.CanResize, restored.ResizeMode);
        restored.CloseForExit(); Assert.True(model.TimerFloatVisible);
        var restoredMain = new MainWindow(reloaded, store);
        new WindowInteropHelper(restoredMain).EnsureHandle();
        Assert.True(Application.Current.Windows.OfType<TimerFloatWindow>().Single(item => item.DataContext == reloaded).IsVisible);
        restoredMain.Close();
        Assert.DoesNotContain(Application.Current.Windows.OfType<TimerFloatWindow>(), item => item.DataContext == reloaded);
        Assert.True(reloaded.TimerFloatVisible); Assert.Equal("True", store.GetSetting("widget.timer.visible"));
        floating.Close(); Assert.False(floating.IsVisible); Assert.False(model.TimerFloatVisible);
        Assert.Equal(FocusState.Paused, model.State); Assert.True(model.RestrictionsActive);
        model.TimerFloatVisible = true; Assert.True(floating.IsVisible);
        ((Button)floating.FindName("FloatResumeButton")).Command.Execute(null);
        ((Button)floating.FindName("FloatEndButton")).Command.Execute(null);
        Assert.Equal(count + 1, store.GetRecords().Count);
        Assert.Equal(TimeSpan.FromSeconds(15), store.GetRecords().First().FocusDuration);
        var imageSelection = model.Media.SelectedBackground;
        model.Media.SelectedBackground = model.Media.Images.Single(image => image.Asset.Id == "clackamas-river").Asset;
        RunWithDispatcher(async () =>
        {
            var player = (MediaElement)((FocusBackgroundView)floating.FindName("FloatBackground")).FindName("VideoPlayer");
            await WaitForAsync(() => player.HasVideo && player.Position > TimeSpan.FromMilliseconds(300));
            var frame = new RenderTargetBitmap((int)player.ActualWidth, (int)player.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            frame.Render(player);
            var colors = new byte[frame.PixelWidth * frame.PixelHeight * 4];
            frame.CopyPixels(colors, frame.PixelWidth * 4, 0);
            Assert.True(colors.Where((_, index) => index % 4 != 3).Any(value => value > 40), "透明浮窗中应能显示视频画面。");
            SavePreview((FrameworkElement)floating.Content, floating.Background, "timer-float-video.png", (int)floating.ActualWidth, (int)floating.ActualHeight);
        });
        model.Media.SelectedBackground = imageSelection;
        model.SelectedTimerMode = model.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
        model.PomodoroFocusMinutes = "0.2"; model.PomodoroShortRestMinutes = "0.1"; model.PomodoroRounds = "2";
        start.Command.Execute(null); clock.Advance(TimeSpan.FromSeconds(12)); model.Tick(); FlushBindings();
        Assert.Equal(FocusState.Resting, model.State); Assert.False(model.RestrictionsActive);
        Assert.Equal("00:00:06", ((TextBlock)floating.FindName("FloatTimer")).Text);
        var skip = (Button)floating.FindName("FloatSkipRestButton"); Assert.Equal(Visibility.Visible, skip.Visibility);
        VerifyFloatingActions(floating);
        AssertVisibleInViewport(skip, (FrameworkElement)floating.Content);
        skip.Command.Execute(null); Assert.Contains("第 2 / 2 轮", model.PhaseText);
        clock.Advance(TimeSpan.FromSeconds(12)); model.Tick(); FlushBindings();
        Assert.Equal(FocusState.Ended, model.State); Assert.Equal(count + 3, store.GetRecords().Count);
        ((Button)floating.FindName("HideFloatButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(floating.IsVisible); Assert.Equal("False", store.GetSetting("widget.timer.visible"));
        model.TimerFloatUseBackground = false;
        model.TimerFloatTransparency = 0;
        model.SelectedAppearanceMode = oldAppearance;
        model.SelectedRestrictionMode = model.RestrictionModes[0];
        reloaded.Media.Dispose();
    }

    private static void VerifyTodosAndOutcomes(MainWindow window, MainViewModel main, SqliteFocusStore store, ManualClock clock)
    {
        var library = main.Todos;
        Assert.True(library.SaveCategory(null, "学习待办"));
        Assert.False(library.SaveCategory(null, " 学习待办 "));
        var folder = library.SaveFolder(null, "课程论文", "学习待办")!;
        Assert.NotNull(folder); Assert.Null(library.SaveFolder(null, "课程论文", "学习待办"));
        var editor = new TodoEditorWindow(main) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((TextBox)editor.FindName("TitleInput")).Text = "阅读第三章";
            ((DatePicker)editor.FindName("DueDateInput")).SelectedDate = new DateTime(2026, 10, 20);
            ((TextBox)editor.FindName("DueTimeInput")).Text = "18:30";
            ((TextBox)editor.FindName("EstimateInput")).Text = "45";
            ((TextBox)editor.FindName("NotesInput")).Text = "整理概念与例题";
            ((TextBox)editor.FindName("ResumeInput")).Text = "从第42页继续";
            ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(editor.ShowDialog());
        var item = library.Items.Single(row => row.Title == "阅读第三章").Item;
        Assert.Equal(new DateOnly(2026, 10, 20), item.DueDate); Assert.Equal(new TimeOnly(18, 30), item.DueTime); Assert.Equal(45, item.EstimatedMinutes);
        Assert.True(library.SaveItem(new(Guid.NewGuid(), folder.Id, "写作提纲", Priority: TodoPriority.High)));
        Assert.False(library.SaveItem(new(Guid.NewGuid(), folder.Id, " 阅读第三章 ")));
        var otherFolder = library.SaveFolder(null, "工作待办", "未分类")!;
        var folderSelector = (ComboBox)window.FindName("FocusTodoFolderSelector");
        var itemSelector = (ComboBox)window.FindName("FocusTodoSelector");
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 0; FlushBindings(); window.UpdateLayout();
        folderSelector.SelectedItem = main.FocusTodoFolderChoices.Single(choice => choice.Id == otherFolder.Id); FlushBindings();
        Assert.Single(main.FolderFocusTodoChoices); Assert.Null(main.SelectedFocusTodo.Id);
        var otherItem = new TodoItem(Guid.NewGuid(), otherFolder.Id, item.Title);
        Assert.True(library.SaveItem(otherItem)); FlushBindings();
        Assert.Equal(new Guid?[] { null, otherItem.Id }, main.FolderFocusTodoChoices.Select(choice => choice.Id));
        itemSelector.SelectedItem = main.FolderFocusTodoChoices.Single(choice => choice.Id == otherItem.Id); FlushBindings();
        Assert.Equal(otherItem.Id, main.SelectedFocusTodo.Id);
        folderSelector.SelectedItem = main.FocusTodoFolderChoices.Single(choice => choice.Id == folder.Id); FlushBindings();
        Assert.Null(main.SelectedFocusTodo.Id); Assert.DoesNotContain(main.FolderFocusTodoChoices, choice => choice.Id == otherItem.Id);
        itemSelector.SelectedItem = main.FolderFocusTodoChoices.Single(choice => choice.Id == item.Id); FlushBindings();
        Assert.Equal(item.Id, main.SelectedFocusTodo.Id); Assert.Equal(item.Title, main.SelectedFocusTodo.Title);
        var restoredAssociation = new MainViewModel(store);
        Assert.Equal(folder.Id, restoredAssociation.SelectedFocusTodoFolder.Id); Assert.Equal(item.Id, restoredAssociation.SelectedFocusTodo.Id);
        restoredAssociation.Media.Dispose();
        main.SelectedFocusTodo = main.FocusTodoChoices.Single(choice => choice.Id == otherItem.Id); FlushBindings();
        Assert.Equal(otherFolder.Id, main.SelectedFocusTodoFolder.Id);
        Assert.True(library.DeleteFolder(otherFolder)); FlushBindings();
        Assert.Null(main.SelectedFocusTodoFolder.Id); Assert.Null(main.SelectedFocusTodo.Id); Assert.False(itemSelector.IsEnabled);
        folderSelector.SelectedItem = main.FocusTodoFolderChoices.Single(choice => choice.Id == folder.Id); FlushBindings();
        itemSelector.SelectedItem = main.FolderFocusTodoChoices.Single(choice => choice.Id == item.Id); FlushBindings();
        library.SelectedFolder = folder;
        SavePreview((FrameworkElement)window.Content, window.Background, "focus-todo-folder-selection.png");
        ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("TodoTab");
        window.UpdateLayout(); FlushBindings();
        var todoView = (TodosView)window.FindName("TodoPage");
        Assert.Equal(2, ((ListBox)todoView.FindName("TodoList")).Items.Count);
        library.Search = "提纲"; FlushBindings(); Assert.Single(library.VisibleItems); library.Search = "";
        library.MoveItem(library.Items.Single(row => row.Title == "写作提纲"), OrderMove.First);
        Assert.Equal("写作提纲", library.VisibleItems[0].Title);
        FlushBindings(); window.UpdateLayout();
        Assert.Contains("阅读第三章", VisibleTexts(todoView)); Assert.Contains("写作提纲", VisibleTexts(todoView));
        SavePreview((FrameworkElement)window.Content, window.Background, "todos-page.png");
        var appearance = main.SelectedAppearanceMode;
        main.SelectedAppearanceMode = main.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
        FlushBindings(); window.UpdateLayout();
        SavePreview((FrameworkElement)window.Content, window.Background, "todos-dark.png");
        var darkEditor = new TodoEditorWindow(main, item) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            darkEditor.UpdateLayout(); FlushBindings();
            var picker = (DatePicker)darkEditor.FindName("DueDateInput");
            Assert.Equal(((SolidColorBrush)Application.Current.FindResource("FieldBrush")).Color, ((SolidColorBrush)picker.Background).Color);
            SavePreview((FrameworkElement)darkEditor.Content, darkEditor.Background, "todo-editor-dark.png", (int)darkEditor.ActualWidth, (int)darkEditor.ActualHeight);
            darkEditor.DialogResult = false;
        }));
        darkEditor.ShowDialog(); main.SelectedAppearanceMode = appearance;
        var count = store.GetRecords().Count;
        main.SelectedRestrictionMode = main.RestrictionModes[0]; main.RestMinutes = "0"; main.SelectedTimerMode = main.TimerModes[0];
        var launch = new TodoStartWindow(main, item, 5) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => ((Button)launch.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
        Assert.True(launch.ShowDialog());
        Assert.Equal("未分类", main.SelectedProject!.Category); Assert.Equal(TimerMode.Countdown, main.SelectedTimerMode.Mode);
        Assert.Equal(item.Id, main.SelectedFocusTodo.Id); Assert.Equal("阅读第三章", main.FocusTodoText);
        Assert.Equal(folder.Id, main.SelectedFocusTodoFolder.Id);
        Assert.Equal("从第42页继续", main.FocusResumeNote);
        Assert.Equal(TodoStatus.InProgress, library.Items.Single(row => row.Id == item.Id).Item.Status);
        Assert.False(library.CanDeleteItem(item.Id)); Assert.False(library.CanDeleteFolder(folder)); Assert.False(library.CanDeleteCategory("学习待办"));
        Assert.False(main.StartFromTodo(library.Items.First(row => row.Id != item.Id).Id));
        main.SelectedFocusTodo = main.FocusTodoChoices[0]; Assert.Equal(item.Id, main.SelectedFocusTodo.Id);
        main.SelectedFocusTodoFolder = main.FocusTodoFolderChoices[0]; Assert.Equal(folder.Id, main.SelectedFocusTodoFolder.Id);
        window.ReturnToFocus(); clock.Advance(TimeSpan.FromSeconds(12)); main.Tick();
        FlushBindings(); window.UpdateLayout();
        SavePreview((FrameworkElement)window.Content, window.Background, "todo-focus.png");
        main.EndCommand.Execute(null);
        Assert.Equal(count + 1, store.GetRecords().Count); Assert.Equal(item.Id, store.GetRecords().First().TodoId);
        Assert.True(main.ShowOutcomeEntry); Assert.Equal(TodoStatus.InProgress, library.Items.Single(row => row.Id == item.Id).Item.Status);
        var id = main.LastSavedRecordId!.Value;
        var result = new OutcomeEditorWindow(main, id) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((TextBox)result.FindName("SummaryInput")).Text = "读完第三章";
            ((TextBox)result.FindName("QuantityInput")).Text = "8";
            ((TextBox)result.FindName("UnitInput")).Text = "页";
            ((TextBox)result.FindName("NextInput")).Text = "接着做练习题";
            ((Button)result.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(result.ShowDialog());
        Assert.Equal("读完第三章", store.GetOutcome(id)!.Summary); Assert.Equal("接着做练习题", main.FocusResumeNote);
        Assert.Equal(TimeSpan.FromSeconds(12), store.GetRecords().First(record => record.Id == id).FocusDuration);
        var project = main.Projects.First(); main.SelectedTimerMode = main.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
        main.PomodoroFocusMinutes = "0.2"; main.PomodoroShortRestMinutes = "0.05"; main.PomodoroRounds = "2";
        Assert.True(main.StartFromTodo(item.Id, project)); clock.Advance(TimeSpan.FromSeconds(12)); main.Tick();
        Assert.Equal(FocusState.Resting, main.State);
        var firstRound = main.LastSavedRecordId!.Value;
        Assert.True(main.SaveOutcome(new(id, "补充旧成果", 9, "页", "旧记录的备注")));
        Assert.Equal(FocusState.Resting, main.State); Assert.Equal("接着做练习题", library.Items.Single(row => row.Id == item.Id).Item.ResumeNote);
        clock.Advance(TimeSpan.FromSeconds(3)); main.Tick(); Assert.Equal(FocusState.Focusing, main.State);
        Assert.True(main.SaveOutcome(new(firstRound, "番茄第一轮成果", NextStep: "第二轮继续")));
        Assert.Equal(FocusState.Focusing, main.State);
        clock.Advance(TimeSpan.FromSeconds(12)); main.Tick(); Assert.Equal(FocusState.Ended, main.State);
        Assert.Equal(count + 3, store.GetRecords().Count);
        Assert.All(store.GetRecords().Take(2), record => Assert.Equal(item.Id, record.TodoId));
        main.SelectedFocusTodo = main.FocusTodoChoices[0]; main.SelectedTimerMode = main.TimerModes[0];
        main.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(10)); main.EndCommand.Execute(null);
        var independent = main.LastSavedRecordId!.Value; Assert.Null(main.GetRecord(independent)!.TodoId);
        Assert.True(main.SaveOutcome(new(independent, "自由阅读", NextStep: "继续附录")));
        Assert.True(main.HasResumeNote); Assert.Equal("继续附录", main.FocusResumeNote);
        var beforeShort = store.GetRecords().Count;
        main.StartCommand.Execute(null); clock.Advance(TimeSpan.FromSeconds(2)); main.EndCommand.Execute(null);
        Assert.Equal(beforeShort, store.GetRecords().Count); Assert.False(main.ShowOutcomeEntry);
        var row = library.Items.Single(row => row.Id == item.Id);
        library.MoveItem(row, OrderMove.First); Assert.Equal(item.Id, library.VisibleItems[0].Id);
        library.ToggleCompleted(row);
        Assert.True(library.Items.Single(row => row.Id == item.Id).Completed);
        Assert.Equal(item.Id, library.VisibleItems[^1].Id); Assert.Equal("写作提纲", library.VisibleItems[0].Title);
        var reloaded = new MainViewModel(store);
        Assert.Equal(item.Id, reloaded.Todos.VisibleItems[^1].Id); Assert.Equal("写作提纲", reloaded.Todos.VisibleItems[0].Title);
        Assert.Equal(TodoStatus.Completed, reloaded.Todos.Items.Single(row => row.Id == item.Id).Item.Status);
        library.ToggleCompleted(library.Items.Single(row => row.Id == item.Id));
        Assert.Equal(item.Id, library.VisibleItems[0].Id);
        library.ToggleCompleted(library.Items.Single(row => row.Id == item.Id));
        Assert.Equal("自由阅读", reloaded.GetOutcome(independent)!.Summary);
        main.NewFolderName = "待办计时分组"; main.AddFolderCommand.Execute(null);
        Assert.True(main.StartFromTodo(item.Id, category: "待办计时分组"));
        Assert.Equal("待办计时分组", main.SelectedProject!.Category); Assert.Equal("未分类", main.SelectedProject.Name);
        clock.Advance(TimeSpan.FromSeconds(10)); main.EndCommand.Execute(null);
        Assert.Equal("待办计时分组", main.Records.First().Category);
        Assert.True(library.DeleteItem(item.Id)); Assert.Equal("阅读第三章", main.Records.First(record => record.Id == id).Todo);
        Assert.NotNull(store.GetOutcome(id));
        Assert.True(library.DeleteFolder(folder)); Assert.Empty(library.Items);
        Assert.True(library.DeleteCategory("学习待办"));
        main.SelectedFocusTodo = main.FocusTodoChoices[0]; reloaded.Media.Dispose();
    }

    private static void VerifyQuadrants(MainWindow window, MainViewModel main, SqliteFocusStore store, ManualClock clock)
    {
        var library = main.Todos;
        library.SaveCategory(null, "象限测试"); var folder = library.SaveFolder(null, "本周事项", "象限测试")!;
        var page = (TodosView)window.FindName("TodoPage");
        ((TabControl)window.FindName("MainTabs")).SelectedItem = window.FindName("TodoTab");
        library.ViewMode = "四象限"; FlushBindings(); window.UpdateLayout();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)page.FindName("QuadrantViewport")).Visibility);
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedIndex = 1; FlushBindings(); window.UpdateLayout();
        var projectWidth = ((Border)window.FindName("ProjectContentCard")).ActualWidth;
        var selectedProject = main.SelectedProject;
        var projectToggle = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("ProjectSidebarToggle");
        projectToggle.IsChecked = true; FlushBindings(); window.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, ((Border)window.FindName("ProjectFoldersPane")).Visibility);
        Assert.True(((Border)window.FindName("ProjectContentCard")).ActualWidth >= projectWidth + 239);
        Assert.Same(selectedProject, main.SelectedProject);
        projectToggle.IsChecked = false; tabs.SelectedItem = window.FindName("TodoTab"); FlushBindings(); window.UpdateLayout();
        var todoWidth = ((Border)page.FindName("TodoContentCard")).ActualWidth;
        var todoToggle = (System.Windows.Controls.Primitives.ToggleButton)page.FindName("TodoSidebarToggle");
        todoToggle.IsChecked = true; FlushBindings(); window.UpdateLayout();
        Assert.Equal(Visibility.Collapsed, ((Border)page.FindName("TodoCategoriesPane")).Visibility);
        Assert.True(((Border)page.FindName("TodoContentCard")).ActualWidth >= todoWidth + 222);
        Assert.Equal(folder.Id, library.SelectedFolder!.Id);
        todoToggle.IsChecked = false; FlushBindings(); window.UpdateLayout();
        Assert.Null(page.FindName("TodoCategorySelector"));
        foreach (var quadrant in library.Quadrants)
        {
            var add = Descendants<Button>(page).Single(button => Equals(button.Content, "＋") && ReferenceEquals(button.DataContext, quadrant));
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows.OfType<TodoEditorWindow>().Single();
                Assert.Equal(quadrant.Important, ((CheckBox)editor.FindName("ImportantInput")).IsChecked);
                Assert.Equal(quadrant.Urgent, ((CheckBox)editor.FindName("UrgentInput")).IsChecked);
                ((TextBox)editor.FindName("TitleInput")).Text = quadrant.Title;
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings(); window.UpdateLayout();
            Assert.Single(quadrant.Items);
        }
        SavePreview((FrameworkElement)window.Content, window.Background, "todo-quadrants.png");
        var viewport = (ScrollViewer)page.FindName("QuadrantViewport");
        var board = (FrameworkElement)page.FindName("QuadrantBoard");
        var origin = board.TranslatePoint(new Point(board.ActualWidth / 2, board.ActualHeight / 2), viewport);
        var originX = origin.X; var originY = origin.Y;
        var axes = (System.Windows.Shapes.Path)page.FindName("QuadrantAxes");
        void CheckAxisPosition()
        {
            var center = ((EllipseGeometry)((GeometryGroup)axes.Data).Children[1]).Center;
            var drawnOrigin = axes.TranslatePoint(center, viewport);
            var boardOrigin = board.TranslatePoint(new Point(board.ActualWidth / 2, board.ActualHeight / 2), viewport);
            Assert.InRange((drawnOrigin - boardOrigin).Length, 0, 0.5);
            Assert.True(axes.RenderedGeometry.Bounds.Width >= board.ActualWidth);
        }
        CheckAxisPosition();
        todoToggle.IsChecked = true; FlushBindings(); window.UpdateLayout(); CheckAxisPosition();
        SavePreview((FrameworkElement)window.Content, window.Background, "todo-quadrant-axes-expanded.png");
        todoToggle.IsChecked = false; FlushBindings(); window.UpdateLayout(); CheckAxisPosition();
        Assert.Equal(0, viewport.ScrollableHeight);
        foreach (var quadrant in library.Quadrants)
        {
            var heading = Descendants<TextBlock>(page).Single(text => text.Text == quadrant.Title && ReferenceEquals(text.DataContext, quadrant));
            var bounds = heading.TransformToAncestor(viewport).TransformBounds(new Rect(heading.RenderSize));
            Assert.True(bounds.Top >= 0 && bounds.Bottom <= viewport.ActualHeight, "常规窗口下四个象限标题均须可见。");
            Assert.Equal(quadrant.Important, bounds.Left > originX);
            Assert.Equal(quadrant.Urgent, bounds.Top < originY);
            var scroll = Descendants<ScrollViewer>(page).Single(element => ReferenceEquals(element.DataContext, quadrant));
            var card = Descendants<Border>(scroll).Single(border => border.DataContext is TodoRow && border.CornerRadius.TopLeft == 10);
            var cardBounds = card.TransformToAncestor(scroll).TransformBounds(new Rect(card.RenderSize));
            Assert.True(cardBounds.Top >= 0 && cardBounds.Bottom <= scroll.ViewportHeight + 1, "单个事项卡片在常规窗口下须完整显示。");
            Assert.Equal(0, scroll.ScrollableHeight);
            Assert.InRange(card.ActualHeight, 31, 33);
        }
        var appearance = main.SelectedAppearanceMode;
        main.SelectedAppearanceMode = main.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
        FlushBindings(); window.UpdateLayout(); SavePreview((FrameworkElement)window.Content, window.Background, "todo-quadrants-dark.png");
        var secondFolder = library.SaveFolder(null, "工作事项", "旧分类")!;
        Assert.Null(library.SaveFolder(null, folder.Name, "另一个旧分类"));
        var crossFolderItem = new TodoItem(Guid.NewGuid(), secondFolder.Id, "处理工作邮件", Important: true, Urgent: false);
        Assert.True(library.SaveItem(crossFolderItem)); library.SelectedFolder = folder;
        Assert.Equal(5, library.VisibleItems.Count); Assert.Contains(library.VisibleItems, row => row.Id == crossFolderItem.Id);
        library.QuadrantScope = "当前文件夹"; Assert.Equal(4, library.VisibleItems.Count);
        Assert.DoesNotContain(library.VisibleItems, row => row.Id == crossFolderItem.Id);
        library.SelectedFolder = secondFolder; Assert.Equal(crossFolderItem.Id, Assert.Single(library.VisibleItems).Id);
        var scopedReload = new MainViewModel(store);
        Assert.Equal(secondFolder.Id, scopedReload.Todos.SelectedFolder!.Id); Assert.Equal("当前文件夹", scopedReload.Todos.QuadrantScope);
        Assert.Equal(crossFolderItem.Id, Assert.Single(scopedReload.Todos.VisibleItems).Id); scopedReload.Media.Dispose();
        library.QuadrantScope = "全部文件夹"; Assert.Equal(5, library.VisibleItems.Count);
        library.ViewMode = "列表"; Assert.Equal(crossFolderItem.Id, Assert.Single(library.VisibleItems).Id);
        library.ViewMode = "四象限"; Assert.Equal(5, library.VisibleItems.Count);
        Assert.True(library.DeleteFolder(secondFolder)); library.SelectedFolder = folder;
        FlushBindings(); window.UpdateLayout();
        var detailItem = library.Quadrants[0].Items[0].Item;
        Assert.True(library.SaveItem(detailItem with { Notes = "完整备注", DueDate = new DateOnly(2026, 10, 20), DueTime = new TimeOnly(18, 30) }));
        FlushBindings(); window.UpdateLayout();
        var deadline = Descendants<TextBlock>(page).Single(text => text.Name == "QuadrantDeadline" && text.DataContext is TodoRow row && row.Id == detailItem.Id);
        Assert.Equal("10-20 18:30", deadline.Text); Assert.Equal(Visibility.Visible, deadline.Visibility);
        Assert.All(Descendants<TextBlock>(page).Where(text => text.Name == "QuadrantDeadline" && text.DataContext is TodoRow row && !row.HasDueDate), text => Assert.Equal(Visibility.Collapsed, text.Visibility));
        SavePreview((FrameworkElement)window.Content, window.Background, "todo-quadrants-dark.png");
        var detailButton = Descendants<Button>(page).Single(button => button.Name == "OpenTodoDetailsButton" && button.DataContext is TodoRow row && row.Id == detailItem.Id);
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var details = Application.Current.Windows.OfType<TodoDetailsWindow>().Single();
            Assert.Equal(detailItem.Title, ((TextBlock)details.FindName("DetailTitle")).Text);
            Assert.Equal(folder.Name, ((TextBlock)details.FindName("FolderLocation")).Text);
            Assert.Equal("完整备注", ((TextBlock)details.FindName("DetailNotes")).Text);
            Assert.Contains("2026-10-20", ((TextBlock)details.FindName("DetailDue")).Text);
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var editor = Application.Current.Windows.OfType<TodoEditorWindow>().Single();
                ((TextBox)editor.FindName("NotesInput")).Text = "修改后的备注";
                ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }));
            ((Button)details.FindName("EditButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
            Assert.Equal("修改后的备注", ((TextBlock)details.FindName("DetailNotes")).Text);
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
            {
                var start = Application.Current.Windows.OfType<TodoStartWindow>().Single();
                Assert.Equal(detailItem.Title, ((TextBlock)start.FindName("TodoHeading")).Text);
                start.DialogResult = false;
            }));
            ((Button)details.FindName("FocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            SavePreview((FrameworkElement)details.Content, details.Background, "todo-details-dark.png", (int)details.ActualWidth, (int)details.ActualHeight);
            details.Close();
        }));
        detailButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FlushBindings();
        VerifyTodoStartSettings(window, main, library.Items.Single(row => row.Id == detailItem.Id).Item, clock);
        var crowded = library.Quadrants[0];
        var extraItems = Enumerable.Range(1, 12).Select(index => new TodoItem(Guid.NewGuid(), folder.Id, $"坐标事项 {index}", Important: true, Urgent: true)).ToArray();
        foreach (var extra in extraItems) Assert.True(library.SaveItem(extra));
        library.ToggleCompleted(library.Items.Single(row => row.Id == extraItems[0].Id));
        library.ToggleCompleted(library.Items.Single(row => row.Id == extraItems[1].Id));
        Assert.Equal(new[] { extraItems[0].Id, extraItems[1].Id }, crowded.Items.TakeLast(2).Select(row => row.Id));
        Assert.All(crowded.Items.Take(crowded.Items.Count - 2), row => Assert.False(row.Completed));
        library.ToggleCompleted(library.Items.Single(row => row.Id == extraItems[0].Id));
        library.ToggleCompleted(library.Items.Single(row => row.Id == extraItems[1].Id));
        FlushBindings(); window.UpdateLayout();
        var quadrantScroll = Descendants<ScrollViewer>(page).Single(scroll => ReferenceEquals(scroll.DataContext, crowded));
        Assert.True(quadrantScroll.ScrollableHeight > 0);
        quadrantScroll.ScrollToEnd(); FlushBindings(); window.UpdateLayout();
        var lastCard = Descendants<Border>(quadrantScroll).Single(border => border.DataContext is TodoRow row && row.Id == extraItems[^1].Id && border.CornerRadius.TopLeft == 10);
        var lastBounds = lastCard.TransformToAncestor(quadrantScroll).TransformBounds(new Rect(lastCard.RenderSize));
        Assert.True(lastBounds.Bottom <= quadrantScroll.ViewportHeight + 1, "象限末项须能完整滚动到可见区域。");
        quadrantScroll.ScrollToHome(); FlushBindings(); window.UpdateLayout();
        Assert.Equal(0, quadrantScroll.VerticalOffset);
        foreach (var extra in extraItems) Assert.True(library.DeleteItem(extra.Id));
        library.ViewMode = "列表"; FlushBindings(); window.UpdateLayout();
        var todoList = (ListBox)page.FindName("TodoList");
        var listContainers = Enumerable.Range(0, 4).Select(index => (ListBoxItem)todoList.ItemContainerGenerator.ContainerFromIndex(index)).ToArray();
        var listBounds = listContainers.Select(container => container.TransformToAncestor(todoList).TransformBounds(new Rect(container.RenderSize))).ToArray();
        Assert.InRange(Math.Abs(listBounds[0].Top - listBounds[2].Top), 0, 1);
        Assert.True(listBounds[0].Right <= listBounds[1].Left && listBounds[1].Right <= listBounds[2].Left);
        Assert.True(listBounds[3].Top > listBounds[0].Top);
        Assert.All(listContainers, container => Assert.InRange(container.ActualHeight, 119, 121));
        var listDeadline = Descendants<TextBlock>(todoList).Single(text => text.Name == "ListDeadline" && text.DataContext is TodoRow row && row.Id == detailItem.Id);
        Assert.Equal(12, listDeadline.FontSize); Assert.Contains("2026-10-20", listDeadline.Text);
        SavePreview((FrameworkElement)window.Content, window.Background, "todo-list-cards-dark.png");
        library.ViewMode = "四象限";
        library.Search = "不重要且紧急"; Assert.Single(library.VisibleItems); Assert.Single(library.Quadrants[2].Items);
        library.Search = ""; library.ViewMode = "列表";
        var item = library.Quadrants[0].Items[0].Item;
        Assert.True(library.SaveItem(item with { Urgent = false })); Assert.Empty(library.Quadrants[0].Items); Assert.Equal(2, library.Quadrants[1].Items.Count);
        library.ViewMode = "四象限"; var reloaded = new MainViewModel(store);
        Assert.Equal("四象限", reloaded.Todos.ViewMode); Assert.Equal(2, reloaded.Todos.Quadrants[1].Items.Count);
        Assert.Equal("全部文件夹", reloaded.Todos.QuadrantScope);
        Assert.True(library.DeleteFolder(folder)); library.DeleteCategory("象限测试"); library.ViewMode = "列表";
        reloaded.Media.Dispose(); main.SelectedAppearanceMode = appearance;
    }

    private static void VerifyTodoStartSettings(MainWindow window, MainViewModel main, TodoItem item, ManualClock clock)
    {
        var originalMode = main.SelectedTimerMode; var originalDuration = main.DurationMinutes;
        var cancelled = new TodoStartWindow(main, item) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((ComboBox)cancelled.FindName("TimerSelector")).SelectedItem = main.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
            FlushBindings(); Assert.Equal(Visibility.Visible, ((FrameworkElement)cancelled.FindName("CycleSettings")).Visibility);
            ((TextBox)((PomodoroSettingsView)cancelled.FindName("CycleSettings")).FindName("FocusMinutesInput")).Text = "20";
            cancelled.DialogResult = false;
        }));
        var originalCycle = main.PomodoroFocusMinutes; cancelled.ShowDialog();
        Assert.Same(originalMode, main.SelectedTimerMode); Assert.Equal(originalDuration, main.DurationMinutes); Assert.Equal(originalCycle, main.PomodoroFocusMinutes);
        var start = new TodoStartWindow(main, item) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((ComboBox)start.FindName("TimerSelector")).SelectedItem = main.TimerModes.Single(mode => mode.Mode == TimerMode.Countdown);
            ((ComboBox)start.FindName("RestrictionSelector")).SelectedItem = main.RestrictionModes.Single(mode => !mode.Strict);
            ((TextBox)start.FindName("DurationInput")).Text = "0.1";
            Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single().Close()));
            ((Button)start.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(main.IsFocusActive); Assert.True(start.IsVisible);
            Assert.Equal(originalDuration, main.DurationMinutes);
            ((TextBox)start.FindName("DurationInput")).Text = "2";
            FlushBindings(); start.UpdateLayout();
            SavePreview((FrameworkElement)start.Content, start.Background, "todo-start-settings.png", (int)start.ActualWidth, (int)start.ActualHeight);
            ((Button)start.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(start.ShowDialog()); Assert.Equal(TimerMode.Countdown, main.SelectedTimerMode.Mode); Assert.Equal("2", main.DurationMinutes);
        Assert.Equal(item.Id, main.SelectedFocusTodo.Id); Assert.False(main.SelectedRestrictionMode.Strict);
        main.RestMinutes = "0"; clock.Advance(TimeSpan.FromSeconds(12)); main.Tick(); main.EndCommand.Execute(null);
        var originalFocus = main.PomodoroFocusMinutes; var originalShort = main.PomodoroShortRestMinutes;
        var originalLong = main.PomodoroLongRestMinutes; var originalEvery = main.PomodoroLongRestEvery; var originalRounds = main.PomodoroRounds;
        var cycleStart = new TodoStartWindow(main, item) { Owner = window };
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            ((ComboBox)cycleStart.FindName("TimerSelector")).SelectedItem = main.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
            ((ComboBox)cycleStart.FindName("RestrictionSelector")).SelectedItem = main.RestrictionModes.Single(mode => mode.Strict);
            var cycle = (PomodoroSettingsView)cycleStart.FindName("CycleSettings");
            FlushBindings(); cycleStart.UpdateLayout();
            foreach (var setting in new[] { ("PomodoroFocusMinutes", "0.2"), ("PomodoroShortRestMinutes", "0"), ("PomodoroLongRestMinutes", "0"), ("PomodoroLongRestEvery", "1"), ("PomodoroRounds", "1") })
            {
                var input = Descendants<TextBox>(cycle).Single(text => System.Windows.Data.BindingOperations.GetBindingExpression(text, TextBox.TextProperty)?.ParentBinding.Path.Path == setting.Item1);
                input.Text = setting.Item2;
            }
            FlushBindings(); cycleStart.UpdateLayout();
            SavePreview((FrameworkElement)cycleStart.Content, cycleStart.Background, "todo-start-pomodoro.png", (int)cycleStart.ActualWidth, (int)cycleStart.ActualHeight);
            ((Button)cycleStart.FindName("StartButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
        Assert.True(cycleStart.ShowDialog()); Assert.Equal(TimerMode.Pomodoro, main.SelectedTimerMode.Mode); Assert.True(main.RestrictionsActive);
        Assert.Equal("0.2", main.PomodoroFocusMinutes); Assert.Equal("1", main.PomodoroRounds);
        clock.Advance(TimeSpan.FromSeconds(12)); main.Tick(); Assert.Equal(FocusState.Ended, main.State);
        main.PomodoroFocusMinutes = originalFocus; main.PomodoroShortRestMinutes = originalShort; main.PomodoroLongRestMinutes = originalLong;
        main.PomodoroLongRestEvery = originalEvery; main.PomodoroRounds = originalRounds;
        main.SelectedRestrictionMode = main.RestrictionModes.Single(mode => !mode.Strict);
        main.SelectedTimerMode = originalMode; main.DurationMinutes = originalDuration;
    }

    private static void VerifyFloatingActions(TimerFloatWindow floating)
    {
        var width = floating.Width; var height = floating.Height;
        var surface = (Border)floating.FindName("FloatSurface");
        var actions = (Grid)floating.FindName("FloatActions");
        var group = (StackPanel)floating.FindName("FloatTimerActions");
        var timer = (TextBlock)floating.FindName("FloatTimer");
        var brand = Descendants<StackPanel>((Border)floating.FindName("DragArea")).Single();
        var hoverKey = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        void SetHover(bool value)
        {
            surface.SetValue(hoverKey, value);
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        }
        foreach (var size in new[] { new Size(240, 180), new Size(360, 260) })
        {
            floating.Width = size.Width; floating.Height = size.Height;
            floating.UpdateLayout(); FlushBindings(); SetHover(false);
            Assert.Equal(Visibility.Hidden, actions.Visibility);
            Assert.False(group.IsVisible);
            Assert.Equal(Visibility.Hidden, brand.Visibility);
            var timerBounds = timer.TransformToAncestor(floating).TransformBounds(new Rect(timer.RenderSize));
            SetHover(true); floating.UpdateLayout();
            Assert.Equal(Visibility.Visible, actions.Visibility);
            Assert.True(group.IsVisible);
            Assert.Equal(Visibility.Visible, brand.Visibility);
            var bounds = group.TransformToAncestor(floating).TransformBounds(new Rect(group.RenderSize));
            Assert.InRange(Math.Abs(bounds.Left + bounds.Width / 2 - floating.ActualWidth / 2), 0, 0.5);
            Assert.Equal(timerBounds, timer.TransformToAncestor(floating).TransformBounds(new Rect(timer.RenderSize)));
            AssertVisibleInViewport(group, (FrameworkElement)floating.Content);
            var mainButton = (Button)floating.FindName("OpenMainButton");
            var mainBounds = mainButton.TransformToAncestor(floating).TransformBounds(new Rect(mainButton.RenderSize));
            Assert.True(bounds.Right < mainBounds.Left, "计时按钮与返回主窗口按钮须保持间距。");
            SetHover(false);
            Assert.Equal(Visibility.Hidden, actions.Visibility);
            Assert.Equal(Visibility.Hidden, brand.Visibility);
            Assert.Equal(timerBounds, timer.TransformToAncestor(floating).TransformBounds(new Rect(timer.RenderSize)));
        }
        floating.Width = width; floating.Height = height; floating.UpdateLayout(); FlushBindings();
    }

    private static void VerifyPomodoro(MainWindow window, MainViewModel model, SqliteFocusStore store, ManualClock clock)
    {
        var tabs = (TabControl)window.FindName("MainTabs");
        tabs.SelectedIndex = 4;
        model.DefaultTimerMode = model.TimerModes.Single(mode => mode.Mode == TimerMode.Pomodoro);
        model.DefaultRestrictionMode = model.RestrictionModes.Single(mode => mode.Strict);
        model.PomodoroFocusMinutes = "0.2";
        model.PomodoroShortRestMinutes = "0.05";
        model.PomodoroLongRestMinutes = "0.1";
        model.PomodoroLongRestEvery = "2";
        model.PomodoroRounds = "3";
        var mediaPreferences = store.GetSetting("focus.media");
        model.SaveSettingsCommand.Execute(null);
        Assert.Equal(mediaPreferences, store.GetSetting("focus.media"));
        var reopened = new MainViewModel(store);
        Assert.True(reopened.Pomodoro);
        Assert.Equal("0.2", reopened.PomodoroFocusMinutes);
        Assert.Equal("0.05", reopened.PomodoroShortRestMinutes);
        Assert.Equal("0.1", reopened.PomodoroLongRestMinutes);
        Assert.Equal("2", reopened.PomodoroLongRestEvery);
        Assert.Equal("3", reopened.PomodoroRounds);
        FlushBindings();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PomodoroPreferences")).Visibility);
        tabs.SelectedIndex = 0;
        FlushBindings();
        Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("PomodoroConfiguration")).Visibility);
        var count = store.GetRecords().Count;
        model.PomodoroFocusMinutes = "0";
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() =>
        {
            var notice = Application.Current.Windows.OfType<ConfirmDeletionWindow>().Single();
            Assert.Equal("专注时长至少10秒", ((TextBlock)notice.FindName("DeletionHeading")).Text);
            Assert.Equal(Visibility.Visible, ((Button)notice.FindName("NoticeButton")).Visibility);
            Assert.Equal(Visibility.Collapsed, ((Button)notice.FindName("ConfirmDeletionButton")).Visibility);
            notice.DialogResult = false;
        }));
        model.StartCommand.Execute(null);
        Assert.False(model.IsFocusActive);
        model.PomodoroFocusMinutes = "0.2";
        model.StartCommand.Execute(null);
        Assert.True(model.RestrictionsActive);
        Assert.Contains("第 1 / 3 轮", model.PhaseText);
        Assert.Equal("00:00:12", model.TimerText);
        FlushBindings();
        Assert.False(((FrameworkElement)window.FindName("PomodoroConfiguration")).IsEnabled);
        SavePreview((FrameworkElement)window.Content, window.Background, "pomodoro-focus.png");
        var quote = model.Media.CurrentQuote;
        clock.Advance(TimeSpan.FromSeconds(5)); model.PauseCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(1)); model.ResumeCommand.Execute(null);
        Assert.Same(quote, model.Media.CurrentQuote);
        Assert.True(model.RestrictionsActive);
        clock.Advance(TimeSpan.FromSeconds(7)); model.Tick();
        Assert.Equal(FocusState.Resting, model.State);
        Assert.Equal("短休息", model.StateText);
        Assert.Equal("00:00:03", model.TimerText);
        Assert.False(model.RestrictionsActive);
        Assert.True(model.CanEditWhitelist);
        Assert.True(model.ShowEnd);
        Assert.False(model.CanDeleteProject(model.SelectedProject!));
        Assert.Equal(count + 1, store.GetRecords().Count);
        Assert.Equal(TimeSpan.FromSeconds(12), store.GetRecords().First().FocusDuration);
        Assert.Equal("番茄循环", model.Records.First().Mode);
        var site = new WebsiteRule("pomodoro.example", true);
        model.Whitelist.Websites.Add(site);
        model.SkipRestCommand.Execute(null);
        Assert.Contains("第 2 / 3 轮", model.PhaseText);
        Assert.Contains(site, model.SessionWebsites);
        Assert.True(model.RestrictionsActive);
        clock.Advance(TimeSpan.FromSeconds(12)); model.Tick();
        Assert.Equal("长休息", model.StateText);
        Assert.Equal("00:00:06", model.TimerText);
        FlushBindings();
        SavePreview((FrameworkElement)window.Content, window.Background, "pomodoro-rest.png");
        clock.Advance(TimeSpan.FromSeconds(6)); model.Tick();
        Assert.Contains("第 3 / 3 轮", model.PhaseText);
        Assert.True(model.RestrictionsActive);
        clock.Advance(TimeSpan.FromSeconds(12)); model.Tick();
        Assert.Equal(FocusState.Ended, model.State);
        Assert.True(model.CanConfigure);
        Assert.False(model.RestrictionsActive);
        Assert.Equal(count + 3, store.GetRecords().Count);
        model.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(12)); model.Tick();
        Assert.Equal(FocusState.Resting, model.State);
        model.EndCommand.Execute(null);
        clock.Advance(TimeSpan.FromMinutes(1)); model.Tick();
        Assert.Equal(FocusState.Ended, model.State);
        Assert.Equal(count + 4, store.GetRecords().Count);
        model.Whitelist.Websites.Remove(site);
        model.DefaultTimerMode = model.TimerModes[0];
        model.DefaultRestrictionMode = model.RestrictionModes[0];
        model.SaveSettingsCommand.Execute(null);
    }

    private static void VerifyTimerPreferences(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, ManualClock clock)
    {
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 4;
        ((FrameworkElement)window.Content).UpdateLayout();
        FlushBindings();
        var defaultTimer = (ComboBox)window.FindName("DefaultTimerModeSelector");
        var defaultRestriction = (ComboBox)window.FindName("DefaultRestrictionModeSelector");
        defaultTimer.SelectedItem = viewModel.TimerModes.Single(choice => choice.Mode == TimerMode.Countdown);
        defaultRestriction.SelectedItem = viewModel.RestrictionModes.Single(choice => choice.Strict);
        FlushBindings();
        var save = (Button)window.FindName("SaveTimerPreferencesButton");
        save.Command.Execute(null);
        Assert.Equal(TimerMode.Countdown, viewModel.SelectedTimerMode.Mode);
        Assert.True(viewModel.SelectedRestrictionMode.Strict);
        var reopened = new MainViewModel(store);
        Assert.Equal(TimerMode.Countdown, reopened.SelectedTimerMode.Mode);
        Assert.True(reopened.SelectedRestrictionMode.Strict);

        viewModel.SelectedTimerMode = viewModel.TimerModes[0];
        viewModel.SelectedRestrictionMode = viewModel.RestrictionModes[0];
        viewModel.StartCommand.Execute(null);
        defaultTimer.SelectedItem = viewModel.TimerModes[0];
        defaultRestriction.SelectedItem = viewModel.RestrictionModes[0];
        FlushBindings();
        clock.Advance(TimeSpan.FromSeconds(1));
        viewModel.EndCommand.Execute(null);
        Assert.Equal(TimerMode.Countdown, viewModel.SelectedTimerMode.Mode);
        Assert.True(viewModel.SelectedRestrictionMode.Strict);

        viewModel.DurationMinutes = "2";
        viewModel.StartCommand.Execute(null);
        clock.Advance(TimeSpan.FromSeconds(10));
        viewModel.Tick();
        Assert.True(viewModel.RestrictionsActive);
        Assert.Equal("00:01:50", viewModel.TimerText);
        save.Command.Execute(null);
        Assert.Equal(TimerMode.Countdown, viewModel.SelectedTimerMode.Mode);
        Assert.True(viewModel.SelectedRestrictionMode.Strict);
        Assert.True(viewModel.RestrictionsActive);
        Assert.Equal("00:01:50", viewModel.TimerText);
        viewModel.EndCommand.Execute(null);
        Assert.Equal(TimerMode.CountUp, viewModel.SelectedTimerMode.Mode);
        Assert.False(viewModel.SelectedRestrictionMode.Strict);
        Assert.True(store.GetRecords().First().Strict);
        Assert.Equal(TimerMode.Countdown, store.GetRecords().First().Mode);
        reopened = new MainViewModel(store);
        Assert.Equal(TimerMode.CountUp, reopened.SelectedTimerMode.Mode);
        Assert.False(reopened.SelectedRestrictionMode.Strict);
    }

    private static void RunWithDispatcher(Func<Task> action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            var task = action();
            var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(60);
            while (!task.IsCompleted && DateTime.UtcNow < timeout) { FlushBindings(); Thread.Sleep(10); }
            Assert.True(task.IsCompleted, "浏览器验证应在限定时间完成。");
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (!predicate() && DateTime.UtcNow < until) await Task.Delay(20);
        Assert.True(predicate(), "浏览器未达到预期导航状态。");
    }

    private static async Task VerifyWindowRestrictionAsync(MainWindow window, MainViewModel viewModel, string directory)
    {
        Assert.False(viewModel.RestrictionsActive);
        using var monitor = new ApplicationRestrictionService(viewModel, window.ReturnToFocus);
        var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var fixtureScript = Path.Combine(directory, "restricted-window.ps1");
        File.WriteAllText(fixtureScript, """
            Add-Type -AssemblyName System.Windows.Forms
            $form = New-Object System.Windows.Forms.Form
            $form.Text = 'FocusBloom restriction test'
            $form.StartPosition = 'Manual'
            $form.Location = New-Object System.Drawing.Point(40,40)
            $form.Size = New-Object System.Drawing.Size(160,100)
            [Console]::WriteLine($form.Handle.ToInt64())
            [System.Windows.Forms.Application]::Run($form)
            """, System.Text.Encoding.ASCII);
        async Task<(Process Process, IntPtr Window)> CreateFixture()
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = powershell, ArgumentList = { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File", fixtureScript },
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            var handle = new IntPtr(long.Parse((await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!));
            await WaitForAsync(() => IsWindowVisible(handle));
            return (process, handle);
        }
        var previousMode = viewModel.Whitelist.ApplicationMode;
        viewModel.Whitelist.ApplicationMode = viewModel.Whitelist.ListModes.Single(choice => choice.Mode == AccessListMode.Blacklist);
        viewModel.Whitelist.AddApplication(new AllowedApplication("测试窗口", powershell));
        viewModel.SelectedTimerMode = viewModel.TimerModes[0]; viewModel.RestMinutes = "0";
        try
        {
            foreach (var originalState in new[] { 9, 6, 0 })
            {
                var fixture = await CreateFixture();
                using var process = fixture.Process;
                try
                {
                    ShowWindow(fixture.Window, originalState);
                    await WaitForAsync(() => originalState == 0 ? !IsWindowVisible(fixture.Window) : originalState != 6 || IsIconic(fixture.Window));
                    viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
                    viewModel.StartCommand.Execute(null);
                    ShowWindow(fixture.Window, 9);
                    typeof(ApplicationRestrictionService).GetMethod("CheckWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [fixture.Window]);
                    var blocked = (ApplicationBlockedWindow)typeof(ApplicationRestrictionService).GetField("_blockedWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
                    Assert.True(blocked.IsVisible); Assert.False(IsIconic(fixture.Window));
                    var activeTimer = (FocusTimer)typeof(MainViewModel).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
                    var interceptionCount = activeTimer.InterceptionCount;
                    Assert.True(interceptionCount > 0);
                    typeof(ApplicationRestrictionService).GetMethod("CheckWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [fixture.Window]);
                    Assert.Equal(interceptionCount, activeTimer.InterceptionCount);
                    ((Button)blocked.FindName("ReturnToFocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForAsync(() => originalState == 0 ? !IsWindowVisible(fixture.Window) : IsIconic(fixture.Window));
                    Assert.False(blocked.IsVisible); Assert.False(process.HasExited);
                    if (originalState != 0)
                    {
                        window.WindowState = WindowState.Minimized; FlushBindings();
                        Assert.NotEqual(fixture.Window, GetForegroundWindow());
                        window.WindowState = WindowState.Normal;
                    }
                    viewModel.EndCommand.Execute(null);
                }
                finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); }
            }
            viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
            viewModel.StartCommand.Execute(null);
            var created = await CreateFixture();
            using (var process = created.Process)
            {
                try
                {
                    typeof(ApplicationRestrictionService).GetMethod("CheckWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(monitor, [created.Window]);
                    var blocked = (ApplicationBlockedWindow)typeof(ApplicationRestrictionService).GetField("_blockedWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
                    ((Button)blocked.FindName("ReturnToFocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitForAsync(() => process.HasExited);
                    Assert.False(blocked.IsVisible); viewModel.EndCommand.Execute(null);
                }
                finally { if (!process.HasExited) process.Kill(); await process.WaitForExitAsync(); }
            }
        }
        finally
        {
            if (viewModel.IsFocusActive) viewModel.EndCommand.Execute(null);
            viewModel.Whitelist.ApplicationMode = previousMode;
            viewModel.SelectedRestrictionMode = viewModel.RestrictionModes[0];
        }
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static async Task VerifyBrowserAsync(MainWindow window, MainViewModel viewModel, string directory)
    {
        using var server = new BrowserTestServer();
        var originalAppearance = viewModel.SelectedAppearanceMode;
        viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
        ((Button)window.FindName("OpenStudyBrowserButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var browser = (StudyBrowserView)((TabItem)window.FindName("BrowserTab")).Content;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((FrameworkElement)window.Content).UpdateLayout();
        try
        {
            var tabs = (ListBox)browser.FindName("BrowserTabs");
            await WaitForAsync(() => tabs.Items.Count > 0 && Descendants<WebView2>(browser).Any(v => v.CoreWebView2 is not null));
            var webview = Descendants<WebView2>(browser).Single();
            webview.CoreWebView2.Environment.BrowserProcessExited += (_, _) => exited.TrySetResult();
            await VerifyBlankBrowserThemeAsync(webview, Color.FromRgb(36, 40, 50));
            viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Light);
            FlushBindings();
            await VerifyBlankBrowserThemeAsync(webview, Colors.White);
            viewModel.SelectedAppearanceMode = viewModel.AppearanceModes.Single(choice => choice.Mode == AppearanceMode.Dark);
            FlushBindings();
            await VerifyBlankBrowserThemeAsync(webview, Color.FromRgb(36, 40, 50));
            Descendants<Button>(browser).Single(button => Equals(button.Content, "＋")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => tabs.Items.Count == 2 && Descendants<WebView2>(browser).Any(view => view != webview && view.CoreWebView2 is not null));
            await VerifyBlankBrowserThemeAsync(Descendants<WebView2>(browser).Single(), Color.FromRgb(36, 40, 50));
            tabs.SelectedIndex = 0; FlushBindings();
            await browser.NavigateAsync(server.DeniedUrl);
            await WaitForAsync(() => webview.CoreWebView2.Source == server.DeniedUrl && webview.CoreWebView2.DocumentTitle == "Learning fixture");
            Assert.Equal("\"\"", await webview.CoreWebView2.ExecuteScriptAsync("document.documentElement.style.backgroundColor"));
            Assert.Equal(System.Drawing.Color.White, webview.DefaultBackgroundColor);
            viewModel.Whitelist.Websites.Clear();
            viewModel.Whitelist.Websites.Add(new WebsiteRule("127.0.0.1", false));
            viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
            viewModel.StartCommand.Execute(null);
            Assert.Equal(Visibility.Collapsed, webview.Visibility);
            var strictTimer = (FocusTimer)typeof(MainViewModel).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(viewModel)!;
            Assert.True(strictTimer.InterceptionCount > 0);
            ((TextBox)browser.FindName("AddressBox")).Text = server.AllowedUrl;
            ((Button)browser.FindName("BrowserNavigateButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => webview.Visibility == Visibility.Visible && webview.CoreWebView2.Source == server.AllowedUrl);
            await WaitForAsync(() => webview.CoreWebView2.DocumentTitle == "Learning fixture");
            await WaitForAsync(() => VisibleTexts(tabs).Contains("Learning fixture"));
            var assetUntil = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (await webview.CoreWebView2.ExecuteScriptAsync("document.getElementById('asset')?.naturalWidth === 10") != "true" && DateTime.UtcNow < assetUntil) await Task.Delay(20);
            Assert.Equal("10", await webview.CoreWebView2.ExecuteScriptAsync("document.getElementById('asset').naturalWidth"));
            var originalCore = webview.CoreWebView2;
            ((Button)browser.FindName("BrowserFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, ((FrameworkElement)window.FindName("BrowserFullscreenView")).Visibility);
            Assert.Same(browser, ((ContentControl)window.FindName("BrowserFullscreenHost")).Content);
            Assert.Same(originalCore, webview.CoreWebView2);
            Assert.Equal(server.AllowedUrl, webview.CoreWebView2.Source);
            Assert.Contains("返回全屏", ((Button)browser.FindName("BrowserReturnToTimerButton")).Content.ToString());
            browser.UpdateLayout();
            SavePreview(browser, (Brush)Application.Current.FindResource("BackgroundBrush"), "study-browser-toolbar.png", (int)Math.Ceiling(browser.ActualWidth), (int)Math.Ceiling(browser.ActualHeight));
            ((Button)browser.FindName("BrowserFullscreenButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Same(browser, ((TabItem)window.FindName("BrowserTab")).Content);
            Assert.Same(originalCore, webview.CoreWebView2);
            viewModel.PauseCommand.Execute(null);
            await browser.NavigateAsync(server.DeniedUrl);
            Assert.Equal(Visibility.Collapsed, webview.Visibility);
            var browserContent = (FrameworkElement)browser;
            browserContent.UpdateLayout();
            SavePreview(browserContent, (Brush)Application.Current.FindResource("BackgroundBrush"), "website-blocked.png", 1000, 700);
            viewModel.ResumeCommand.Execute(null);
            await browser.NavigateAsync(server.AllowedUrl);
            await WaitForAsync(() => webview.CoreWebView2.Source == server.AllowedUrl && webview.Visibility == Visibility.Visible);
            await webview.CoreWebView2.ExecuteScriptAsync($"window.location.href='http://127.0.0.1:{server.Port}/redirect'");
            await WaitForAsync(() => webview.Visibility == Visibility.Collapsed);
            await browser.NavigateAsync(server.AllowedUrl);
            await WaitForAsync(() => webview.Visibility == Visibility.Visible && webview.CoreWebView2.Source == server.AllowedUrl);
            var before = tabs.Items.Count;
            await webview.CoreWebView2.ExecuteScriptAsync($"window.open('{server.AllowedUrl}')");
            await WaitForAsync(() => tabs.Items.Count == before + 1 && Descendants<WebView2>(browser).Any(v => v.CoreWebView2?.Source == server.AllowedUrl));
            var popup = Descendants<WebView2>(browser).Single();
            await WaitForAsync(() => popup.CoreWebView2.DocumentTitle == "Learning fixture");
            await popup.CoreWebView2.ExecuteScriptAsync($"window.open('{server.DeniedUrl}')");
            await WaitForAsync(() => popup.Visibility == Visibility.Collapsed);
            Assert.Equal(before + 1, tabs.Items.Count);
            viewModel.EndCommand.Execute(null);
            await WaitForAsync(() => popup.Visibility == Visibility.Visible && popup.CoreWebView2.Source == server.DeniedUrl);
        }
        finally
        {
            browser.Dispose();
            window.ReturnToFocus();
            viewModel.PrepareExit();
            viewModel.SelectedAppearanceMode = originalAppearance;
            await Task.WhenAny(exited.Task, Task.Delay(5000));
        }
    }

    private static async Task VerifyBlankBrowserThemeAsync(WebView2 browser, Color color)
    {
        var expected = $"rgb({color.R}, {color.G}, {color.B})";
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        string? actual;
        do
        {
            actual = JsonSerializer.Deserialize<string>(await browser.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.documentElement).backgroundColor"));
            if (actual == expected) break;
            await Task.Delay(20);
        } while (DateTime.UtcNow < deadline);
        Assert.Equal(expected, actual);
        Assert.Equal("about:blank", browser.CoreWebView2.Source);
        Assert.Equal(System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B), browser.DefaultBackgroundColor);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }

    private static IEnumerable<string> VisibleTexts(DependencyObject root) => TextElements(root).Select(text => text.Text);

    private static IEnumerable<TextBlock> TextElements(DependencyObject root)
    {
        if (root is TextBlock text) yield return text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var value in TextElements(VisualTreeHelper.GetChild(root, i))) yield return value;
    }

    private static void AssertVisibleInViewport(FrameworkElement element, FrameworkElement content)
    {
        var bounds = element.TransformToAncestor(content).TransformBounds(new Rect(element.RenderSize));
        Assert.True(bounds.Bottom <= content.ActualHeight, "开始按钮须在支持的窗口尺寸中保持可见。");
    }

    private static void VerifySoundScrolling(FocusAudioControls controls, FocusMediaViewModel media, string preview)
    {
        controls.UpdateLayout(); FlushBindings();
        var list = (ListBox)controls.FindName("SoundSelector");
        var scroll = (ScrollViewer)list.Template.FindName("PART_ScrollViewer", list);
        Assert.False(scroll.CanContentScroll);
        Assert.Equal(media.QuickSounds.Count(), list.Items.Count);
        Assert.All(list.Items.Cast<FocusAsset>(), sound => Assert.False(string.IsNullOrWhiteSpace(sound.Name)));
        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        scroll.ScrollToTop(); FlushBindings();
        var offset = scroll.VerticalOffset;
        scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        FlushBindings();
        Assert.True(scroll.VerticalOffset > offset);
        Assert.True(scroll.VerticalOffset - offset < first.ActualHeight * 2);
        scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120) { RoutedEvent = System.Windows.Input.Mouse.MouseWheelEvent });
        FlushBindings(); Assert.Equal(0, scroll.VerticalOffset);
        scroll.ScrollToEnd(); FlushBindings(); controls.UpdateLayout();
        var last = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(list.Items.Count - 1);
        var bounds = last.TransformToAncestor(scroll).TransformBounds(new Rect(last.RenderSize));
        Assert.InRange(bounds.Bottom, scroll.ViewportHeight - 1, scroll.ViewportHeight + 1);
        Assert.True(bounds.Top >= 0);
        SavePreview(controls, (Brush)Application.Current.FindResource("CardBrush"), preview, (int)Math.Ceiling(controls.ActualWidth), (int)Math.Ceiling(controls.ActualHeight));
        scroll.ScrollToTop(); FlushBindings();
    }

    private static void SavePreview(FrameworkElement content, Brush background, string filename, int width = 1240, int height = 750)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "FocusBloom.sln")))
        {
            var parent = Directory.GetParent(root);
            if (parent is null) return;
            root = parent.FullName;
        }
        var output = Path.Combine(root, "artifacts", "qa");
        Directory.CreateDirectory(output);
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var canvas = new DrawingVisual();
        using (var drawing = canvas.RenderOpen())
        {
            drawing.DrawRectangle(background, null, new Rect(0, 0, width, height));
            drawing.DrawImage(image, new Rect(0, 0, width, height));
        }
        var preview = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        preview.Render(canvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(preview));
        using var stream = File.Create(Path.Combine(output, filename));
        encoder.Save(stream);
    }
}
