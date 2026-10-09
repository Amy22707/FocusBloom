using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using Microsoft.Win32;

namespace FocusBloom.Desktop.Views;

public partial class FocusAudioControls : UserControl
{
    public FocusAudioControls() => InitializeComponent();
    public static readonly DependencyProperty ShowLibraryProperty = DependencyProperty.Register(nameof(ShowLibrary), typeof(bool), typeof(FocusAudioControls), new PropertyMetadata(true));
    public bool ShowLibrary { get => (bool)GetValue(ShowLibraryProperty); set => SetValue(ShowLibraryProperty, value); }
    private void ImportSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "选择本地音乐或环境声", Filter = "音频文件|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.aif;*.aiff|所有文件|*.*" };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) ((FocusMediaViewModel)DataContext).ImportSound(dialog.FileName);
    }
}
