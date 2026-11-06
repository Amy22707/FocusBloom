using System.Text.Json.Nodes;
using Avalonia.Controls;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private async Task ManageMedia(bool quotes)
    {
        var media = Media();
        var custom = quotes ? List(media, "customQuotes") : List(media, "customImages");
        var records = custom.OfType<JsonObject>().ToArray();
        if (records.Length == 0) { Report(T("请先添加自定义素材。", "Add some custom media first.")); return; }
        var pick = Choice(records.Select(item => Value(item, quotes ? "text" : "name") ?? ""));
        pick.MaxWidth = 350;
        var text = Input(Value(records[0], quotes ? "text" : "name") ?? "", quotes);
        var remove = new CheckBox { Content = T("从素材库移除", "Remove from the library") };
        var favorite = new CheckBox { Content = T("收藏", "Favorite") };
        var field = quotes ? "text" : "name";
        var favoritesKey = quotes ? "favoriteQuoteIds" : "favoriteIds";
        var removedKey = quotes ? "removedQuoteIds" : "removedImageIds";
        pick.SelectionChanged += (_, _) =>
        {
            if (pick.SelectedIndex < 0) return;
            var item = records[pick.SelectedIndex];
            text.Text = Value(item, field); remove.IsChecked = false;
            favorite.IsChecked = List(media, favoritesKey).Any(id => id?.GetValue<string>() == Value(item, "id"));
        };
        favorite.IsChecked = List(media, favoritesKey).Any(id => id?.GetValue<string>() == Value(records[0], "id"));
        if (await Form(quotes ? T("管理句子", "Manage quotes") : T("管理背景", "Manage backgrounds"), [pick, text, favorite, remove], () =>
        {
            var item = records[pick.SelectedIndex];
            var id = Value(item, "id")!;
            item[field] = Required(text);
            var favorites = List(media, favoritesKey);
            for (var index = favorites.Count - 1; index >= 0; index--) if (favorites[index]?.GetValue<string>() == id) favorites.RemoveAt(index);
            if (favorite.IsChecked == true) favorites.Add(id);
            if (remove.IsChecked == true)
            {
                if (!List(media, removedKey).Any(node => node?.GetValue<string>() == id)) List(media, removedKey).Add(id);
                if (Value(media, quotes ? "quoteId" : "backgroundId") == id) media[quotes ? "quoteId" : "backgroundId"] = quotes ? "" : "theme";
            }
            SaveMedia(media);
        })) Refresh();
    }
}
