using Avalonia.Controls;
using Avalonia.Input;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private Control DragHandle(string kind, Guid id)
    {
        var handle = Label("≡", 18);
        handle.Width = 24; handle.Cursor = new Cursor(StandardCursorType.SizeAll);
        ToolTip.SetTip(handle, T("拖动到另一个分组", "Drag to another group"));
        handle.PointerPressed += async (_, args) =>
        {
            if (!args.GetCurrentPoint(handle).Properties.IsLeftButtonPressed) return;
            try
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText($"FocusBloom:{kind}:{id:D}"));
                await DragDrop.DoDragDropAsync(args, data, DragDropEffects.Move);
            }
            catch (Exception error) { Report(error.Message); }
        };
        return handle;
    }
    private Control GroupHeader(string name, string kind, Action<Guid> move)
    {
        var header = Card(Label(name, 17, true));
        DragDrop.SetAllowDrop(header, true);
        header.AddHandler(DragDrop.DragOverEvent, (_, args) =>
        {
            args.DragEffects = args.DataTransfer.TryGetText()?.StartsWith("FocusBloom:" + kind + ":", StringComparison.Ordinal) == true ? DragDropEffects.Move : DragDropEffects.None;
            args.Handled = true;
        });
        header.AddHandler(DragDrop.DropEvent, (_, args) =>
        {
            var prefix = "FocusBloom:" + kind + ":";
            var text = args.DataTransfer.TryGetText();
            if (text?.StartsWith(prefix, StringComparison.Ordinal) != true || !Guid.TryParse(text[prefix.Length..], out var id)) return;
            try { move(id); Refresh(); }
            catch (Exception error) { Report(error.Message); }
            args.Handled = true;
        });
        return header;
    }
}
