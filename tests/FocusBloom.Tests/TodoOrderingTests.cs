using System.IO;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class TodoOrderingTests
{
    [Fact]
    public void QuadrantOrderingUsesVisibleItemsKeepsCompletedLastAndSurvivesReopen()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FocusBloomTodoOrdering", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(directory, "test.db"));
            var library = new TodoLibraryViewModel(store, _ => { }, _ => true);
            var folder = library.SaveFolder(null, "学习", "未分类")!;
            var otherFolder = library.SaveFolder(null, "工作", "未分类")!;
            var first = new TodoItem(Guid.NewGuid(), folder.Id, "阅读", Important: true, Urgent: true);
            var second = first with { Id = Guid.NewGuid(), Title = "写作" };
            var third = first with { Id = Guid.NewGuid(), FolderId = otherFolder.Id, Title = "邮件" };
            var otherQuadrant = first with { Id = Guid.NewGuid(), Title = "复习", Urgent = false };
            var completed = first with { Id = Guid.NewGuid(), Title = "已完成一", Status = TodoStatus.Completed };
            var completedSecond = completed with { Id = Guid.NewGuid(), Title = "已完成二" };
            foreach (var item in new[] { first, otherQuadrant, second, third, completed, completedSecond }) Assert.True(library.SaveItem(item));
            library.ViewMode = "四象限"; library.QuadrantScope = "全部文件夹";
            var quadrant = library.Quadrants.Single(group => group.Important && group.Urgent);
            TodoRow Row(Guid id) => library.Items.Single(item => item.Id == id);
            Guid[] Order() => quadrant.Items.Select(item => item.Id).ToArray();
            library.MoveItem(Row(third.Id), OrderMove.First, quadrant: quadrant);
            Assert.Equal(new[] { third.Id, first.Id, second.Id, completed.Id, completedSecond.Id }, Order());
            library.MoveItem(Row(first.Id), OrderMove.First, Row(second.Id), quadrant);
            Assert.Equal(new[] { third.Id, second.Id, first.Id, completed.Id, completedSecond.Id }, Order());
            library.MoveItem(Row(completedSecond.Id), OrderMove.First, quadrant: quadrant);
            Assert.Equal(new[] { third.Id, second.Id, first.Id, completedSecond.Id, completed.Id }, Order());
            library.MoveItem(Row(completed.Id), OrderMove.First, Row(third.Id), quadrant);
            Assert.Equal(new[] { third.Id, second.Id, first.Id, completedSecond.Id, completed.Id }, Order());
            library.SelectedFolder = folder; library.QuadrantScope = "当前文件夹";
            library.MoveItem(Row(first.Id), OrderMove.First, quadrant: quadrant);
            Assert.Equal(new[] { first.Id, second.Id, completedSecond.Id, completed.Id }, Order());
            library.QuadrantScope = "全部文件夹";
            var beforeSearch = Order(); library.Search = "已完成";
            library.MoveItem(Row(completedSecond.Id), OrderMove.Last, quadrant: quadrant);
            library.Search = "";
            Assert.Equal(beforeSearch.Take(3).Concat(new[] { completed.Id, completedSecond.Id }), Order());
            var restored = new TodoLibraryViewModel(store, _ => { }, _ => true);
            Assert.Equal(Order(), restored.Quadrants.Single(group => group.Important && group.Urgent).Items.Select(item => item.Id));
            Assert.Equal(otherQuadrant.Id, Assert.Single(restored.Quadrants.Single(group => group.Important && !group.Urgent).Items).Id);
            Assert.All(store.GetTodos().Where(item => item.Id != otherQuadrant.Id), item => Assert.True(item.Important && item.Urgent));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
