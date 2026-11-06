using System.Diagnostics;
using Avalonia.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Reflection;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private DateTimeOffset _lastReminder;
    private bool _sendingReminders;
    private async Task<int> Notify(string command, params string[] arguments)
    {
        if (!OperatingSystem.IsMacOS()) return 2;
        var result = await MacNotifications.Send(command == "authorize", arguments.ElementAtOrDefault(0) ?? "", arguments.ElementAtOrDefault(1) ?? "");
        if (result == 1) throw new InvalidOperationException(T("无法发送系统通知。", "Could not send the notification."));
        return result;
    }
    private Control NotificationSettings()
    {
        var enabled = new CheckBox { Content = T("开启系统提醒", "Enable system notifications"), IsChecked = _store.GetSetting("mac.reminders.enabled") == "True" };
        var handling = false;
        enabled.IsCheckedChanged += async (_, _) =>
        {
            if (handling) return;
            handling = true;
            try
            {
                if (enabled.IsChecked == true && await Notify("authorize") != 0)
                {
                    enabled.IsChecked = false;
                    Report(T("请在系统设置的通知中允许 FocusBloom 通知。", "Allow FocusBloom notifications in System Settings."));
                }
                _store.SaveSetting("mac.reminders.enabled", (enabled.IsChecked == true).ToString());
            }
            catch (Exception error) { enabled.IsChecked = false; Report(error.Message); }
            finally { handling = false; }
        };
        return Card(Label(T("日程与习惯提醒", "Calendar & habit reminders"), 17, true), enabled,
            Label(T("软件运行期间，会按事项和习惯的提醒设置发送通知。", "Notifications follow your event and habit reminder settings while FocusBloom is running.")),
            AsyncButton(T("发送一条测试通知", "Send a test notification"), async () =>
            {
                if (await Notify("send", "FocusBloom", T("提醒已准备好。", "Notifications are ready.")) != 0)
                    Report(T("请先启用并允许系统提醒。", "Enable and allow notifications first."));
            }));
    }
    private async Task CheckReminders()
    {
        var now = DateTimeOffset.Now;
        if (_sendingReminders || now - _lastReminder < TimeSpan.FromMinutes(1) || _store.GetSetting("mac.reminders.enabled") != "True") return;
        _lastReminder = now; _sendingReminders = true;
        try
        {
            var reminders = ReminderRules.Build(_store.GetTodos(), _store.GetDateEvents(), _store.GetCalendarPlans(), _store.GetSchoolTerm(),
                    _store.GetCourses(), _store.GetCourseChanges(), _store.GetReminderOptions, Today, Today.AddDays(-1))
                .Concat(HabitReminders.Build(_store.GetHabits(), _store.GetHabitCheckIns(), Today));
            foreach (var reminder in reminders.Where(r => r.At <= now && now - r.At <= TimeSpan.FromDays(1) && !_store.IsReminderHandled(r.Key)))
            {
                if (await Notify("send", reminder.Title, reminder.Detail) != 0) break;
                _store.MarkReminderHandled(reminder.Key);
            }
        }
        catch (Exception error) { Report(error.Message); }
        finally { _sendingReminders = false; }
    }
}
