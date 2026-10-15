using System;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public CalendarViewModel Calendar { get; }
    public CalendarViewModel FocusCalendar { get; }
    public DateEventsViewModel DateEvents { get; }
    private bool _softwareRemindersEnabled = true;
    private bool _outlookSyncRequested;
    public bool SoftwareRemindersEnabled
    {
        get => _softwareRemindersEnabled;
        set { if (_softwareRemindersEnabled == value) return; _softwareRemindersEnabled = value; _store.SaveSetting("reminders.enabled", value.ToString()); Notify(); CalendarDataChanged?.Invoke(this, EventArgs.Empty); }
    }
    public bool OutlookSyncRequested
    {
        get => _outlookSyncRequested;
        set { if (_outlookSyncRequested == value) return; _outlookSyncRequested = value; _store.SaveSetting("outlook.sync.requested", value.ToString()); Notify(); Notify(nameof(OutlookSyncStatus)); }
    }
    public string OutlookSyncStatus => OutlookSyncRequested ? "已保存同步偏好，账号连接尚未接入；当前仍使用本地日历。" : "不同步到 Windows／Outlook，保留本地日历。";
    private DateOnly _calendarDay = DateOnly.FromDateTime(DateTime.Today);
    private void RefreshCalendarDay()
    { var day = DateOnly.FromDateTime(DateTime.Today); if (day != _calendarDay) { _calendarDay = day; ReloadCalendarData(); Goals.Reload(); Statistics.Reload(); } }
    private string _notificationStatus = "系统提醒正在初始化";
    public string NotificationStatus { get => _notificationStatus; set { _notificationStatus = value; Notify(); } }
    public event EventHandler? CalendarDataChanged;
    public void ReloadCalendarData()
    { Calendar.Reload(); FocusCalendar.Reload(); DateEvents.Reload(); CalendarDataChanged?.Invoke(this, EventArgs.Empty); }
}
