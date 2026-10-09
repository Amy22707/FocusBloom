using System.IO;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
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
                var folderInput = (TextBox)window.FindName("FolderNameInput");
                var addFolder = (Button)window.FindName("AddFolderButton");
                folderInput.Text = "学习";
                FlushBindings();
                addFolder.Command.Execute(null);
                var projectInput = (TextBox)window.FindName("ProjectNameInput");
                var addProject = (Button)window.FindName("AddProjectButton");
                projectInput.Text = "英语阅读";
                FlushBindings();
                addProject.Command.Execute(null);
                projectInput.Text = "高等数学";
                FlushBindings();
                addProject.Command.Execute(null);
                folderInput.Text = "生活";
                FlushBindings();
                addFolder.Command.Execute(null);
                projectInput.Text = "阅读";
                FlushBindings();
                addProject.Command.Execute(null);
                folderInput.Text = "工作";
                FlushBindings();
                addFolder.Command.Execute(null);
                Assert.Empty(viewModel.FolderProjects);
                var folders = (ListBox)window.FindName("FolderList");
                folders.SelectedItem = viewModel.Folders.Single(f => f.Name == "学习");
                FlushBindings();
                Assert.Equal(2, viewModel.FolderProjects.Count);
                Assert.Equal("英语阅读", viewModel.SelectedProject!.Name);
                content.UpdateLayout();
                SavePreview(content, window.Background, "projects-page.png");

                tabs.SelectedIndex = 0;
                content.UpdateLayout();
                FlushBindings();
                var mode = (ComboBox)window.FindName("TimerModeSelector");
                Assert.Equal(2, mode.Items.Count);
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
                tabs.SelectedIndex = 3;
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
                tabs.SelectedIndex = 2;
                content.UpdateLayout();
                FlushBindings();
                SavePreview(content, window.Background, "records-page.png");
                tabs.SelectedIndex = 0;
                content.Measure(new Size(1000, 630));
                content.Arrange(new Rect(0, 0, 1000, 630));
                content.UpdateLayout();
                AssertVisibleInViewport(start, content);
                mode.SelectedIndex = 1;
                viewModel.DurationMinutes = "0.1";
                viewModel.RestMinutes = "1";
                FlushBindings();
                start.Command.Execute(null);
                enterFullscreen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Visibility.Visible, fullscreen.Visibility);
                clock.Advance(TimeSpan.FromSeconds(10));
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
                Assert.Equal(TimeSpan.FromSeconds(6), countdown.FocusDuration);
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
                RunWithDispatcher(() => VerifyWindowRestrictionAsync(window, viewModel, directory));
                RunWithDispatcher(() => VerifyBrowserAsync(window, viewModel, directory));
                VerifyTimerPreferences(window, viewModel, store, clock);
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

    private static void VerifyFocusFonts(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, FrameworkElement content)
    {
        var typography = viewModel.Typography;
        var image = viewModel.Media.Background; var quote = viewModel.Media.QuoteText; var timer = viewModel.TimerText;
        var tabs = (TabControl)window.FindName("MainTabs"); tabs.SelectedIndex = 3;
        content.UpdateLayout(); FlushBindings();
        var settings = (FocusFontSettingsView)((Border)window.FindName("FocusFontSettingsCard")).Child;
        Assert.Same(typography, settings.DataContext);
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

    private static void VerifyFocusMedia(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, ManualClock clock, string directory)
    {
        var media = viewModel.Media;
        Assert.Equal(6, media.Images.Count);
        Assert.Equal(6, media.SoundChoices.Count);
        Assert.All(media.Images, image => Assert.NotNull(image.Thumbnail));
        Assert.All(media.SoundChoices.Where(sound => sound.File.Length > 0), sound => Assert.True(File.Exists(sound.File)));
        var localFolder = Path.Combine(directory, "pictures");
        Directory.CreateDirectory(localFolder);
        var importedPath = Path.Combine(localFolder, "my-background.jpg");
        File.Copy(media.Images[0].Asset.File, importedPath);
        media.ImportFolder(localFolder);
        var invalidPath = Path.Combine(directory, "invalid.png");
        File.WriteAllText(invalidPath, "not an image");
        media.ImportImages([invalidPath]);
        Assert.DoesNotContain(media.Images, image => image.Asset.File == invalidPath);

        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 3;
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
        media.RemoveSound(removedSound); Assert.False(media.IsPlaying); Assert.True(media.NoSound);
        var favoriteQuote = media.Quotes.First(card => !card.Custom);
        favoriteQuote.Favorite = true; media.ShowFavoriteQuotes = true; Assert.Single(media.VisibleQuotes);
        media.QuoteFavoritesOnly = true; media.SelectedQuote = media.QuoteChoices[0];
        viewModel.StartCommand.Execute(null); Assert.Equal(favoriteQuote.Text, media.QuoteText);
        media.RemoveQuote(favoriteQuote.Quote); Assert.Equal(favoriteQuote.Text, media.QuoteText);
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
            Assert.DoesNotContain(restored.Sounds, sound => sound.Asset.Id == "rain");
            Assert.DoesNotContain(restored.Quotes, card => card.Quote.Id == favoriteQuote.Quote.Id);
            Assert.True(restored.Quotes.Single(card => card.Quote.Id == savedFavoriteQuote.Quote.Id).Favorite);
            Assert.True(restored.Sounds.Single(sound => sound.Asset.Id == "brown").Favorite);
        }
        media.RestoreSounds(); media.RestoreQuotes(); media.RestoreImages();
        Assert.Equal(5, media.Sounds.Count); Assert.Equal(25, media.Quotes.Count(card => !card.Custom));
        foreach (var card in media.Quotes.ToArray()) media.RemoveQuote(card.Quote);
        viewModel.StartCommand.Execute(null); Assert.Equal("", media.QuoteText); viewModel.EndCommand.Execute(null);
        media.RestoreQuotes(); media.QuoteFavoritesOnly = false;
        media.ShowFavoriteQuotes = false; media.ShowFavoriteSounds = false;
        media.Dispose();
    }

    private static IEnumerable<Button> FindButtons(DependencyObject root)
    {
        if (root is Button button) yield return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in FindButtons(VisualTreeHelper.GetChild(root, index))) yield return child;
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
    private static void VerifyTimerPreferences(MainWindow window, MainViewModel viewModel, SqliteFocusStore store, ManualClock clock)
    {
        ((TabControl)window.FindName("MainTabs")).SelectedIndex = 3;
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
        using var source = new HwndSource(new HwndSourceParameters("FocusBloom window verification")
        {
            Width = 120, Height = 80, PositionX = -10000, PositionY = -10000,
            WindowStyle = 0x10CF0000, ExtendedWindowStyle = 0x08000080
        });
        using var blockingSource = new HwndSource(new HwndSourceParameters("FocusBloom blocking interface verification")
        {
            Width = 120, Height = 80, PositionX = -10000, PositionY = -10000,
            WindowStyle = 0x10CF0000, ExtendedWindowStyle = 0x80
        });
        var pulses = 0;
        using var heartbeat = new System.Threading.Timer(_ => Interlocked.Increment(ref pulses), null, 0, 20);
        GetWindowRect(source.Handle, out var originalBounds);
        var previousForeground = GetForegroundWindow();
        try
        {
            Assert.True(ApplicationRestrictionService.ActivateForegroundWindow(blockingSource.Handle));
            await WaitForAsync(() => GetForegroundWindow() == blockingSource.Handle);
            Assert.False(IsIconic(source.Handle));
            GetWindowRect(source.Handle, out var bounds);
            Assert.Equal(originalBounds, bounds);
            var before = Volatile.Read(ref pulses);
            await Task.Delay(120);
            Assert.True(Volatile.Read(ref pulses) > before);
            var fixtureScript = Path.Combine(directory, "restricted-window.ps1");
            File.WriteAllText(fixtureScript, """
                Add-Type -AssemblyName System.Windows.Forms
                $form = New-Object System.Windows.Forms.Form
                $form.Text = 'FocusBloom restriction test'
                $form.StartPosition = 'Manual'
                $form.Location = New-Object System.Drawing.Point(-10000,-10000)
                $form.Size = New-Object System.Drawing.Size(160,100)
                [Console]::WriteLine($form.Handle.ToInt64())
                [System.Windows.Forms.Application]::Run($form)
                """, System.Text.Encoding.ASCII);
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            using var fixture = Process.Start(new ProcessStartInfo
            {
                FileName = powershell,
                ArgumentList = { "-NoProfile", "-STA", "-ExecutionPolicy", "Bypass", "-File", fixtureScript },
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            var previousMode = viewModel.Whitelist.ApplicationMode;
            try
            {
                var handleLine = await fixture.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
                var fixtureWindow = new IntPtr(long.Parse(handleLine!));
                await WaitForAsync(() => IsWindowVisible(fixtureWindow));
                GetWindowRect(fixtureWindow, out var fixtureBounds);
                viewModel.Whitelist.ApplicationMode = viewModel.Whitelist.ListModes.Single(choice => choice.Mode == AccessListMode.Blacklist);
                viewModel.Whitelist.AddApplication(new AllowedApplication("测试窗口", powershell));
                viewModel.SelectedTimerMode = viewModel.TimerModes[0];
                viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
                viewModel.RestMinutes = "0";
                viewModel.StartCommand.Execute(null);
                ApplicationRestrictionService.ActivateForegroundWindow(fixtureWindow);
                monitor.CheckForeground();
                await WaitForAsync(() => typeof(ApplicationRestrictionService).GetField("_blockedWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor) is ApplicationBlockedWindow { IsVisible: true });
                var blocked = (ApplicationBlockedWindow)typeof(ApplicationRestrictionService).GetField("_blockedWindow", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
                Assert.True(blocked.IsVisible);
                ((Button)blocked.FindName("ReturnToFocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitForAsync(() => GetForegroundWindow() == new WindowInteropHelper(window).Handle);
                Assert.False(blocked.IsVisible);
                var queuedEvent = (Delegate)typeof(ApplicationRestrictionService).GetField("_callback", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(monitor)!;
                queuedEvent.DynamicInvoke(IntPtr.Zero, (uint)3, fixtureWindow, 0, 0, (uint)0, (uint)0);
                monitor.CheckForeground();
                FlushBindings();
                Assert.False(blocked.IsVisible);
                Assert.True(viewModel.RestrictionsActive);
                Assert.Equal(0, ((TabControl)window.FindName("MainTabs")).SelectedIndex);
                ApplicationRestrictionService.ActivateForegroundWindow(fixtureWindow);
                monitor.CheckForeground();
                await WaitForAsync(() => blocked.IsVisible);
                Assert.False(IsIconic(fixtureWindow));
                GetWindowRect(fixtureWindow, out var afterReturnBounds);
                Assert.Equal(fixtureBounds, afterReturnBounds);
                ((Button)blocked.FindName("ReturnToFocusButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                viewModel.EndCommand.Execute(null);
                Assert.False(blocked.IsVisible);
            }
            finally
            {
                if (viewModel.IsFocusActive) viewModel.EndCommand.Execute(null);
                viewModel.Whitelist.ApplicationMode = previousMode;
                viewModel.SelectedRestrictionMode = viewModel.RestrictionModes[0];
                if (!fixture.HasExited) fixture.Kill();
                await fixture.WaitForExitAsync();
            }
        }
        finally { if (previousForeground != IntPtr.Zero) ApplicationRestrictionService.ActivateForegroundWindow(previousForeground); }
    }

    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }

    private static async Task VerifyBrowserAsync(MainWindow window, MainViewModel viewModel, string directory)
    {
        using var server = new BrowserTestServer();
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
            await browser.NavigateAsync(server.DeniedUrl);
            await WaitForAsync(() => webview.CoreWebView2.Source == server.DeniedUrl && webview.CoreWebView2.DocumentTitle == "Learning fixture");
            viewModel.Whitelist.Websites.Clear();
            viewModel.Whitelist.Websites.Add(new WebsiteRule("127.0.0.1", false));
            viewModel.SelectedRestrictionMode = viewModel.RestrictionModes.Single(choice => choice.Strict);
            viewModel.StartCommand.Execute(null);
            Assert.Equal(Visibility.Collapsed, webview.Visibility);
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
            await Task.WhenAny(exited.Task, Task.Delay(5000));
        }
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
