using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class DesktopWidgetWindow
{
    private void DrawDates()
    {
        var rows = _manager.Data.Dates.Where(item => Option.Includes(item.Item.Id)).ToArray();
        if (Option.View == "月历") { MonthTools(); DrawDateCalendar(rows); return; }
        Tool("＋", () => new DateEventEditorWindow(_main) { Owner = this }.ShowDialog());
        if (rows.Length == 0) { WidgetBody.Children.Add(Text("没有选中的日期事项，可在组件设置里选择或点击＋添加。", 12, true)); return; }
        if (Option.View == "大字")
        {
            _page %= rows.Length; var row = rows[_page]; Tool("‹", () => { _page = (_page + rows.Length - 1) % rows.Length; Render(); }); Tool("›", () => { _page = (_page + 1) % rows.Length; Render(); });
            var body = new StackPanel(); var title = Text(row.Title, 17); title.FontWeight = FontWeights.SemiBold; title.TextAlignment = TextAlignment.Center; body.Children.Add(title);
            var label = Text(row.Label, 12, true); label.Margin = new Thickness(0, 12, 0, 0); label.TextAlignment = TextAlignment.Center; body.Children.Add(label);
            var number = Text(row.Number, 58); number.FontWeight = FontWeights.Light; number.TextAlignment = TextAlignment.Center; number.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); body.Children.Add(new Viewbox { Child = number, MaxHeight = 80, StretchDirection = StretchDirection.DownOnly });
            var date = Text((row.Number == "—" ? "" : "天 · ") + row.Detail, 11, true); date.TextAlignment = TextAlignment.Center; body.Children.Add(date); body.Children.Add(Text($"{_page + 1} / {rows.Length}", 10, true));
            var open = new Button { Content = body, Tag = row.Item.Id, Margin = new Thickness(0, 8, 0, 0) }; open.SetResourceReference(StyleProperty, "CompletionDateButton"); open.Click += (_, _) => _open(DesktopWidgetKind.Dates, row.Item.Id, row.Next ?? row.Item.Date); WidgetBody.Children.Add(open); return;
        }
        var list = Option.View == "卡片" ? (Panel)new UniformGrid { Columns = WidgetViewport.ActualWidth >= 470 ? 2 : 1 } : new StackPanel();
        foreach (var row in rows)
        {
            var body = new StackPanel();
            if (Option.View == "卡片" && Option.ShowCovers && row.Cover is not null)
            {
                var image = new Image { Height = 78, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 0, 8), ClipToBounds = true }; image.SetBinding(Image.SourceProperty, new Binding(nameof(DateEventRow.Thumbnail)) { Source = row }); image.SetBinding(VisibilityProperty, new Binding(nameof(DateEventRow.HasCover)) { Source = row, Converter = (IValueConverter)FindResource("BoolVisibility") }); body.Children.Add(image);
            }
            var title = Text(row.Title, 13); title.FontWeight = FontWeights.SemiBold; title.TextTrimming = TextTrimming.CharacterEllipsis; title.TextWrapping = TextWrapping.NoWrap; body.Children.Add(title);
            var number = Text(row.Number == "—" ? row.Label : row.Label + " " + row.Number + " 天", Option.View == "卡片" ? 24 : 17); number.Margin = new Thickness(0, 7, 0, 5); number.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); body.Children.Add(number); body.Children.Add(Text(row.Detail, 10, true));
            var open = new Button { Content = body, Tag = row.Item.Id, ToolTip = row.Title + "\n" + row.Notes }; open.SetResourceReference(StyleProperty, "CompletionDateButton"); open.Click += (_, _) => _open(DesktopWidgetKind.Dates, row.Item.Id, row.Next ?? row.Item.Date);
            var card = Card(open); card.Margin = new Thickness(0, 0, Option.View == "卡片" ? 7 : 0, 8); list.Children.Add(card);
        }
        WidgetBody.Children.Add(list);
    }
    private void DrawDateCalendar(DateEventRow[] rows)
    {
        MonthPicker(); var first = new DateOnly(_anchor.Year, _anchor.Month, 1); var last = new DateOnly(first.Year, first.Month, DateTime.DaysInMonth(first.Year, first.Month)); var start = CalendarRules.Monday(first);
        var occurrences = rows.SelectMany(row => (row.Item.Repeat == DateRepeat.None ? new[] { row.Item.Date }.Where(day => day >= first && day <= last) : CalendarRules.Occurrences(row.Item, first, last)).Select(day => (day, row))).ToArray();
        var count = (last.DayNumber - start.DayNumber + 7) / 7 * 7;
        var weekdays = new UniformGrid { Columns = 7, Height = 18 }; foreach (var name in new[] { "一", "二", "三", "四", "五", "六", "日" }) { var label = Text(name, 10, true); label.TextAlignment = TextAlignment.Center; weekdays.Children.Add(label); } WidgetBody.Children.Add(weekdays);
        var rowsCount = count / 7; var grid = new UniformGrid { Columns = 7, Rows = rowsCount }; var height = Math.Clamp((WidgetViewport.ActualHeight - 64) / rowsCount - 4, 28, 44);
        for (var index = 0; index < count; index++)
        {
            var day = start.AddDays(index); var events = occurrences.Where(item => item.day == day).Select(item => item.row).ToArray();
            var body = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; var label = Text(day.Day.ToString(), 11); label.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; label.LineHeight = 14; label.TextAlignment = TextAlignment.Center; body.Children.Add(label); var marker = Text(events.Length > 0 ? "● " + events.Length : "", 9, true); marker.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; marker.LineHeight = 11; marker.TextAlignment = TextAlignment.Center; body.Children.Add(marker);
            var button = new Button { Content = body, Tag = day, Height = height, Margin = new Thickness(2), Opacity = day.Month == first.Month ? 1 : .3, ToolTip = day.ToString("yyyy-MM-dd") + string.Concat(events.Select(row => "\n" + row.Title)) }; button.SetResourceReference(StyleProperty, "CompletionDateButton"); button.SetResourceReference(BackgroundProperty, events.Length > 0 ? "AccentSoftBrush" : "SubtleBrush");
            button.Click += (_, _) =>
            {
                if (events.Length == 1) { _open(DesktopWidgetKind.Dates, events[0].Item.Id, day); return; }
                if (events.Length == 0) return;
                var menu = new ContextMenu(); menu.SetResourceReference(BackgroundProperty, "CardBrush"); menu.SetResourceReference(ForegroundProperty, "TextBrush");
                foreach (var row in events) { var item = new MenuItem { Header = row.Title }; item.Click += (_, _) => _open(DesktopWidgetKind.Dates, row.Item.Id, day); menu.Items.Add(item); } menu.PlacementTarget = button; menu.IsOpen = true;
            };
            grid.Children.Add(button);
        }
        WidgetBody.Children.Add(grid);
    }
}
