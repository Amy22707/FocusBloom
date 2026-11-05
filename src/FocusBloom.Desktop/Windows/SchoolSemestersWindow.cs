using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FocusBloom.Core.Calendar;
using FocusBloom.Desktop.ViewModels;

using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.Windows;

public sealed class SchoolSemestersWindow : CalendarFormWindow
{
    private readonly TextBox _newName;
    private readonly DatePicker _start;
    public SchoolSemestersWindow(MainViewModel main) : base(main, "学期管理")
    {
        var store = main.Calendar.Store;
        var semesters = store.GetSchoolSemesters();
        var active = semesters.Single(item => item.Active);
        Caption(active.Label);
        var renameInput = AddTextInput("SemesterNameInput", "修改当前学期名称", active.Name);
        var rename = new Button { Content = UiText.Get("保存名称"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 12) };
        rename.Click += (_, _) => Commit(() => store.RenameSchoolSemester(renameInput.Text));
        Fields.Children.Add(rename);
        if (semesters.Any(item => !item.Active))
        {
            var choices = Choice("ArchivedSemesters", "已归档学期", semesters.Where(item => !item.Active).ToArray(), display: nameof(SchoolSemester.Label));
            var open = new Button { Content = UiText.Get("切换到所选学期"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 12) };
            open.Click += (_, _) =>
            {
                if (choices.SelectedItem is SchoolSemester selected)
                    Commit(() => { store.SelectSchoolSemester(selected.Id); main.Calendar.Date = selected.Start; });
            };
            Fields.Children.Add(open);
        }
        Caption("新建和切换学期会保留原学期的课程、节次及调课记录。");
        _newName = AddTextInput("NewSemesterNameInput", "新学期名称");
        _start = DateInput("NewSemesterStart", "第一教学周的周一", CalendarRules.Monday(DateOnly.FromDateTime(DateTime.Today)));
        SaveButton.Content = UiText.Get("新建并切换");
        Finish();
    }
    protected override void Save()
    {
        if (_start.SelectedDate is not { DayOfWeek: DayOfWeek.Monday } date) { Message.Text = UiText.Get("请选择第一教学周的周一。"); return; }
        Commit(() =>
        {
            var day = DateOnly.FromDateTime(date);
            Main.Calendar.Store.CreateSchoolSemester(_newName.Text, day);
            Main.Calendar.Date = day;
        });
    }
}
