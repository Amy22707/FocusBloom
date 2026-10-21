using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.IO;
using Microsoft.Win32;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class DesktopWidgetSettingsWindow : Window
{
    private sealed record Scope(Guid? Id, string Name);
    public DesktopWidgetSettingsWindow(MainViewModel main, DesktopWidgetOption option)
    {
        Title = option.Name + " · 组件设置"; Width = 470; Height = Math.Min(660, SystemParameters.WorkArea.Height - 60); MinWidth = 390; MinHeight = 350; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this); DataContext = option; NameScope.SetNameScope(this, new NameScope());
        SourceInitialized += (_, _) => { if (Owner is DesktopWidgetWindow widget) Closed += (_, _) => widget.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(widget.ApplyWindowLevel)); };
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var done = new Button { Content = "完成", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; done.SetResourceReference(StyleProperty, "PrimaryButton"); DockPanel.SetDock(done, Dock.Bottom); root.Children.Add(done);
        var fields = new StackPanel(); root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        void Caption(string text) { var label = new TextBlock { Text = text, FontSize = 12, Margin = new Thickness(0, 12, 0, 7), TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); fields.Children.Add(label); }
        void BindCheck(string name, string text, string property) { var check = new CheckBox { Content = text, Margin = new Thickness(0, 8, 0, 6) }; check.SetBinding(CheckBox.IsCheckedProperty, new Binding(property) { Mode = BindingMode.TwoWay }); RegisterName(name, check); fields.Children.Add(check); }
        fields.Children.Add(new TextBlock { Text = option.Name, FontSize = 22, FontWeight = FontWeights.SemiBold }); Caption("选择即时保存。组件可独立拖动和缩放，主窗口进入托盘后继续显示。");
        Caption("显示方式"); var view = new ComboBox { ItemsSource = option.Views }; view.SetBinding(ComboBox.SelectedItemProperty, new Binding("View") { Mode = BindingMode.TwoWay }); RegisterName("WidgetViewSelector", view); fields.Children.Add(view);
        BindCheck("WidgetTopmostInput", "置顶于其他窗口", "Topmost"); Caption("透明度：0%不透明，70%最透明");
        var transparency = new Slider { Minimum = 0, Maximum = 70, TickFrequency = 5, IsSnapToTickEnabled = true }; transparency.SetBinding(Slider.ValueProperty, new Binding("Transparency") { Mode = BindingMode.TwoWay }); RegisterName("WidgetTransparencySlider", transparency); fields.Children.Add(transparency);
        Caption("组件背景"); var background = new ComboBox { ItemsSource = option.BackgroundModes }; background.SetBinding(ComboBox.SelectedItemProperty, new Binding("BackgroundMode") { Mode = BindingMode.TwoWay }); RegisterName("WidgetBackgroundSelector", background); fields.Children.Add(background);
        var colors = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) }; fields.Children.Add(colors);
        foreach (var hex in new[] { "#EDE7F3", "#F5E2E6", "#E2EDDF", "#E0EBF4", "#F5EAD6", "#FFFFFF", "#28313E", "#3C3448", "#29443E" })
        {
            var swatch = new Button { Width = 30, Height = 30, Padding = new Thickness(0), Margin = new Thickness(0, 0, 8, 7), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)), ToolTip = hex };
            swatch.Click += (_, _) => { option.BackgroundColor = hex; option.BackgroundMode = "纯色"; }; colors.Children.Add(swatch);
        }
        var custom = new DockPanel(); var apply = new Button { Content = "应用颜色", Margin = new Thickness(8, 0, 0, 0) }; DockPanel.SetDock(apply, Dock.Right); custom.Children.Add(apply);
        var colorInput = new TextBox { ToolTip = "颜色代码，例如 #EDE7F3" }; colorInput.SetBinding(TextBox.TextProperty, new Binding("BackgroundColor") { Mode = BindingMode.OneWay }); RegisterName("WidgetColorInput", colorInput); custom.Children.Add(colorInput); fields.Children.Add(custom);
        apply.Click += (_, _) => { try { var color = (Color)ColorConverter.ConvertFromString(colorInput.Text.Trim()); option.BackgroundColor = $"#{color.R:X2}{color.G:X2}{color.B:X2}"; option.BackgroundMode = "纯色"; } catch (Exception ex) when (ex is FormatException or NotSupportedException) { ConfirmDeletionWindow.ShowNotice(this, "请输入有效的颜色代码，例如 #EDE7F3。"); } };
        var pickImage = new Button { Content = "选择背景图片…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 5) }; RegisterName("WidgetImageButton", pickImage); fields.Children.Add(pickImage);
        var imageName = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 11 }; imageName.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); imageName.SetBinding(TextBlock.TextProperty, new Binding("BackgroundImage") { TargetNullValue = "尚未选择图片；图片背景自动搭配深色遮罩。" }); fields.Children.Add(imageName);
        pickImage.Click += (_, _) => { var picker = new OpenFileDialog { Filter = "背景图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp", CheckFileExists = true }; if (picker.ShowDialog(this) == true) { if (FocusMediaViewModel.LoadImage(picker.FileName, 80) is null) { ConfirmDeletionWindow.ShowNotice(this, "无法读取这张图片，请选择 PNG、JPG、BMP 或 GIF 图片。"); return; } option.BackgroundImage = Path.GetFullPath(picker.FileName); option.BackgroundMode = "图片"; } };
        Caption("宽度"); var width = new Slider { Minimum = option.MinimumWidth, Maximum = 1000, TickFrequency = 10, IsSnapToTickEnabled = true }; width.SetBinding(Slider.ValueProperty, new Binding("Width") { Mode = BindingMode.TwoWay }); RegisterName("WidgetWidthSlider", width); fields.Children.Add(width);
        Caption("高度"); var height = new Slider { Minimum = option.MinimumHeight, Maximum = 1000, TickFrequency = 10, IsSnapToTickEnabled = true }; height.SetBinding(Slider.ValueProperty, new Binding("Height") { Mode = BindingMode.TwoWay }); RegisterName("WidgetHeightSlider", height); fields.Children.Add(height);
        if (option.Kind == DesktopWidgetKind.Focus)
        {
            Caption("统计范围"); var period = new ComboBox { ItemsSource = new[] { "日", "周", "月", "年" } }; period.SetBinding(ComboBox.SelectedItemProperty, new Binding("Period") { Mode = BindingMode.TwoWay }); fields.Children.Add(period);
            Caption("专注项目"); var projects = new[] { new Scope(null, "全部项目") }.Concat(main.Calendar.Store.GetProjects(true).Select(item => new Scope(item.Id, item.Category + " / " + item.Name))).ToArray();
            var project = new ComboBox { ItemsSource = projects, DisplayMemberPath = "Name", SelectedItem = projects.FirstOrDefault(item => item.Id == option.ProjectId) ?? projects[0] }; project.SelectionChanged += (_, _) => option.ProjectId = ((Scope)project.SelectedItem).Id; RegisterName("WidgetProjectSelector", project); fields.Children.Add(project);
        }
        if (option.Kind == DesktopWidgetKind.Todos)
        {
            Caption("待办文件夹"); var scopes = new[] { new Scope(null, "全部文件夹") }.Concat(main.Todos.Folders.Select(item => new Scope(item.Id, item.Name))).ToList();
            if (option.FolderId is { } missing && scopes.All(item => item.Id != missing)) scopes.Add(new(missing, "已删除的文件夹"));
            var scope = new ComboBox { ItemsSource = scopes, DisplayMemberPath = "Name", SelectedItem = scopes.Single(item => item.Id == option.FolderId) }; scope.SelectionChanged += (_, _) => option.FolderId = ((Scope)scope.SelectedItem).Id; RegisterName("WidgetFolderSelector", scope); fields.Children.Add(scope);
            BindCheck("WidgetCompletedInput", "显示已完成待办（置于最后）", "IncludeCompleted");
        }
        if (option.Kind is DesktopWidgetKind.Dates or DesktopWidgetKind.Habits)
        {
            BindCheck("WidgetAllItemsInput", "显示全部事项", "AllItems"); Caption("关闭“显示全部事项”后，勾选要展示的内容：");
            var selection = new StackPanel(); selection.SetBinding(IsEnabledProperty, new Binding("AllItems") { Converter = new InverseBooleanConverter() });
            var choices = option.Kind == DesktopWidgetKind.Dates ? main.DateEvents.Items.Select(item => new Scope(item.Item.Id, item.Title)) : main.Goals.Habits.Select(item => new Scope(item.Id, item.Name));
            foreach (var item in choices) { var check = new CheckBox { Content = item.Name, Tag = item.Id, IsChecked = (option.Preferences.ItemIds ?? Array.Empty<Guid>()).Contains(item.Id!.Value), Margin = new Thickness(0, 5, 0, 5) }; check.Checked += (_, _) => option.SelectItem(item.Id!.Value, true); check.Unchecked += (_, _) => option.SelectItem(item.Id!.Value, false); selection.Children.Add(check); }
            fields.Children.Add(new ScrollViewer { Content = selection, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); RegisterName("WidgetItemSelection", selection);
        }
        if (option.Kind == DesktopWidgetKind.Dates) BindCheck("WidgetCoversInput", "卡片显示封面", "ShowCovers");
        if (option.Kind == DesktopWidgetKind.Dates)
        {
            Caption("日期分组 · 卡片、列表混合显示所选分组，分组视图按列显示");
            var all = new CheckBox { Content = "显示全部分组", IsChecked = option.AllDateGroups, Margin = new Thickness(0, 5, 0, 8) }; RegisterName("WidgetAllDateGroups", all); fields.Children.Add(all);
            var groups = new StackPanel { IsEnabled = !option.AllDateGroups }; RegisterName("WidgetDateGroups", groups);
            foreach (var name in main.DateEvents.GroupNames)
            {
                var group = new CheckBox { Content = name, Tag = name, IsChecked = option.IncludesGroup(name), Margin = new Thickness(0, 5, 0, 5) };
                group.Checked += (_, _) => option.SelectDateGroup(name, true); group.Unchecked += (_, _) => option.SelectDateGroup(name, false); groups.Children.Add(group);
            }
            all.Checked += (_, _) => { option.AllDateGroups = true; groups.IsEnabled = false; };
            all.Unchecked += (_, _) => { option.AllDateGroups = false; foreach (var group in groups.Children.OfType<CheckBox>().Where(group => group.IsChecked == true)) option.SelectDateGroup((string)group.Tag, true); groups.IsEnabled = true; };
            fields.Children.Add(groups);
        }
    }
    private sealed class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is not true;
    }
}
