using System;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class FocusRecoveryWindow : Window
{
    public FocusRecoveryWindow(MainViewModel main)
    {
        Title = "恢复上次专注"; Width = 530; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; SetResourceReference(StyleProperty, typeof(Window)); WindowAppearance.Attach(this);
        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "接着上次的一刻", FontSize = 24, FontWeight = FontWeights.SemiBold });
        var description = new TextBlock { Text = "发现未结束的计时。中断期间不计入专注，确认后再继续。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 20) };
        description.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); root.Children.Add(description);
        var details = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(20), Child = new TextBlock { Text = main.RecoveryDetails, TextWrapping = TextWrapping.Wrap, LineHeight = 28 } };
        details.SetResourceReference(Border.BackgroundProperty, "SubtleBrush"); root.Children.Add(details);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0) };
        error.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush"); root.Children.Add(error);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
        void Add(string name, string text, Action action, bool primary = false)
        {
            var button = new Button { Name = name, Content = text, Margin = new Thickness(6, 6, 0, 0) };
            if (primary) button.SetResourceReference(StyleProperty, "PrimaryButton");
            button.Click += (_, _) => action(); buttons.Children.Add(button);
        }
        void Resolve(bool resume, bool discard = false)
        {
            if (main.ResolveRecovery(resume, discard)) DialogResult = true;
            else error.Text = main.StatusMessage;
        }
        Add("DiscardRecoveryButton", "放弃", () =>
        {
            var confirm = new ConfirmDeletionWindow("放弃这次未完成的计时？已经保存的专注记录会保留。") { Owner = this, Title = "放弃未完成计时" };
            confirm.DeletionHeading.Text = "放弃未完成计时"; confirm.ConfirmDeletionButton.Content = "放弃";
            if (confirm.ShowDialog() == true) Resolve(false, true);
        });
        Add("SaveRecoveryButton", "保存并结束", () => Resolve(false));
        if (main.CanContinueRecovery) Add("ContinueRecoveryButton", main.RecoveryContinueText, () => Resolve(true), true);
        root.Children.Add(buttons); Content = root;
    }
}
