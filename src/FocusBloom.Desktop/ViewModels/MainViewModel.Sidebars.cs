using System;
using System.Globalization;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private double? _todoSidebarWidth, _projectSidebarWidth, _navigationWidth;
    public double TodoSidebarWidth
    {
        get => _todoSidebarWidth ??= LoadSidebarWidth("todos", 220);
        set => SaveSidebarWidth(ref _todoSidebarWidth, value, "todos", nameof(TodoSidebarWidth));
    }
    public double ProjectSidebarWidth
    {
        get => _projectSidebarWidth ??= LoadSidebarWidth("projects", 220);
        set => SaveSidebarWidth(ref _projectSidebarWidth, value, "projects", nameof(ProjectSidebarWidth));
    }
    public double NavigationWidth
    {
        get => _navigationWidth ??= LoadSidebarWidth("navigation", UiText.IsEnglish ? 200 : 160);
        set => SaveSidebarWidth(ref _navigationWidth, value, "navigation", nameof(NavigationWidth));
    }

    private double LoadSidebarWidth(string name, double fallback) =>
        double.TryParse(_store.GetSetting("ui.sidebar.width." + name), CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? Math.Clamp(value, 160, 360) : fallback;

    private void SaveSidebarWidth(ref double? field, double value, string name, string property)
    {
        if (!double.IsFinite(value)) return;
        value = Math.Clamp(value, 160, 360);
        if (field == value) return;
        field = value;
        Notify(property);
        try { _store.SaveSetting("ui.sidebar.width." + name, value.ToString(CultureInfo.InvariantCulture)); }
        catch (Exception error) { ReportStatus(UiText.Format("侧栏宽度保存失败：{0}", error.Message)); }
    }
}
