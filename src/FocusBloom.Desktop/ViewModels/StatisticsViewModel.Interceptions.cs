using System;
using FocusBloom.Core.Statistics;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class StatisticsViewModel
{
    public InterceptionStatistics PreviousInterceptions { get; private set; } = new(0, 0, 0, 0);
    public DateOnly PreviousInterceptionFrom { get; private set; }
    public DateOnly PreviousInterceptionTo { get; private set; }
    public string InterceptionRateText => Interceptions.PerHour is { } rate ? $"{rate:0.##} 次/小时" : "—";
    public double? InterceptionRateRelativeChange => Interceptions.PerHour is { } current && PreviousInterceptions.PerHour is > 0 ? current / PreviousInterceptions.PerHour.Value - 1 : null;
    public string InterceptionComparisonText
    {
        get
        {
            if (Interceptions.PerHour is not { } current) return "暂无可比较数据";
            if (PreviousInterceptions.PerHour is not { } previous) return "上一周期暂无数据";
            if (current == previous) return "与上一周期持平";
            if (previous == 0) return $"较上一周期增加 {current:0.##} 次/小时";
            var change = Math.Abs(current / previous - 1); var percent = change < .001 ? "不足0.1%" : change.ToString("P1");
            return (current < previous ? "↓ 较上一周期降低 " : "↑ 较上一周期增加 ") + percent;
        }
    }
    public string InterceptionComparisonTooltip => $"上一周期 {PreviousInterceptionFrom:yyyy-MM-dd} — {PreviousInterceptionTo:yyyy-MM-dd}\n"
        + $"{PreviousInterceptions.CountedSessions} 次已统计严格专注 · 有效专注 {Duration(PreviousInterceptions.CountedDuration)}\n"
        + (PreviousInterceptions.PerHour is { } rate ? $"每专注小时拦截 {rate:0.##} 次" : "没有可用于计算频率的数据");
    private void RefreshInterceptionComparison()
    {
        var from = Snapshot.Filter.From; var to = Snapshot.Filter.To;
        PreviousInterceptionTo = from.AddDays(-1);
        PreviousInterceptionFrom = Range == "月" ? from.AddMonths(-1) : Range == "年" ? from.AddYears(-1) : from.AddDays(-(to.DayNumber - from.DayNumber + 1));
        PreviousInterceptions = IsValid ? InterceptionStatistics.Build(FocusStatistics.Build(_records,
            Snapshot.Filter with { From = PreviousInterceptionFrom, To = PreviousInterceptionTo })) : new(0, 0, 0, 0);
    }
}
