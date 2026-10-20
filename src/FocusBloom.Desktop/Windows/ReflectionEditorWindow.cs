using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Data;
using FocusBloom.Core.Reflection;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Infrastructure.Storage;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Windows;

public sealed class ReflectionEditorWindow : CalendarFormWindow
{
    private readonly DateOnly _date;
    private int? _mood;
    private readonly Dictionary<int, Button> _choices = new();
    private readonly TextBox _tags, _feeling, _highlights, _next;
    private readonly ReflectionImages _library;
    private readonly List<ReflectionPhoto> _photos = new();
    private readonly HashSet<string> _added = new();
    private readonly WrapPanel _photoPanel = new();
    private bool _saved;
    public ReflectionEditorWindow(MainViewModel main, DateOnly date) : base(main, date.ToString("yyyy年M月d日") + " · 心情与复盘", main.Reflection.Entries.Any(item => item.Date == date))
    {
        _date = date; Height = Math.Min(740, SystemParameters.WorkArea.Height - 70); var entry = main.Reflection.Entries.FirstOrDefault(item => item.Date == date); _mood = entry?.Mood;
        _library = new(Path.GetDirectoryName(((SqliteFocusStore)main.Calendar.Store).DatabasePath)!);
        foreach (var path in entry?.Photos ?? Array.Empty<string>()) _photos.Add(new(path));
        Caption("这一刻的心情"); var choices = new UniformGrid { Columns = 3 }; RegisterName("MoodChoices", choices);
        foreach (var choice in ReflectionRules.Moods.OrderByDescending(choice => choice.Level).ThenBy(choice => choice.Value))
        {
            var content = new StackPanel(); content.Children.Add(new MoodIcon { Value = choice.Value, Width = 42, Height = 42, HorizontalAlignment = HorizontalAlignment.Center }); content.Children.Add(new TextBlock { Text = choice.Name, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 5, 0, 0) });
            var button = new Button { Content = content, Padding = new Thickness(4, 7, 4, 7), Margin = new Thickness(0, 0, 6, 6), Height = 82, BorderThickness = new Thickness(1), ToolTip = choice.Name }; RegisterName("MoodChoice" + choice.Value, button); _choices.Add(choice.Value, button);
            button.Click += (_, _) => { _mood = choice.Value; RefreshChoices(); }; choices.Children.Add(button);
        }
        Fields.Children.Add(choices); var clear = new Button { Content = "不选择心情，只写复盘", FontSize = 11, Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }; clear.Click += (_, _) => { _mood = null; RefreshChoices(); }; Fields.Children.Add(clear); RefreshChoices();
        _tags = AddTextInput("TagsInput", "标签（用逗号分开，例如：学习，运动）", entry is null ? "" : string.Join("，", entry.Tags));
        _feeling = AddTextInput("FeelingInput", "今天的感受 · 可留空", entry?.Feeling ?? "", true);
        Caption("图片 · 自动保存到本地"); var imageActions = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var upload = new Button { Content = "添加图片", FontSize = 12, Padding = new Thickness(10, 5, 10, 5) }; RegisterName("AddPhotosButton", upload);
        upload.Click += (_, _) => { var dialog = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp", Multiselect = true }; if (dialog.ShowDialog(this) == true) foreach (var path in dialog.FileNames) AddFile(path); }; imageActions.Children.Add(upload);
        var paste = new Button { Content = "粘贴图片 · Ctrl+V", FontSize = 12, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0) }; RegisterName("PastePhotosButton", paste); paste.Click += (_, _) => PasteClipboardImages(); imageActions.Children.Add(paste);
        var folder = new Button { Content = "本地图片", FontSize = 12, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(8, 0, 0, 0), ToolTip = _library.DirectoryPath }; folder.Click += (_, _) => { Directory.CreateDirectory(_library.DirectoryPath); Process.Start(new ProcessStartInfo(_library.DirectoryPath) { UseShellExecute = true }); }; imageActions.Children.Add(folder);
        Fields.Children.Add(imageActions); RegisterName("PhotoThumbnails", _photoPanel); Fields.Children.Add(_photoPanel); RenderPhotos();
        _highlights = AddTextInput("HighlightsInput", "完成了什么，或想记住什么 · 可留空", entry?.Highlights ?? "", true);
        _next = AddTextInput("NextStepInput", "明天的第一步 · 可留空", entry?.NextStep ?? "", true);
        Caption("选一个心情就可以保存，文字可以以后再补。每天保留一条记录。"); Finish();
        PreviewKeyDown += (_, args) => { if (args.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && PasteClipboardImages()) args.Handled = true; };
        Closed += (_, _) => { foreach (var path in _added.Where(path => !_saved || !_photos.Any(photo => photo.Path == path))) _library.DeleteCopy(path); };
    }
    public void AddFile(string path)
    { try { AddCopy(_library.ImportFile(path)); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Message.Text = error.Message; } }
    public void AddPastedImage(BitmapSource image) => AddCopy(_library.SavePastedImage(image));
    private void AddCopy(string path) { _added.Add(path); _photos.Add(new(path)); RenderPhotos(); }
    public bool PasteClipboardImages()
    {
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image) { AddPastedImage(image); return true; }
            if (Clipboard.ContainsFileDropList()) { foreach (var path in Clipboard.GetFileDropList().Cast<string>()) AddFile(path); return true; }
            return false;
        }
        catch (Exception error) when (error is ExternalException or IOException or UnauthorizedAccessException) { Message.Text = "图片粘贴失败：" + error.Message; return true; }
    }
    private void RenderPhotos()
    {
        _photoPanel.Children.Clear(); var index = 0;
        foreach (var photo in _photos)
        {
            var body = new StackPanel { Width = 128, Margin = new Thickness(0, 0, 8, 10) };
            var brush = new ImageBrush { Stretch = Stretch.UniformToFill }; BindingOperations.SetBinding(brush, ImageBrush.ImageSourceProperty, new Binding(nameof(ReflectionPhoto.Thumbnail)) { Source = photo });
            var preview = new Button { Content = new Border { Height = 90, CornerRadius = new CornerRadius(8), Background = brush }, Padding = new Thickness(0), ToolTip = "查看大图" }; preview.SetResourceReference(StyleProperty, "CompletionDateButton"); preview.Click += (_, _) => new PhotoViewerWindow(photo.Path) { Owner = this }.ShowDialog(); body.Children.Add(preview);
            var footer = new DockPanel { Margin = new Thickness(0, 4, 0, 0) }; var remove = new Button { Content = "移除", FontSize = 11, Padding = new Thickness(4, 1, 4, 1), Tag = photo.Path }; DockPanel.SetDock(remove, Dock.Right); footer.Children.Add(remove);
            remove.Click += (_, _) => { if (ConfirmDeletionWindow.Ask(this, "从这条记录中移除这张图片？")) { _photos.Remove(photo); RenderPhotos(); } }; footer.Children.Add(new TextBlock { Text = "图片 " + ++index, FontSize = 11, VerticalAlignment = VerticalAlignment.Center }); body.Children.Add(footer); _photoPanel.Children.Add(body);
        }
    }
    private void RefreshChoices()
    {
        foreach (var (value, button) in _choices)
        {
            if (_mood == value) { button.Background = Views.ReflectionView.MoodFill(value); button.BorderBrush = MoodIcon.BrushFor(value); button.BorderThickness = new Thickness(2); }
            else { button.SetResourceReference(BackgroundProperty, "SubtleBrush"); button.SetResourceReference(BorderBrushProperty, "BorderBrush"); button.BorderThickness = new Thickness(1); }
        }
    }
    protected override void Save() => Commit(() => { Main.Reflection.Save(new(_date, _mood, _tags.Text.Split(new[] { ',', '，', ';', '；', '\n' }, StringSplitOptions.RemoveEmptyEntries), _feeling.Text, _highlights.Text, _next.Text, _photos.Select(photo => photo.Path).ToArray())); _saved = true; });
    protected override void Delete()
    {
        if (ConfirmDeletionWindow.Ask(this, $"删除 {_date:yyyy年M月d日} 的心情与复盘？专注、待办和打卡记录仍保留。")) Commit(() => Main.Reflection.Delete(_date));
    }
}
