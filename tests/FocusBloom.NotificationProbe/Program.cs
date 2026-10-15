using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

ToastNotificationManagerCompat.OnActivated += args => Console.WriteLine("Activated: " + args.Argument);
var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
const string tag = "scheduled-test";
const string group = "FocusBloomQA";
if (args.FirstOrDefault() == "schedule")
{
    foreach (var item in notifier.GetScheduledToastNotifications().Where(item => item.Group == group)) notifier.RemoveFromSchedule(item);
    var content = new ToastContentBuilder().AddText("一刻专注 · 提醒验证").AddText("这是一条测试通知，验证程序退出后由 Windows 投递，稍后自动清理。");
    var scheduled = new ScheduledToastNotification(content.GetToastContent().GetXml(), DateTimeOffset.Now.AddSeconds(12)) { Tag = tag, Group = group };
    notifier.AddToSchedule(scheduled);
    var count = notifier.GetScheduledToastNotifications().Count(item => item.Tag == tag && item.Group == group);
    Console.WriteLine($"Scheduled={count}; Setting={notifier.Setting}; Process exits now.");
    return count == 1 ? 0 : 1;
}
var delivered = ToastNotificationManagerCompat.History.GetHistory().Any(item => item.Tag == tag && item.Group == group);
var pending = notifier.GetScheduledToastNotifications().Count(item => item.Tag == tag && item.Group == group);
Console.WriteLine($"DeliveredAfterProcessExit={delivered}; Pending={pending}; Setting={notifier.Setting}");
foreach (var item in notifier.GetScheduledToastNotifications().Where(item => item.Group == group)) notifier.RemoveFromSchedule(item);
ToastNotificationManagerCompat.History.RemoveGroup(group); ToastNotificationManagerCompat.Uninstall();
return delivered && pending == 0 ? 0 : 1;
