using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FocusBloom.Desktop.Presentation;

public static class DatePickerNavigation
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(DatePickerNavigation), new PropertyMetadata(false, Changed));
    public static bool GetEnable(DependencyObject target) => (bool)target.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject target, bool value) => target.SetValue(EnableProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not DatePicker picker) return;
        if ((bool)args.NewValue) { picker.CalendarOpened += Opened; picker.Loaded += CenterText; }
        else { picker.CalendarOpened -= Opened; picker.Loaded -= CenterText; }
    }
    private static void CenterText(object sender, RoutedEventArgs args)
    {
        var picker = (DatePicker)sender;
        if (picker.Template.FindName("PART_TextBox", picker) is not DatePickerTextBox field) return;
        field.SetCurrentValue(TextBox.TextAlignmentProperty, TextAlignment.Center);
        field.SetCurrentValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
    }
    private static void Opened(object? sender, RoutedEventArgs args)
    {
        var picker = (DatePicker)sender!;
        if (picker.Template.FindName("PART_Popup", picker) is not Popup popup || popup.Child is not Calendar calendar) return;
        var years = new ComboBox { Width = 88, ItemsSource = Enumerable.Range(DateTime.Today.Year - 100, 201).ToArray(), ToolTip = "年份", Margin = new Thickness(0, 0, 8, 0) };
        var months = new ComboBox { Width = 80, ItemsSource = Enumerable.Range(1, 12).ToArray(), ToolTip = "月份" };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 8, 10, 4) }; toolbar.Children.Add(years); toolbar.Children.Add(months);
        var content = new DockPanel(); content.SetResourceReference(Panel.BackgroundProperty, "CardBrush"); DockPanel.SetDock(toolbar, Dock.Top); content.Children.Add(toolbar);
        popup.Child = null; content.Children.Add(calendar); popup.Child = content;
        var updating = false;
        void Refresh() { updating = true; years.SelectedItem = calendar.DisplayDate.Year; months.SelectedItem = calendar.DisplayDate.Month; updating = false; }
        void Navigate()
        {
            if (updating || years.SelectedItem is not int year || months.SelectedItem is not int month) return;
            calendar.DisplayMode = CalendarMode.Month;
            calendar.DisplayDate = new DateTime(year, month, Math.Min(calendar.DisplayDate.Day, DateTime.DaysInMonth(year, month)));
        }
        years.SelectionChanged += (_, _) => Navigate(); months.SelectionChanged += (_, _) => Navigate(); calendar.DisplayDateChanged += (_, _) => Refresh();
        Refresh();
    }
}
