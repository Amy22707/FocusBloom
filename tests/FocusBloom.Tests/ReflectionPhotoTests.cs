using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusBloom.Core.Reflection;
using FocusBloom.Desktop;
using FocusBloom.Desktop.Presentation;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Desktop.Views;
using FocusBloom.Desktop.Windows;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

[Collection("Desktop UI")]
public sealed class ReflectionPhotoTests
{
    [Fact]
    public void ImportedAndPastedPicturesUseLocalCopiesAndTravelWithBackup()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary();
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "source", "test.db")); var library = new ReflectionImages(Path.GetDirectoryName(store.DatabasePath)!); var source = Path.Combine(root, "original", "照片.png"); Directory.CreateDirectory(Path.GetDirectoryName(source)!); var bitmap = Picture(); Write(source, bitmap);
                var original = File.ReadAllBytes(source); var uploaded = library.ImportFile(source); var sameName = library.ImportFile(source); var pasted = library.SavePastedImage(bitmap);
                Assert.NotEqual(uploaded, sameName); Assert.Equal(original, File.ReadAllBytes(uploaded)); File.Delete(source); Assert.NotNull(ReflectionImages.ReadImage(uploaded, 200));
                var today = DateOnly.FromDateTime(DateTime.Today); store.SaveDailyReflection(new(today, null, Array.Empty<string>(), Photos: new[] { uploaded, pasted }));
                var old = JsonSerializer.Deserialize<DailyReflection>("{\"date\":\"2026-10-20\",\"mood\":3,\"tags\":[],\"feeling\":\"保留\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web))!; Assert.Null(old.Photos); Assert.Equal("保留", old.Feeling);
                var archive = Path.Combine(root, "images.fbbak"); var result = new LocalBackupService(store.DatabasePath).CreateAsync(archive).GetAwaiter().GetResult(); Assert.Equal(2, result.Manifest.Assets.Count); Assert.Empty(result.Manifest.Missing);
                var target = new SqliteFocusStore(Path.Combine(root, "restored", "test.db")); var backup = new LocalBackupService(target.DatabasePath); using (var prepared = backup.PrepareRestoreAsync(archive).GetAwaiter().GetResult()) backup.ApplyRestore(prepared);
                target = new(target.DatabasePath); var row = Assert.Single(target.GetDailyReflections()); Assert.Null(row.Mood); Assert.Equal(2, row.Photos!.Length);
                foreach (var path in row.Photos) { Assert.StartsWith(Path.Combine(root, "restored", "restored-media"), path); Assert.True(File.Exists(path)); Assert.NotNull(ReflectionImages.ReadImage(path, 200)); }
                library.DeleteCopy(sameName); Assert.False(File.Exists(sameName)); Assert.True(File.Exists(uploaded));
            }
            finally { Clean(root); }
        });
    }

    [Fact]
    public void DiaryViewsDisplayPhotosAndEditingConfirmsRemovalAndCleansCancelledCopies()
    {
        WpfTestHost.Run(() =>
        {
            var root = Temporary(); MainViewModel? main = null; MainWindow? window = null;
            try
            {
                var store = new SqliteFocusStore(Path.Combine(root, "test.db")); var today = DateOnly.FromDateTime(DateTime.Today); var image = Picture(); var source = Path.Combine(root, "source.png"); Write(source, image);
                for (var index = 1; index <= 6; index++) store.SaveDailyReflection(new(today.AddDays(-index), index, Array.Empty<string>()));
                main = new(store); window = new(main, store, Path.Combine(root, "browser")) { Width = 1240, Height = 900 }; window.Show(); main.Navigation.Items.Single(item => item.Key == "reflection").OpenCommand.Execute(null); Flush();
                var page = Children<ReflectionView>(window).Single(); var edit = (Button)page.FindName("EditReflectionButton");
                InDialog<ReflectionEditorWindow>(() => Click(edit), editor =>
                {
                    editor.AddFile(source); editor.AddPastedImage(image); ((Button)editor.FindName("MoodChoice4")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TextBox)editor.FindName("FeelingInput")).Text = "把今天值得记住的小事留下来。"; ((TextBox)editor.FindName("HighlightsInput")).Text = "完成了计划，也留了一点休息的时间。";
                    Assert.Equal(2, ((WrapPanel)editor.FindName("PhotoThumbnails")).Children.Count); Click((Button)editor.FindName("SaveButton"));
                }); Flush(); var photos = store.GetDailyReflections().Single(item => item.Date == today).Photos!; Assert.Equal(2, photos.Length); Assert.All(photos, path => Assert.True(File.Exists(path))); File.Delete(source);
                ((ComboBox)page.FindName("ReflectionViewMode")).SelectedItem = "每日记录"; Flush(); Assert.Equal(Visibility.Collapsed, ((StackPanel)page.FindName("MonthPanel")).Visibility); var pictures = (ItemsControl)page.FindName("ReflectionPhotos"); Assert.Equal(2, pictures.Items.Count);
                Wait(() => Children<Image>(pictures).All(item => item.Source is not null)); GoalsUiTests.Save(window, "reflection-diary-photos-light.png");
                InDialog<PhotoViewerWindow>(() => Click(Children<Button>(pictures).First()), viewer => { Wait(() => Children<Image>(viewer).Any(item => item.Source is not null)); viewer.Close(); });
                main.Reflection.PreviousMonthCommand.Execute(null); Assert.Equal(today.AddDays(-1), main.Reflection.Day); main.Reflection.NextMonthCommand.Execute(null); Assert.Equal(today, main.Reflection.Day); Assert.True(new ReflectionViewModel(store).IsDailyView);
                var folder = Path.Combine(root, "reflection-images"); var before = Directory.GetFiles(folder).Length;
                InDialog<ReflectionEditorWindow>(() => Click(edit), editor => { editor.AddPastedImage(image); editor.Close(); }); Assert.Equal(before, Directory.GetFiles(folder).Length); Assert.Equal(2, store.GetDailyReflections().Single(item => item.Date == today).Photos!.Length);
                InDialog<ReflectionEditorWindow>(() => Click(edit), editor =>
                {
                    var panel = (WrapPanel)editor.FindName("PhotoThumbnails"); var remove = Children<Button>(panel).First(button => Equals(button.Content, "移除"));
                    InDialog<ConfirmDeletionWindow>(() => Click(remove), confirmation => confirmation.Close()); Assert.Equal(2, panel.Children.Count);
                    InDialog<ConfirmDeletionWindow>(() => Click(remove), confirmation => Click((Button)confirmation.FindName("ConfirmDeletionButton"))); Assert.Single(panel.Children); editor.Close();
                }); Assert.Equal(2, store.GetDailyReflections().Single(item => item.Date == today).Photos!.Length);
                main.SelectedAppearanceMode = main.AppearanceModes.Single(item => item.Mode == AppearanceMode.Dark); Flush(); Wait(() => Children<Image>(pictures).All(item => item.Source is not null)); GoalsUiTests.Save(window, "reflection-diary-photos-dark.png");
                main.Reflection.ViewMode = "月视图"; Flush(); Assert.Contains(Children<TextBlock>((StackPanel)page.FindName("MoodCalendar")), item => item.Text == "▧ 2"); GoalsUiTests.Save(window, "reflection-month-photos.png");
                Assert.NotEqual(TestDesktop.Current, TestDesktop.Input);
            }
            finally { foreach (var dialog in Application.Current.Windows.OfType<Window>().Where(item => item != window).ToArray()) dialog.Close(); window?.CloseDesktopWidgetsForExit(); window?.DisposeBrowser(); window?.Close(); main?.Media.Dispose(); Clean(root); }
        });
    }
    private static BitmapSource Picture()
    {
        var visual = new DrawingVisual(); using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(147, 190, 207), Color.FromRgb(231, 232, 215), 90), null, new Rect(0, 0, 600, 360));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(239, 211, 153)), null, new Point(450, 85), 32, 32);
            drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(133, 169, 151)), null, Geometry.Parse("M0,250 Q150,100 300,240 Q470,135 600,220 L600,360 L0,360 Z"));
            drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(89, 135, 124)), null, Geometry.Parse("M0,310 Q140,205 280,300 Q440,215 600,280 L600,360 L0,360 Z"));
        }
        var bitmap = new RenderTargetBitmap(600, 360, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static void Write(string path, BitmapSource image) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file); }
    private static string Temporary() => Path.Combine(Path.GetTempPath(), "FocusBloomReflectionPhotos", Guid.NewGuid().ToString());
    private static void Clean(string root) { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void InDialog<T>(Action open, Action<T> action) where T : Window { Dispatcher.CurrentDispatcher.BeginInvoke(new Action(() => action(Application.Current.Windows.OfType<T>().Single()))); open(); }
    private static void Flush() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Wait(Func<bool> condition) { var until = DateTime.UtcNow.AddSeconds(4); while (!condition() && DateTime.UtcNow < until) { Flush(); Thread.Sleep(15); } Assert.True(condition()); }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    { for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) { var child = VisualTreeHelper.GetChild(parent, index); if (child is T item) yield return item; foreach (var nested in Children<T>(child)) yield return nested; } }
}
