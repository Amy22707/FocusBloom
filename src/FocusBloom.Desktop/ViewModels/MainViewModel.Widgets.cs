using FocusBloom.Core.Focus;

namespace FocusBloom.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public DesktopWidgetsViewModel Widgets { get; }
    public FocusRecord? WidgetCurrentRecord => _timer.CaptureCurrentRecord() ?? (HasUnsavedRecord && _timer.LastRecord!.FocusDuration.TotalSeconds >= 10 ? _timer.LastRecord : null);
}
