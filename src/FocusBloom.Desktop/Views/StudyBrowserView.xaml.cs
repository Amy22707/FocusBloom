using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Media;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Core.Restrictions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FocusBloom.Desktop.Views;

public partial class StudyBrowserView : UserControl, IDisposable
{
    private readonly ObservableCollection<BrowserTab> _tabs = [];
    private Task<CoreWebView2Environment>? _environment;
    private readonly string _userDataFolder;
    private MainViewModel? _viewModel;
    public event EventHandler? ReturnToFocusRequested;
    public event EventHandler? FullscreenRequested;
    private bool _fullscreenPresentation;
    private BrowserTab? SelectedTab => BrowserTabs.SelectedItem as BrowserTab;

    public StudyBrowserView() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FocusBloom", "browser")) { }

    public StudyBrowserView(string userDataFolder)
    {
        _userDataFolder = userDataFolder;
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F11 || (e.Key == Key.Escape && _fullscreenPresentation))
            { e.Handled = true; FullscreenRequested?.Invoke(this, EventArgs.Empty); }
        };
        BrowserTabs.ItemsSource = _tabs;
        DataContextChanged += (_, e) =>
        {
            if (_viewModel is not null) _viewModel.RestrictionsChanged -= RestrictionsChanged;
            _viewModel = e.NewValue as MainViewModel;
            if (_viewModel is not null) _viewModel.RestrictionsChanged += RestrictionsChanged;
        };
        Loaded += async (_, _) => { if (_tabs.Count == 0) await CreateTabAsync("about:blank"); };
    }

    public async Task NavigateAsync(string address)
    {
        address = address.Trim();
        if (!address.Contains("://")) address = "https://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        { BrowserStatus.Text = "请输入 http 或 https 网址。"; return; }
        address = uri.AbsoluteUri;
        var tab = SelectedTab ?? await CreateTabAsync("about:blank");
        if (tab.Browser.CoreWebView2 is null) { BrowserStatus.Text = "浏览器正在准备，请稍候。"; return; }
        Navigate(tab, address);
    }

    public void SetFullscreenPresentation(bool fullscreen)
    {
        _fullscreenPresentation = fullscreen;
        PageHeading.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;
        BrowserFullscreenButton.ToolTip = fullscreen ? "退出浏览器全屏 · F11 / Esc" : "浏览器全屏 · F11";
        var focusFullscreen = fullscreen && _viewModel?.IsFocusActive == true;
        BrowserReturnToTimerButton.Content = focusFullscreen ? "← 返回全屏" : "← 返回计时";
        BrowserReturnToTimerButton.ToolTip = focusFullscreen ? "返回当前专注的全屏界面" : "返回当前计时界面";
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke(this, EventArgs.Empty);
    private void ReturnToTimer_Click(object sender, RoutedEventArgs e) => ReturnToFocusRequested?.Invoke(this, EventArgs.Empty);

    private async Task<BrowserTab> CreateTabAsync(string? address)
    {
        var tab = new BrowserTab();
        _tabs.Add(tab);
        BrowserTabs.SelectedItem = tab;
        var content = new Grid();
        tab.Content = content;
        content.Children.Add(tab.Browser);
        content.Children.Add(tab.BlockedPanel);
        ApplyBrowserTheme(tab);
        var blockedTitle = new TextBlock { FontSize = 22, FontWeight = FontWeights.SemiBold };
        blockedTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(MainViewModel.WebsiteBlockedMessage)));
        tab.BlockedPanel.Children.Add(blockedTitle);
        tab.BlockedPanel.Children.Add(tab.BlockedAddress);
        var returnButton = new Button { Content = "返回专注", Margin = new Thickness(0, 22, 0, 12), HorizontalAlignment = HorizontalAlignment.Left };
        returnButton.Click += (_, _) => ReturnToFocusRequested?.Invoke(this, EventArgs.Empty);
        tab.BlockedPanel.Children.Add(returnButton);
        tab.BlockedPanel.Children.Add(new TextBlock { Text = "在设置中管理网站规则，新列表在下一次严格专注生效。", TextWrapping = TextWrapping.Wrap });
        SelectTab();
        try
        {
            _environment ??= CoreWebView2Environment.CreateAsync(userDataFolder: _userDataFolder);
            await tab.Browser.EnsureCoreWebView2Async(await _environment);
            if (!_tabs.Contains(tab)) return tab;
            var core = tab.Browser.CoreWebView2;
            core.NavigationStarting += (_, e) =>
            {
                if (_viewModel?.CanNavigateWebsite(e.Uri) == false)
                {
                    e.Cancel = true;
                    Block(tab, e.Uri);
                    return;
                }
                ApplyBrowserTheme(tab, e.Uri);
            };
            core.FrameNavigationStarting += (_, e) =>
            {
                if (_viewModel?.CanNavigateWebsite(e.Uri) != false) return;
                e.Cancel = true;
                BrowserStatus.Text = $"嵌入页面未放行：{e.Uri}";
            };
            core.SourceChanged += (_, _) =>
            {
                if (tab.BlockedUri is not null) return;
                ApplyBrowserTheme(tab);
                tab.Address = core.Source;
                if (tab == SelectedTab) AddressBox.Text = tab.Address;
            };
            core.DocumentTitleChanged += (_, _) => { if (tab.BlockedUri is null) tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "新标签页" : core.DocumentTitle; };
            core.NewWindowRequested += async (_, e) =>
            {
                using var deferral = e.GetDeferral();
                e.Handled = true;
                var target = string.IsNullOrWhiteSpace(e.Uri) ? "about:blank" : e.Uri;
                if (_viewModel?.CanNavigateWebsite(target) == false) { Block(tab, target); return; }
                var popup = await CreateTabAsync(null);
                if (popup.Browser.CoreWebView2 is not null) e.NewWindow = popup.Browser.CoreWebView2;
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (tab.BlockedUri is not null) return;
                ApplyBrowserTheme(tab);
                if (e.IsSuccess) tab.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? "新标签页" : core.DocumentTitle;
                BrowserStatus.Text = e.IsSuccess ? "页面已加载" : $"页面加载未完成：{e.WebErrorStatus}";
            };
            core.WindowCloseRequested += async (_, _) => await CloseTabAsync(tab);
            if (address is not null) Navigate(tab, address);
            else ApplyBrowserTheme(tab);
        }
        catch (Exception ex) { BrowserStatus.Text = $"学习浏览器未能启动：{ex.Message}。请确认已安装 Microsoft Edge WebView2 Runtime。"; }
        return tab;
    }

    private void Navigate(BrowserTab tab, string address)
    {
        tab.Address = address;
        if (_viewModel?.CanNavigateWebsite(address) == false) { Block(tab, address); return; }
        tab.BlockedUri = null;
        tab.BlockedPanel.Visibility = Visibility.Collapsed;
        tab.Browser.Visibility = Visibility.Visible;
        tab.Browser.CoreWebView2?.Navigate(address);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty)
            foreach (var tab in _tabs) ApplyBrowserTheme(tab);
    }

    private void ApplyBrowserTheme(BrowserTab tab, string? address = null)
    {
        if (Background is not SolidColorBrush brush) return;
        var blank = (address ?? tab.Browser.CoreWebView2?.Source ?? "about:blank") == "about:blank";
        var color = brush.Color;
        tab.Browser.DefaultBackgroundColor = blank
            ? System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B)
            : System.Drawing.Color.White;
        if (blank && tab.Browser.CoreWebView2 is not null) _ = ApplyBlankPageThemeAsync(tab);
    }

    private async Task ApplyBlankPageThemeAsync(BrowserTab tab)
    {
        if (tab.Browser.CoreWebView2 is not { } core || Background is not SolidColorBrush brush) return;
        var color = brush.Color;
        var scheme = Appearance.IsDark ? "dark" : "light";
        try
        {
            await core.ExecuteScriptAsync($"if (location.href === 'about:blank' && document.documentElement) {{ document.documentElement.style.backgroundColor = '#{color.R:X2}{color.G:X2}{color.B:X2}'; document.documentElement.style.colorScheme = '{scheme}'; }}");
        }
        catch (InvalidOperationException) { }
        catch (System.Runtime.InteropServices.COMException) { }
    }

    private void Block(BrowserTab tab, string address)
    {
        _viewModel?.RegisterInterception();
        tab.Browser.CoreWebView2?.Stop();
        tab.BlockedUri = address;
        tab.Address = address;
        tab.Title = "网站未放行";
        tab.BlockedAddress.Text = address;
        tab.Browser.Visibility = Visibility.Collapsed;
        tab.BlockedPanel.Visibility = Visibility.Visible;
        if (tab == SelectedTab) { AddressBox.Text = address; BrowserStatus.Text = "网站未放行 · 可返回专注或选择允许网站"; }
    }

    private void RestrictionsChanged(object? sender, EventArgs e)
    {
        foreach (var tab in _tabs.ToArray())
        {
            if (tab.Browser.CoreWebView2 is null) continue;
            var address = tab.BlockedUri ?? tab.Browser.CoreWebView2.Source;
            if (_viewModel?.CanNavigateWebsite(address) == false) Block(tab, address);
            else if (tab.BlockedUri is not null) Navigate(tab, address);
        }
        SelectTab();
    }

    private void SelectTab()
    {
        TabContent.Content = SelectedTab?.Content;
        AddressBox.Text = SelectedTab?.Address ?? "";
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e) => await NavigateAsync(AddressBox.Text);
    private async void OpenAllowedWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (AllowedWebsiteSelector.SelectedItem is WebsiteRule rule) await NavigateAsync("https://" + rule.Domain);
        else BrowserStatus.Text = "请先在设置中添加允许网站。";
    }
    private void AllowedWebsiteSelector_TargetUpdated(object sender, DataTransferEventArgs e)
    {
        if (AllowedWebsiteSelector.SelectedItem is null && AllowedWebsiteSelector.Items.Count > 0) AllowedWebsiteSelector.SelectedIndex = 0;
    }
    private async void AddressBox_KeyDown(object sender, KeyEventArgs e)
    { if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(AddressBox.Text); } }
    private async void NewTab_Click(object sender, RoutedEventArgs e) => await CreateTabAsync("about:blank");
    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectTab();
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTab is not { } tab || tab.Browser.CoreWebView2 is not { } core) return;
        if (tab.BlockedUri is not null)
        {
            tab.BlockedUri = null;
            tab.BlockedPanel.Visibility = Visibility.Collapsed;
            tab.Browser.Visibility = Visibility.Visible;
            if (_viewModel?.CanNavigateWebsite(core.Source) != false) { Navigate(tab, core.Source); return; }
        }
        if (core.CanGoBack) core.GoBack();
    }
    private void Forward_Click(object sender, RoutedEventArgs e) { if (SelectedTab?.Browser.CoreWebView2?.CanGoForward == true) SelectedTab.Browser.CoreWebView2.GoForward(); }
    private void Reload_Click(object sender, RoutedEventArgs e) { if (SelectedTab is { } tab) Navigate(tab, tab.Address); }
    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).Tag is not BrowserTab tab) return;
        await CloseTabAsync(tab);
    }

    private async Task CloseTabAsync(BrowserTab tab)
    {
        _tabs.Remove(tab);
        tab.Browser.Dispose();
        BrowserTabs.SelectedItem = _tabs.FirstOrDefault();
        if (_tabs.Count == 0) await CreateTabAsync("about:blank");
    }

    public void Dispose()
    {
        if (_viewModel is not null) _viewModel.RestrictionsChanged -= RestrictionsChanged;
        foreach (var tab in _tabs) tab.Browser.Dispose();
        _tabs.Clear();
    }

    private sealed class BrowserTab : ObservableObject
    {
        private string _title = "新标签页";
        public string Title { get => _title; set { _title = value; Notify(); } }
        public string Address { get; set; } = "about:blank";
        public string? BlockedUri { get; set; }
        public Grid? Content { get; set; }
        public WebView2 Browser { get; } = new();
        public StackPanel BlockedPanel { get; } = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(32), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        public TextBlock BlockedAddress { get; } = new() { Margin = new Thickness(0, 14, 0, 0), TextWrapping = TextWrapping.Wrap, MaxWidth = 600 };
    }
}
