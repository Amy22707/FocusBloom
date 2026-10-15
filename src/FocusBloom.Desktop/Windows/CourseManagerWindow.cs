using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Core.Calendar;

namespace FocusBloom.Desktop.Windows;

public sealed class CourseManagerWindow : CalendarFormWindow
{
    public CourseManagerWindow(MainViewModel main) : base(main, "课程管理") { SaveButton.Content = "完成"; Reload(); }
    private void Reload()
    {
        Fields.Children.Clear();
        var term = Main.Calendar.Store.GetSchoolTerm(); Caption($"第1周 {term.Start:yyyy-MM-dd} · 共{term.Weeks}周 · {term.Periods.Count}个节次");
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var settings = new Button { Content = "学期与节次", Margin = new Thickness(0, 0, 10, 0) }; settings.Click += (_, _) => { new SchoolTermWindow(Main) { Owner = this }.ShowDialog(); Reload(); }; actions.Children.Add(settings);
        var add = new Button { Content = "添加课程" }; add.Click += (_, _) => { new CourseEditorWindow(Main) { Owner = this }.ShowDialog(); Reload(); }; actions.Children.Add(add); Fields.Children.Add(actions);
        foreach (var course in Main.Calendar.Store.GetCourses())
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = course.Name, FontWeight = FontWeights.SemiBold });
            var detail = new TextBlock { Text = string.Join("\n", CalendarRules.Meetings(course).Select(meeting => $"周{new[] { "一", "二", "三", "四", "五", "六", "日" }[meeting.Weekday - 1]} · 第{meeting.StartPeriod}—{meeting.EndPeriod}节 · {meeting.Weeks} · {meeting.Room}")), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(detail);
            var edit = new Button { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) }; edit.Click += (_, _) => { new CourseEditorWindow(Main, course) { Owner = this }.ShowDialog(); Reload(); }; Fields.Children.Add(edit);
        }
        if (Main.Calendar.Store.GetCourses().Count == 0) Caption("还没有课程，先设置学期和节次，再添加课程。");
    }
    protected override void Save() => Close();
}
