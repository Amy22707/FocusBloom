namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public StatisticsViewModel Statistics { get; }
    private string _recordPage = "时间轴";
    public string RecordPage
    {
        get => _recordPage;
        set
        {
            if (_recordPage == value) return; _recordPage = value; _store.SaveSetting("records.page", value);
            Statistics.ReviewMode = value == "复盘";
            Notify(); Notify(nameof(ShowRecordCalendar)); Notify(nameof(ShowRecordStatistics));
        }
    }
    public bool ShowRecordCalendar => RecordPage == "时间轴";
    public bool ShowRecordStatistics => !ShowRecordCalendar;
}
