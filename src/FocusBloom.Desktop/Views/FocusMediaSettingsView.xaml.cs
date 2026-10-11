using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Windows;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Views;

public partial class FocusMediaSettingsView : UserControl
{
    private FocusMediaViewModel Model => (FocusMediaViewModel)DataContext;
    private ScrollViewer? _returnScroll;
    private string? _returnQuoteId;
    private double _returnTop;
    private double _returnOffset;
    private bool _editorWasExpanded;
    public FocusMediaSettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is FocusMediaViewModel old) old.QuoteEditorCompleted -= ReturnToQuote;
            if (args.NewValue is FocusMediaViewModel current) current.QuoteEditorCompleted += ReturnToQuote;
        };
    }
    private void ImportImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "添加专注背景", Multiselect = true, Filter = "背景（图片、GIF、视频）|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.mp4;*.wmv|图片与 GIF|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff|视频（MP4、WMV）|*.mp4;*.wmv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Model.ImportImages(dialog.FileNames);
    }
    private void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择专注背景文件夹" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Model.ImportFolder(dialog.FolderName);
    }
    private void RandomBackground_Click(object sender, RoutedEventArgs e) => Model.SelectedBackground = Model.BackgroundChoices[0];
    private void PlainBackground_Click(object sender, RoutedEventArgs e) => Model.SelectedBackground = Model.BackgroundChoices[1];
    private void RandomQuote_Click(object sender, RoutedEventArgs e) => Model.SelectedQuote = Model.QuoteChoices[0];
    private void NoSound_Click(object sender, RoutedEventArgs e) => Model.SelectedSound = Model.SoundChoices[0];
    private void UseImage_Click(object sender, RoutedEventArgs e) => Model.SelectedBackground = ((FocusImage)((Button)sender).DataContext).Asset;
    private void RemoveImage_Click(object sender, RoutedEventArgs e)
    {
        var image = (FocusImage)((Button)sender).DataContext;
        if (image.Custom && ConfirmDeletionWindow.Ask(Window.GetWindow(this), $"删除自定义背景‘{image.Name}’？原文件保留。")) Model.RemoveImage(image);
    }
    private void UseQuote_Click(object sender, RoutedEventArgs e) => Model.SelectedQuote = ((FocusQuoteCard)((Button)sender).DataContext).Quote;
    private void EditQuote_Click(object sender, RoutedEventArgs e)
    {
        var card = (FocusQuoteCard)((Button)sender).DataContext;
        if (!card.Custom) return;
        _returnScroll = ParentScroll(); _returnQuoteId = card.Quote.Id;
        _returnOffset = _returnScroll?.VerticalOffset ?? 0;
        _editorWasExpanded = QuoteEditor.IsExpanded;
        if (_returnScroll is not null && QuoteCards.ItemContainerGenerator.ContainerFromItem(card) is FrameworkElement item)
            _returnTop = item.TransformToAncestor(_returnScroll).Transform(new Point()).Y;
        Model.EditQuote(card.Quote); QuoteEditor.IsExpanded = true; QuoteEditor.BringIntoView();
    }
    private ScrollViewer? ParentScroll()
    {
        for (DependencyObject? parent = this; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll) return scroll;
        return null;
    }
    private void ReturnToQuote(string? id)
    {
        if (id is null || id != _returnQuoteId || _returnScroll is null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new System.Action(() =>
        {
            QuoteEditor.IsExpanded = _editorWasExpanded;
            UpdateLayout();
            var card = System.Linq.Enumerable.FirstOrDefault(Model.Quotes, card => card.Quote.Id == id);
            if (card is not null && QuoteCards.ItemContainerGenerator.ContainerFromItem(card) is FrameworkElement item)
                _returnScroll.ScrollToVerticalOffset(_returnScroll.VerticalOffset + item.TransformToAncestor(_returnScroll).Transform(new Point()).Y - _returnTop);
            else _returnScroll.ScrollToVerticalOffset(_returnOffset);
            _returnQuoteId = null;
        }));
    }
    private void RemoveQuote_Click(object sender, RoutedEventArgs e)
    {
        var quote = ((FocusQuoteCard)((Button)sender).DataContext).Quote;
        if (quote.Custom && ConfirmDeletionWindow.Ask(Window.GetWindow(this), $"删除自定义句子‘{quote.Name}’？")) Model.RemoveQuote(quote);
    }
    private void QuoteSource_Click(object sender, RoutedEventArgs e) => Model.OpenQuoteSource(((FocusQuoteCard)((Button)sender).DataContext).Quote);
    private void UseSound_Click(object sender, RoutedEventArgs e) => Model.SelectedSound = ((FocusSoundCard)((Button)sender).DataContext).Asset;
    private void RemoveSound_Click(object sender, RoutedEventArgs e)
    {
        var sound = (FocusSoundCard)((Button)sender).DataContext;
        if (sound.Custom && ConfirmDeletionWindow.Ask(Window.GetWindow(this), $"删除自定义声音‘{sound.Name}’？原文件保留。")) Model.RemoveSound(sound);
    }
    private void SoundSource_Click(object sender, RoutedEventArgs e) => Model.OpenSoundSource(((FocusSoundCard)((Button)sender).DataContext).Asset);
    private void RestoreImages_Click(object sender, RoutedEventArgs e) => Model.RestoreImages();
    private void RestoreQuotes_Click(object sender, RoutedEventArgs e) => Model.RestoreQuotes();
    private void RestoreSounds_Click(object sender, RoutedEventArgs e) => Model.RestoreSounds();
    private void ImportSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "添加本地音频", Filter = "音频|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.aif;*.aiff" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Model.ImportSound(dialog.FileName);
    }
    private void ImportQuotes_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "导入文字列表（每行一句）", Filter = "文字列表|*.txt" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) Model.ImportQuotes(dialog.FileName);
    }
}
