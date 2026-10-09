using System;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Views;

public partial class FocusMediaSettingsView : UserControl
{
    private FocusMediaViewModel Model => (FocusMediaViewModel)DataContext;
    public FocusMediaSettingsView() => InitializeComponent();
    private void ImportImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "添加专注背景", Multiselect = true, Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff" };
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
    private void RemoveImage_Click(object sender, RoutedEventArgs e) => Model.RemoveImage((FocusImage)((Button)sender).DataContext);
    private void UseQuote_Click(object sender, RoutedEventArgs e) => Model.SelectedQuote = ((FocusQuoteCard)((Button)sender).DataContext).Quote;
    private void EditQuote_Click(object sender, RoutedEventArgs e) { Model.EditQuote(((FocusQuoteCard)((Button)sender).DataContext).Quote); QuoteEditor.IsExpanded = true; QuoteEditor.BringIntoView(); }
    private void RemoveQuote_Click(object sender, RoutedEventArgs e) => Model.RemoveQuote(((FocusQuoteCard)((Button)sender).DataContext).Quote);
    private void UseSound_Click(object sender, RoutedEventArgs e) => Model.SelectedSound = ((FocusSoundCard)((Button)sender).DataContext).Asset;
    private void RemoveSound_Click(object sender, RoutedEventArgs e) => Model.RemoveSound((FocusSoundCard)((Button)sender).DataContext);
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
