using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Statistics;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class StatisticsDetailsWindow : Window
{
    public StatisticsDetailsWindow(MainViewModel main, StatisticsViewModel model, StatisticsSnapshot data, string title)
    {
        Title = title; Width = 600; Height = 560; MinWidth = 440; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this);
        var root = new DockPanel { Margin = new Thickness(24) }; Content = root;
        var close = new Button { Content = "关闭", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(close, Dock.Bottom); root.Children.Add(close);
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; heading.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var summary = new TextBlock { Text = $"{StatisticsViewModel.Duration(data.Duration)} · {data.Count}次在此范围结束", FontSize = 12, Margin = new Thickness(0, 8, 0, 0) }; summary.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); heading.Children.Add(summary); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var items = new StackPanel(); root.Children.Add(new ScrollViewer { Content = items, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        if (data.Sessions.Count == 0) items.Children.Add(new TextBlock { Text = "这个范围没有对应记录。", Margin = new Thickness(0, 20, 0, 0) });
        foreach (var item in data.Sessions)
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = model.ProjectName(item.Record.ProjectId), FontSize = 16, FontWeight = FontWeights.SemiBold });
            var detail = $"{item.Record.StartedAt.LocalDateTime:yyyy-MM-dd HH:mm} — {item.Record.EndedAt.LocalDateTime:yyyy-MM-dd HH:mm}\n本范围投入 {StatisticsViewModel.Duration(item.IncludedDuration)} · 完整专注 {StatisticsViewModel.Duration(item.Record.FocusDuration)}";
            if (model.TodoName(item.Record.TodoId) is { Length: > 0 } todo) detail += "\n待办 · " + todo;
            if (item.Record.Strict) detail += "\n严格专注 · " + (item.Record.InterceptionCount is { } count ? $"拦截 {count} 次" : "拦截次数未统计");
            var label = new TextBlock { Text = detail, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(label);
            if (model.OutcomeText(item.Record.Id) is { Length: > 0 } outcome) panel.Children.Add(new TextBlock { Text = "成果 · " + outcome, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            var edit = new Button { Content = "查看详情／补记成果", Padding = new Thickness(10, 5, 10, 5), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0), Tag = item.Record.Id };
            edit.Click += (_, _) => { new OutcomeEditorWindow(main, item.Record.Id) { Owner = this }.ShowDialog(); DialogResult = true; }; panel.Children.Add(edit);
            var card = new Border { Child = panel, Padding = new Thickness(16), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
            card.SetResourceReference(Border.BackgroundProperty, "FieldBrush"); card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); items.Children.Add(card);
        }
    }
}
