using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

internal sealed class PlannerItemEditorWindow : Window
{
    public string ItemText { get; private set; }

    public PlannerItemEditorWindow(string text)
    {
        ItemText = text;
        Title = UiText.Get("编辑计划事项");
        Width = 450;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(StyleProperty, typeof(Window));
        WindowAppearance.Attach(this);

        var root = new StackPanel { Margin = new Thickness(24) };
        Content = root;
        var input = new TextBox { Text = text, MaxLength = 500, TextWrapping = TextWrapping.Wrap, MinHeight = 72, Name = "PlannerEditInput" };
        System.Windows.Automation.AutomationProperties.SetName(input, Title);
        root.Children.Add(input);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        root.Children.Add(actions);
        actions.Children.Add(new Button { Content = UiText.Get("取消"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var save = new Button { Content = UiText.Get("保存"), IsDefault = true, IsEnabled = !string.IsNullOrWhiteSpace(text), Name = "PlannerSaveEdit" };
        save.SetResourceReference(StyleProperty, "PrimaryButton");
        actions.Children.Add(save);
        input.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return;
            ItemText = input.Text.Trim();
            DialogResult = true;
        };
        Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
