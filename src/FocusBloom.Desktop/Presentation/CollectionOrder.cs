using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace FocusBloom.Desktop.Presentation;

public enum OrderMove { First, Previous, Next, Last }

public static class CollectionOrder
{
    public static bool Move<T>(ObservableCollection<T> items, T item, OrderMove direction, IEnumerable<T>? visible = null, T? targetItem = default) where T : class
    {
        var shown = (visible ?? items).ToList();
        var index = shown.IndexOf(item);
        if (index < 0) return false;
        var target = targetItem is not null ? shown.IndexOf(targetItem) : direction switch { OrderMove.First => 0, OrderMove.Previous => Math.Max(0, index - 1), OrderMove.Next => Math.Min(shown.Count - 1, index + 1), _ => shown.Count - 1 };
        if (target < 0) return false;
        if (target == index) return false;
        items.Move(items.IndexOf(item), items.IndexOf(shown[target]));
        return true;
    }

    public static void Apply<T, TKey>(ObservableCollection<T> items, IReadOnlyList<TKey> order, Func<T, TKey> key)
    {
        var keys = order.ToList();
        var sorted = items.OrderBy(item => { var index = keys.IndexOf(key(item)); return index < 0 ? int.MaxValue : index; }).ToArray();
        for (var index = 0; index < sorted.Length; index++)
        {
            var current = items.IndexOf(sorted[index]);
            if (current != index) items.Move(current, index);
        }
    }
}
