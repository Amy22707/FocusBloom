using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FocusBloom.Desktop.ViewModels;

namespace FocusBloom.Desktop.Presentation;

public sealed class QuoteAttributionText : TextBlock
{
    public static readonly DependencyProperty QuoteProperty = DependencyProperty.Register(nameof(Quote), typeof(FocusQuote), typeof(QuoteAttributionText), new PropertyMetadata(null, (sender, _) => ((QuoteAttributionText)sender).RenderQuote()));
    public FocusQuote? Quote { get => (FocusQuote?)GetValue(QuoteProperty); set => SetValue(QuoteProperty, value); }
    private void RenderQuote()
    {
        Inlines.Clear();
        if (Quote is not { } quote) return;
        Append(quote.Author, false);
        Append(quote.Work, !HasChineseText(quote.Work));
        Append(quote.Translator.Length > 0 ? $"译者：{quote.Translator}" : "", false);
    }
    private static bool HasChineseText(string text) => text.Any(character => character is >= '\u3000' and <= '\u303F' or >= '\u4E00' and <= '\u9FFF');
    private void Append(string text, bool italic)
    {
        if (text.Length == 0) return;
        if (Inlines.Count > 0) Inlines.Add(new Run(" · "));
        Inlines.Add(new Run(text) { FontStyle = italic ? FontStyles.Italic : FontStyles.Normal });
    }
}
