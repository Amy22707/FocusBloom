using System.Windows.Controls;

namespace FocusBloom.Desktop.Presentation;

public sealed class SelectableText : TextBox
{
    public SelectableText() => SetResourceReference(StyleProperty, "SelectableText");
}
