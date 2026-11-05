using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Core.Calendar;

using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public sealed class CourseManagerWindow : CalendarFormWindow
{
    public CourseManagerWindow(MainViewModel main) : base(main, "课程管理") { SaveButton.Content = UiText.Get("完成"); Reload(); }
    private void Reload()
    {
        Fields.Children.Clear();
        var term = Main.Calendar.Store.GetSchoolTerm();
        var semester = MainWindow.OfflineRelease ? Main.Calendar.Store.GetSchoolSemesters().Single(item => item.Active).Name + " · " : "";
        Caption($"{semester}第1周 {term.Start:yyyy-MM-dd} · 共{term.Weeks}周 · {term.Periods.Count}个节次");
        var actions = new WrapPanel();
        if (MainWindow.OfflineRelease)
        {
            var terms = new Button { Content = UiText.Get("学期管理"), Margin = new Thickness(0, 0, 10, 6) };
            terms.Click += (_, _) => { new SchoolSemestersWindow(Main) { Owner = this }.ShowDialog(); Reload(); };
            actions.Children.Add(terms);
        }
        var settings = new Button { Content = UiText.Get("学期与节次"), Margin = new Thickness(0, 0, 10, 0) }; settings.Click += (_, _) => { new SchoolTermWindow(Main) { Owner = this }.ShowDialog(); Reload(); }; actions.Children.Add(settings);
        var add = new Button { Content = UiText.Get("添加课程") }; add.Click += (_, _) => { new CourseEditorWindow(Main) { Owner = this }.ShowDialog(); Reload(); }; actions.Children.Add(add);
        var import = new Button { Content = UiText.Get("导入 CSV"), Margin = new Thickness(10, 0, 0, 0), ToolTip = CourseCsv.Header };
        import.Click += (_, _) => ImportCsv(); actions.Children.Add(import); Fields.Children.Add(actions);
        foreach (var course in Main.Calendar.Store.GetCourses())
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = course.Name, FontWeight = FontWeights.SemiBold });
            var detail = new TextBlock { Text = string.Join("\n", CalendarRules.Meetings(course).Select(meeting => $"周{new[] { "一", "二", "三", "四", "五", "六", "日" }[meeting.Weekday - 1]} · 第{meeting.StartPeriod}—{meeting.EndPeriod}节 · {meeting.Weeks} · {meeting.Room}")), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) }; detail.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush"); panel.Children.Add(detail);
            var edit = new Button { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) }; edit.Click += (_, _) => { new CourseEditorWindow(Main, course) { Owner = this }.ShowDialog(); Reload(); }; Fields.Children.Add(edit);
        }
        if (Main.Calendar.Store.GetCourses().Count == 0) Caption("还没有课程，先设置学期和节次，再添加课程。");
        Finish();
    }
    private void ImportCsv()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Title = UiText.Get("导入课程 CSV"), Filter = "CSV 文件|*.csv" };
        if (picker.ShowDialog(this) != true) return;
        try
        {
            using var input = File.OpenText(picker.FileName);
            var store = Main.Calendar.Store;
            var courses = CourseCsv.Read(input, store.GetSchoolTerm(), store.GetCourses());
            if (courses.Count == 0) { Message.Text = UiText.Get("没有需要新增的课程时段。"); return; }
            var count = courses.Sum(course => CalendarRules.Meetings(course).Count);
            if (MessageBox.Show(this, $"将向当前学期新增 {courses.Count} 门课程的 {count} 个时段，现有课程保留。", "导入课程", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            store.ImportCourses(courses); Main.ReloadCalendarData(); Reload(); Message.Text = $"已导入 {count} 个课程时段。";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or Microsoft.Data.Sqlite.SqliteException)
        { Message.Text = UiText.Get("导入失败：") + error.Message; }
    }
    protected override void Save() => Close();
}
