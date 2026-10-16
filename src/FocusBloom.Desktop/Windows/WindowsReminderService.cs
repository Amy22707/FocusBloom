using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.ViewModels;
using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace FocusBloom.Desktop.Windows;

public sealed class WindowsReminderService : IDisposable
{
    private sealed record ScheduledItem(string Key, string Tag, DateTimeOffset At, string Title, string Detail, ReminderOccurrence Occurrence);
    private readonly IFocusStore _store;
    private readonly MainViewModel _main;
    private readonly Action<CalendarEntry> _open;
    private readonly ToastNotifierCompat _notifier;
    private readonly DispatcherTimer _rebuild;
    private IReadOnlyList<ReminderOccurrence> _occurrences = Array.Empty<ReminderOccurrence>();
    private List<ScheduledItem> _scheduled = new();
    private DateOnly _day = DateOnly.FromDateTime(DateTime.Today);
    private DateTimeOffset _lastTick;
    public WindowsReminderService(IFocusStore store, MainViewModel main, Action<CalendarEntry> open)
    {
        _store = store; _main = main; _open = open;
        ToastNotificationManagerCompat.OnActivated += Activated;
        _notifier = ToastNotificationManagerCompat.CreateToastNotifier();
        _rebuild = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _rebuild.Tick += (_, _) => { _rebuild.Stop(); Reconcile(); };
        main.CalendarDataChanged += DataChanged; Reconcile();
    }
    private void DataChanged(object? sender, EventArgs e) { _rebuild.Stop(); _rebuild.Start(); }
    public void Reconcile()
    {
        _rebuild.Stop();
        try
        {
            if (!_main.SoftwareRemindersEnabled)
            {
                foreach (var item in _notifier.GetScheduledToastNotifications().Where(item => item.Group == "FocusBloom")) _notifier.RemoveFromSchedule(item);
                ToastNotificationManagerCompat.History.RemoveGroup("FocusBloom");
                _occurrences = Array.Empty<ReminderOccurrence>(); _scheduled.Clear(); _store.SaveSetting("notifications.pending", "[]");
                _main.NotificationStatus = "软件提醒已关闭，事项提醒设置保留。"; return;
            }
            _scheduled = _store.GetSetting("notifications.pending") is { } json ? JsonSerializer.Deserialize<List<ScheduledItem>>(json)! : new();
            var today = DateOnly.FromDateTime(DateTime.Today);
            var lower = _scheduled.Where(item => item.At <= DateTimeOffset.Now && !_store.IsReminderHandled(item.Key)).Select(item => DateOnly.FromDateTime(item.Occurrence.HappensAt.LocalDateTime)).Append(today.AddDays(-7)).Min();
            _occurrences = ReminderRules.Build(_store.GetTodos(), _store.GetDateEvents(), _store.GetCalendarPlans(), _store.GetSchoolTerm(), _store.GetCourses(), _store.GetCourseChanges(), _store.GetReminderOptions, today, lower)
                .Concat(FocusBloom.Core.Goals.HabitReminders.Build(_store.GetHabits(), _store.GetHabitCheckIns(), today))
                .Concat(FocusBloom.Core.Reflection.ReflectionReminders.Build(_main.Reflection.Reminder, _store.GetDailyReflections(), today)).OrderBy(item => item.At).ToArray();
            var native = _notifier.GetScheduledToastNotifications().Where(item => item.Group == "FocusBloom").ToDictionary(item => item.Tag);
            var now = DateTimeOffset.Now;
            var desired = _occurrences.Where(item => item.At > now && !_store.IsReminderHandled(item.Key)).ToDictionary(item => item.Key);
            var active = _occurrences.ToDictionary(item => item.Key);
            foreach (var orphan in native.Values.Where(item => _scheduled.All(schedule => schedule.Tag != item.Tag))) _notifier.RemoveFromSchedule(orphan);
            foreach (var item in _scheduled.ToArray())
            {
                if (!active.TryGetValue(item.Key, out var occurrence) || item.Title != occurrence.Title || item.Detail != occurrence.Detail)
                { if (native.TryGetValue(item.Tag, out var notification)) _notifier.RemoveFromSchedule(notification); ToastNotificationManagerCompat.History.Remove(item.Tag, "FocusBloom"); _scheduled.Remove(item); }
            }
            foreach (var occurrence in desired.Values)
            {
                var existing = _scheduled.FirstOrDefault(item => item.Key == occurrence.Key);
                if (existing is not null && native.ContainsKey(existing.Tag)) continue;
                var tag = existing?.Tag ?? Guid.NewGuid().ToString("N")[..16];
                var scheduled = new ScheduledToastNotification(Content(occurrence).GetToastContent().GetXml(), occurrence.At) { Tag = tag, Group = "FocusBloom" };
                _notifier.AddToSchedule(scheduled);
                if (existing is not null) _scheduled.Remove(existing);
                _scheduled.Add(new(occurrence.Key, tag, occurrence.At, occurrence.Title, occurrence.Detail, occurrence));
            }
            _store.SaveSetting("notifications.pending", JsonSerializer.Serialize(_scheduled));
            _main.NotificationStatus = _notifier.Setting != NotificationSetting.Enabled ? "Windows 已关闭系统通知，请在系统通知设置中开启。" : $"已安排 {_scheduled.Count(item => item.At > now)} 条系统提醒";
            ProcessDue();
        }
        catch (Exception ex) { _main.NotificationStatus = "系统提醒安排失败：" + ex.Message; }
    }
    public void Tick()
    {
        if (DateTimeOffset.Now - _lastTick < TimeSpan.FromMinutes(1)) return;
        _lastTick = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (today != _day) { _day = today; Reconcile(); }
        else ProcessDue();
    }
    private void ProcessDue()
    {
        if (!_main.SoftwareRemindersEnabled) return;
        try
        {
            var now = DateTimeOffset.Now;
            if (_notifier.Setting != NotificationSetting.Enabled)
            { var pending = _occurrences.Where(item => item.At <= now && !_store.IsReminderHandled(item.Key)).ToArray(); if (pending.Length > 0) _store.SaveSetting("notifications.missed", JsonSerializer.Serialize(pending)); return; }
            var history = ToastNotificationManagerCompat.History.GetHistory().Select(item => item.Tag).ToHashSet();
            var missed = new List<ReminderOccurrence>();
            foreach (var occurrence in _occurrences.Where(item => item.At <= now && !_store.IsReminderHandled(item.Key)))
            {
                var schedule = _scheduled.FirstOrDefault(item => item.Key == occurrence.Key);
                if (schedule is null && occurrence.HappensAt < now) { _store.MarkReminderHandled(occurrence.Key); continue; }
                if (schedule is not null && (history.Contains(schedule.Tag) || now - occurrence.At < TimeSpan.FromMinutes(5))) { _store.MarkReminderHandled(occurrence.Key); continue; }
                missed.Add(occurrence);
            }
            if (missed.Count == 1)
            { var item = missed[0]; var tag = _scheduled.FirstOrDefault(schedule => schedule.Key == item.Key)?.Tag ?? Guid.NewGuid().ToString("N")[..16]; _notifier.Show(new ToastNotification(Content(item).GetToastContent().GetXml()) { Tag = tag, Group = "FocusBloom" }); }
            else if (missed.Count > 1)
            {
                var content = new ToastContentBuilder().AddArgument("kind", "汇总").AddArgument("id", Guid.Empty.ToString()).AddText($"{missed.Count} 项待处理提醒").AddText(string.Join("；", missed.Take(3).Select(item => item.Title)));
                _notifier.Show(new ToastNotification(content.GetToastContent().GetXml()) { Tag = "missed", Group = "FocusBloom" });
            }
            foreach (var item in missed) _store.MarkReminderHandled(item.Key);
            if (missed.Count > 0) _store.SaveSetting("notifications.missed", JsonSerializer.Serialize(missed));
        }
        catch (Exception ex) { _main.ReportStatus("系统提醒检查失败：" + ex.Message); }
    }
    private static ToastContentBuilder Content(ReminderOccurrence item) => new ToastContentBuilder().AddArgument("kind", item.Kind).AddArgument("id", item.Id.ToString()).AddArgument("key", item.Key)
        .AddArgument("date", item.HappensAt.LocalDateTime.ToString("yyyy-MM-dd")).AddArgument("original", item.OriginalDate?.ToString("yyyy-MM-dd") ?? "")
        .AddArgument("meeting", item.MeetingId?.ToString() ?? "")
        .AddText(item.Title).AddText(item.HappensAt.LocalDateTime.ToString(item.AllDay ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm") + " · " + item.Detail);
    private void Activated(ToastNotificationActivatedEventArgsCompat args)
    {
        var arguments = ToastArguments.Parse(args.Argument);
        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (arguments.TryGetValue("key", out var key)) _store.MarkReminderHandled(key);
            if (arguments.TryGetValue("kind", out var kind) && arguments.TryGetValue("id", out var text) && Guid.TryParse(text, out var id))
            {
                var day = arguments.TryGetValue("date", out var dateText) && DateOnly.TryParse(dateText, out var date) ? date : DateOnly.FromDateTime(DateTime.Today);
                DateOnly? original = arguments.TryGetValue("original", out var originalText) && DateOnly.TryParse(originalText, out var parsed) ? parsed : null;
                Guid? meeting = arguments.TryGetValue("meeting", out var meetingText) && Guid.TryParse(meetingText, out var meetingId) ? meetingId : null;
                _open(new(kind, id, "", day, OriginalDate: original, MeetingId: meeting));
            }
        }));
    }
    public void Dispose() { _rebuild.Stop(); _main.CalendarDataChanged -= DataChanged; ToastNotificationManagerCompat.OnActivated -= Activated; }
}
