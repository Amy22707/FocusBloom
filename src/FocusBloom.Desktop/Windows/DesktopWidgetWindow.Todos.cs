using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private void AddTodo(bool important = false, bool urgent = false)
    {
        if (_main.Todos.Folders.Count == 0) { _open(DesktopWidgetKind.Todos, null, null); return; }
        new TodoEditorWindow(_main, important: important, urgent: urgent, initialFolderId: Option.FolderId) { Owner = this }.ShowDialog();
    }
    private void DrawTodos()
    {
        Tool("＋", () => AddTodo());
        var rows = _manager.Data.Todos.Where(item => (Option.FolderId is null || item.Item.FolderId == Option.FolderId) && (Option.IncludeCompleted || !item.Completed)).OrderBy(item => item.Completed).ToArray();
        WidgetBody.Children.Add(Text((Option.FolderId is { } id ? _main.Todos.Folders.FirstOrDefault(folder => folder.Id == id)?.Name ?? "已删除的文件夹" : "全部文件夹") + $" · {rows.Length} 项", 10, true));
        if (Option.View == "四象限")
        {
            var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) }; grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new()); grid.ColumnDefinitions.Add(new()); grid.ColumnDefinitions.Add(new());
            foreach (var (important, urgent) in new[] { (false, true), (true, true), (false, false), (true, false) })
            {
                var panel = new DockPanel(); var header = new DockPanel { Margin = new Thickness(0, 0, 0, 7) }; DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
                var add = new Button { Content = "＋", Style = (Style)FindResource("WidgetTool") }; add.Click += (_, _) => AddTodo(important, urgent); DockPanel.SetDock(add, Dock.Right); header.Children.Add(add);
                var title = Text((important ? "重要" : "不重要") + " · " + (urgent ? "紧急" : "不紧急"), 10); header.Children.Add(title);
                var list = new StackPanel(); foreach (var row in rows.Where(item => item.Item.Important == important && item.Item.Urgent == urgent)) list.Children.Add(TodoCard(row, true));
                panel.Children.Add(new ScrollViewer { Content = list, Height = Math.Max(80, (WidgetViewport.ActualHeight - 126) / 2), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
                var card = Card(panel, 7); card.Margin = new Thickness(important ? 4 : 0, urgent ? 0 : 4, important ? 0 : 4, urgent ? 4 : 0); Grid.SetColumn(card, important ? 1 : 0); Grid.SetRow(card, urgent ? 0 : 1); grid.Children.Add(card);
            }
            WidgetBody.Children.Add(grid); return;
        }
        if (rows.Length == 0) { WidgetBody.Children.Add(Text("这里还没有待办，点击＋添加。", 12, true)); return; }
        if (Option.View == "平铺")
        {
            var grid = new UniformGrid { Columns = WidgetViewport.ActualWidth >= 470 ? 3 : WidgetViewport.ActualWidth >= 330 ? 2 : 1, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var row in rows) { var card = TodoCard(row, false); card.Margin = new Thickness(0, 0, 7, 7); grid.Children.Add(card); } WidgetBody.Children.Add(grid);
        }
        else
        {
            string? last = null;
            foreach (var row in Option.View == "按截止日期" ? rows.OrderBy(item => item.Completed).ThenBy(item => item.Item.DueDate is null).ThenBy(item => item.Item.DueDate).ThenBy(item => item.Item.DueTime).ToArray() : rows)
            {
                var heading = row.Completed ? "已完成" : row.Item.DueDate is { } date ? date.ToString("M月d日") : "无截止日期";
                if (Option.View == "按截止日期" && last != heading) { var label = Text(heading, 11, true); label.Margin = new Thickness(0, 10, 0, 7); WidgetBody.Children.Add(label); last = heading; }
                WidgetBody.Children.Add(TodoCard(row, true));
            }
        }
    }
    private Border TodoCard(TodoRow row, bool compact)
    {
        var content = new DockPanel(); var check = new CheckBox { IsChecked = row.Completed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 7, 0), Tag = row.Id, ToolTip = "标记完成或恢复待办" }; DockPanel.SetDock(check, Dock.Left); content.Children.Add(check);
        check.Click += (_, _) => { _store.SaveTodo(row.Item with { Status = check.IsChecked == true ? TodoStatus.Completed : TodoStatus.Pending }); _main.Todos.Reload(); };
        var body = new StackPanel(); var title = Text(row.Title, compact ? 11 : 13); title.FontWeight = row.Completed ? FontWeights.Normal : FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        if (!compact) { title.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; title.LineHeight = 18; title.MaxHeight = 36; }
        body.Children.Add(title); if (row.Item.DueDate is not null) { var due = Text(row.DueCompactText, 10, true); due.Margin = new Thickness(0, 5, 0, 0); if (row.Overdue) due.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); body.Children.Add(due); }
        if (!compact) { var folder = Text(_main.Todos.Folders.FirstOrDefault(item => item.Id == row.Item.FolderId)?.Name ?? "", 10, true); folder.TextWrapping = TextWrapping.NoWrap; folder.TextTrimming = TextTrimming.CharacterEllipsis; folder.Margin = new Thickness(0, 5, 0, 0); body.Children.Add(folder); }
        var open = new Button { Content = body, Tag = row.Id, ToolTip = row.Title + (row.Item.DueDate is not null ? "\n截止 " + row.DueText : "") }; open.SetResourceReference(StyleProperty, "CompletionDateButton"); open.Click += (_, _) => _open(DesktopWidgetKind.Todos, row.Id, null); content.Children.Add(open);
        var card = Card(content, compact ? 7 : 10); if (!compact) card.Height = 104; return card;
    }
}
