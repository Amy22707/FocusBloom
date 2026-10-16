using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Windows;

public sealed class ReminderInboxWindow : CalendarFormWindow
{
    public ReminderInboxWindow(MainViewModel main, Action<CalendarEntry> open) : base(main, "事项提醒")
    {
        SaveButton.Content = "完成"; Caption(main.SoftwareRemindersEnabled ? main.NotificationStatus : "软件提醒已关闭；下列时间仅展示事项设置，不会推送。");
        var store = main.Calendar.Store;
        var items = store.GetSetting("notifications.missed") is { } json ? JsonSerializer.Deserialize<List<ReminderOccurrence>>(json)! : new();
        if (items.Count > 0) { Caption("最近错过的提醒"); foreach (var item in items) Add(item); }
        var future = ReminderRules.Build(store.GetTodos(), store.GetDateEvents(), store.GetCalendarPlans(), store.GetSchoolTerm(), store.GetCourses(), store.GetCourseChanges(), store.GetReminderOptions, DateOnly.FromDateTime(DateTime.Today))
            .Concat(FocusBloom.Core.Goals.HabitReminders.Build(store.GetHabits(), store.GetHabitCheckIns(), DateOnly.FromDateTime(DateTime.Today)))
            .Concat(FocusBloom.Core.Reflection.ReflectionReminders.Build(main.Reflection.Reminder, store.GetDailyReflections(), DateOnly.FromDateTime(DateTime.Today))).OrderBy(item => item.At).Where(item => item.At > DateTimeOffset.Now).Take(60).ToArray();
        Caption("接下来的提醒"); foreach (var item in future) Add(item);
        if (future.Length == 0) Caption("没有已开启的未来提醒。");
        void Add(ReminderOccurrence item)
        {
            var button = new Button { Content = $"{item.Title}\n{item.At.LocalDateTime:yyyy-MM-dd HH:mm} · {item.Kind}", HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            button.Click += (_, _) => open(new(item.Kind, item.Id, item.Title, DateOnly.FromDateTime(item.HappensAt.LocalDateTime), OriginalDate: item.OriginalDate, MeetingId: item.MeetingId)); Fields.Children.Add(button);
        }
    }
    protected override void Save() => Close();
}
