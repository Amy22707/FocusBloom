using System.Windows;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public partial class RenameBackgroundWindow : Window
{
    private readonly FocusMediaViewModel _model;
    private readonly FocusImage _image;
    public RenameBackgroundWindow(FocusMediaViewModel model, FocusImage image)
    {
        InitializeComponent(); WindowAppearance.Attach(this);
        _model = model; _image = image; NameInput.Text = image.Name;
        Loaded += (_, _) => { NameInput.Focus(); NameInput.SelectAll(); };
    }
    private void Save_Click(object sender, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(NameInput.Text)) { ValidationMessage.Text = "请填写背景名称。"; return; }
        if (_model.RenameImage(_image, NameInput.Text)) DialogResult = true;
    }
}
