using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Desktop.Views;

public sealed class ReminderSettingsControl : StackPanel
{
    private readonly CheckBox _enabled = new() { Content = "提醒我", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBox _lead = new();
    private readonly TextBox _dayTime = new();
    private readonly StackPanel _leadArea = new();
    private readonly StackPanel _dayArea = new();
    private readonly TextBlock _preview = new() { FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };
    private DateOnly? _date; private TimeOnly? _time;
    public ReminderSettingsControl(ReminderOptions options)
    {
        _enabled.IsChecked = options.Enabled; _lead.Text = options.MinutesBefore.ToString(); _dayTime.Text = (options.DayTime ?? new TimeOnly(8, 0)).ToString("HH:mm");
        Children.Add(_enabled);
        foreach (var (area, caption, input) in new[] { (_leadArea, "提前 / 分钟", _lead), (_dayArea, "当天提醒时间 / HH:mm", _dayTime) })
        {
            var label = new TextBlock { Text = caption, FontSize = 12, Margin = new Thickness(0, 0, 0, 6) }; label.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            area.Children.Add(label); area.Children.Add(input); Children.Add(area);
        }
        _preview.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); Children.Add(_preview);
        _enabled.Checked += (_, _) => UpdatePreview(); _enabled.Unchecked += (_, _) => UpdatePreview();
        _lead.TextChanged += (_, _) => UpdatePreview(); _dayTime.TextChanged += (_, _) => UpdatePreview(); UpdatePreview();
    }
    public void SetOccurrence(DateOnly? date, TimeOnly? time) { _date = date; _time = time; UpdatePreview(); }
    public bool TryGetOptions(out ReminderOptions options)
    {
        options = new(_enabled.IsChecked == true);
        if (!options.Enabled) return true;
        if (!int.TryParse(_lead.Text, out var lead) || lead is < 0 or > 10080
            || !TimeOnly.TryParseExact(_dayTime.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)) return false;
        options = new(true, lead, day); return true;
    }
    private void UpdatePreview()
    {
        _leadArea.Visibility = _time is not null ? Visibility.Visible : Visibility.Collapsed;
        _dayArea.Visibility = _time is null ? Visibility.Visible : Visibility.Collapsed;
        _leadArea.IsEnabled = _dayArea.IsEnabled = _enabled.IsChecked == true;
        _preview.Text = _enabled.IsChecked != true ? "提醒已关闭" : _date is null ? "没有可提醒的日期" : TryGetOptions(out var options)
            ? $"提醒时间：{CalendarRules.ReminderAt(_date.Value, _time, options).LocalDateTime:yyyy-MM-dd HH:mm}" : "请填写有效提前分钟数及HH:mm时间";
    }
}
