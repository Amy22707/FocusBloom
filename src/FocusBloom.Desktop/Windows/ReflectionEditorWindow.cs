using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FocusBloom.Core.Reflection;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class ReflectionEditorWindow : CalendarFormWindow
{
    private readonly DateOnly _date;
    private int? _mood;
    private readonly Dictionary<int, Button> _choices = new();
    private readonly TextBox _tags, _feeling, _highlights, _next;
    public ReflectionEditorWindow(MainViewModel main, DateOnly date) : base(main, date.ToString("yyyy年M月d日") + " · 心情与复盘", main.Reflection.Entries.Any(item => item.Date == date))
    {
        _date = date; Height = Math.Min(740, SystemParameters.WorkArea.Height - 70); var entry = main.Reflection.Entries.FirstOrDefault(item => item.Date == date); _mood = entry?.Mood;
        Caption("这一刻的心情"); var choices = new UniformGrid { Columns = 5 };
        foreach (var choice in ReflectionRules.Moods)
        {
            var content = new StackPanel(); content.Children.Add(new TextBlock { Text = choice.Emoji, FontFamily = new("Segoe UI Emoji"), FontSize = 25, TextAlignment = TextAlignment.Center }); content.Children.Add(new TextBlock { Text = choice.Name, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            var button = new Button { Content = content, Padding = new Thickness(4, 9, 4, 9), Margin = new Thickness(0, 0, 5, 0), BorderThickness = new Thickness(1) }; RegisterName("MoodChoice" + choice.Value, button); _choices.Add(choice.Value, button);
            button.Click += (_, _) => { _mood = choice.Value; RefreshChoices(); }; choices.Children.Add(button);
        }
        Fields.Children.Add(choices); var clear = new Button { Content = "不选择心情，只写复盘", FontSize = 11, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }; clear.Click += (_, _) => { _mood = null; RefreshChoices(); }; Fields.Children.Add(clear); RefreshChoices();
        _tags = AddTextInput("TagsInput", "标签（用逗号分开，例如：学习，运动）", entry is null ? "" : string.Join("，", entry.Tags));
        _feeling = AddTextInput("FeelingInput", "今天的感受 · 可留空", entry?.Feeling ?? "", true);
        _highlights = AddTextInput("HighlightsInput", "完成了什么，或想记住什么 · 可留空", entry?.Highlights ?? "", true);
        _next = AddTextInput("NextStepInput", "明天的第一步 · 可留空", entry?.NextStep ?? "", true);
        Caption("选一个心情就可以保存，文字可以以后再补。每天保留一条记录。"); Finish();
    }
    private void RefreshChoices()
    {
        foreach (var (value, button) in _choices) { button.SetResourceReference(BackgroundProperty, _mood == value ? "AccentSoftBrush" : "SubtleBrush"); button.SetResourceReference(BorderBrushProperty, _mood == value ? "AccentBrush" : "BorderBrush"); }
    }
    protected override void Save() => Commit(() => Main.Reflection.Save(new(_date, _mood, _tags.Text.Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries), _feeling.Text, _highlights.Text, _next.Text)));
    protected override void Delete()
    {
        if (ConfirmDeletionWindow.Ask(this, $"删除 {_date:yyyy年M月d日} 的心情与复盘？专注、待办和打卡记录仍保留。")) Commit(() => Main.Reflection.Delete(_date));
    }
}
