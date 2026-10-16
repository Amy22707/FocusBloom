using System;
using System.Globalization;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class ReflectionReminderWindow : CalendarFormWindow
{
    private readonly CheckBox _enabled;
    private readonly TextBox _time;
    public ReflectionReminderWindow(MainViewModel main) : base(main, "心情与复盘 · 每日提醒")
    {
        Height = 410; var options = main.Reflection.Reminder;
        _enabled = Check("ReminderEnabledInput", "每天提醒我留下一点记录", options.Enabled);
        _time = AddTextInput("ReminderTimeInput", "提醒时间（24小时制）", (options.Time ?? new TimeOnly(21, 0)).ToString("HH:mm"));
        Caption("当天已经记录心情或复盘后，不再提醒。默认关闭，可随时调整。");
        Caption(main.SoftwareRemindersEnabled ? "提醒由 Windows 通知发送，受偏好设置中的软件提醒总开关控制。" : "软件提醒总开关已关闭；这里的选择会保存，开启总开关后才会推送。"); Finish();
    }
    protected override void Save()
    {
        if (!TimeOnly.TryParseExact(_time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) { Message.Text = "请输入有效的提醒时间，例如 21:00。"; return; }
        Commit(() => Main.Reflection.SaveReminder(new(_enabled.IsChecked == true, time)));
    }
}
