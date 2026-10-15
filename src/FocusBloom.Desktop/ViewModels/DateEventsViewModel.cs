using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed class DateEventRow(DateEvent item, DateOnly today) : ObservableObject
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private ImageSource? _thumbnail;
    private Task? _loading;
    public DateEvent Item { get; } = item;
    public string Title => Item.Title;
    public DateOnly? Next => CalendarRules.NextDate(Item, today);
    public int Days => Item.Mode == DateCountMode.CountUp ? today.DayNumber - Item.Date.DayNumber : (Next ?? Item.Date).DayNumber - today.DayNumber;
    public string Number => Item.Mode == DateCountMode.Countdown && Item.Repeat != DateRepeat.None && Next is null ? "—" : Math.Abs(Days).ToString();
    public string Label => Item.Mode == DateCountMode.Countdown && Item.Repeat != DateRepeat.None && Next is null ? "循环已结束" : Item.Mode == DateCountMode.CountUp ? Days >= 0 ? "已经" : "距离开始" : Days >= 0 ? "还有" : "已过去";
    public string Detail => $"{Next ?? Item.Date:yyyy-MM-dd}" + (Item.Time is { } time ? $" {time:HH:mm}" : "") + (Item.Repeat != DateRepeat.None ? " · " + (Item.Repeat switch { DateRepeat.Daily => "每日", DateRepeat.Monthly => "每月", _ => "每年" }) : "") + (Item.Pinned ? " · 置顶" : "");
    public string Notes => Item.Notes;
    public string? Cover => Item.CoverPath;
    public ImageSource? Thumbnail { get { _loading ??= LoadCover(); return _thumbnail; } }
    public bool HasCover => _thumbnail is not null;
    private async Task LoadCover()
    {
        if (string.IsNullOrWhiteSpace(Cover)) return;
        var image = await Task.Run(() => FocusMediaViewModel.LoadImage(Cover, 720)).ConfigureAwait(false);
        await _dispatcher.InvokeAsync(() => { _thumbnail = image; Notify(nameof(Thumbnail)); Notify(nameof(HasCover)); });
    }
}
public sealed class DateEventsViewModel(IFocusStore store) : ObservableObject
{
    public ObservableCollection<DateEventRow> Items { get; } = [];
    public void Reload()
    {
        Items.Clear(); var today = DateOnly.FromDateTime(DateTime.Today);
        foreach (var item in store.GetDateEvents().OrderByDescending(item => item.Pinned).ThenBy(item => CalendarRules.NextDate(item, today))) Items.Add(new(item, today));
        Notify(nameof(IsEmpty));
    }
    public bool IsEmpty => Items.Count == 0;
}
