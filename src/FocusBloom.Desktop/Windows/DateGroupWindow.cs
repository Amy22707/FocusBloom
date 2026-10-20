using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class DateGroupWindow : Window
{
    public DateGroupWindow(MainViewModel main, string? name = null)
    {
        Title = name is null ? "新建日期分组" : "编辑日期分组"; Width = 440; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false;
        SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this); NameScope.SetNameScope(this, new NameScope());
        var body = new StackPanel { Margin = new Thickness(26) }; Content = body; body.Children.Add(new TextBlock { Text = Title, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        var input = new TextBox { Text = name ?? "" }; RegisterName("NameInput", input); body.Children.Add(input);
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) }; message.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); body.Children.Add(message);
        var actions = new DockPanel { Margin = new Thickness(0, 18, 0, 0) }; body.Children.Add(actions);
        if (name is not null && name != "未分组")
        {
            var delete = new Button { Content = "删除分组" }; RegisterName("DeleteButton", delete); DockPanel.SetDock(delete, Dock.Left); actions.Children.Add(delete);
            delete.Click += (_, _) => { var count = main.DateEvents.Items.Count(row => row.Item.Group == name); if (!ConfirmDeletionWindow.Ask(this, $"删除分组‘{name}’？组内 {count} 个事项将移至‘未分组’。")) return; var error = main.DateEvents.DeleteGroup(name); if (error is null) DialogResult = true; else message.Text = error; };
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; actions.Children.Add(buttons); buttons.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 10, 0) });
        var save = new Button { Content = "保存", IsDefault = true }; save.SetResourceReference(StyleProperty, "PrimaryButton"); RegisterName("SaveButton", save); buttons.Children.Add(save);
        save.Click += (_, _) => { var error = main.DateEvents.SaveGroup(name, input.Text); if (error is null) DialogResult = true; else message.Text = error; };
    }
}
