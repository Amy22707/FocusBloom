using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Todos;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan time) => _now += time;
    }

    internal async Task VerifyStartup()
    {
        await Task.Delay(300);
        if (!IsVisible || Bounds.Width <= 0) throw new InvalidOperationException("Application window did not open.");
        var clock = new TestClock();
        var timer = new FocusTimer(clock);
        var project = new FocusProject(Guid.NewGuid(), "Reading", "Sample", "#427B72");
        _store.SaveFolder(project.Category);
        _store.SaveProject(project);
        var folder = new TodoFolder(Guid.NewGuid(), "Courses", "Sample");
        _store.SaveTodoCategory(folder.Category); _store.SaveTodoFolder(folder);
        var todo = new TodoItem(Guid.NewGuid(), folder.Id, "Math assignment", DueDate: Today);
        _store.SaveTodo(todo);
        var item = new ChecklistEntry(Guid.NewGuid(), "Read a chapter", false);
        var plan = new PeriodicChecklist(_store);
        var scope = ChecklistScope.For(ChecklistPeriod.Day, Today);
        plan.Save(scope, [item]);
        plan.Save(scope, [item with { Text = "Read two chapters", Done = true }]);
        var options = new FocusOptions(project.Id, TimerMode.CountUp, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.Zero,
            PlannerTitle: item.Text, PlannerItemId: item.Id);
        timer.Start(options); clock.Advance(TimeSpan.FromSeconds(9)); timer.End(); SaveRecord(timer.LastRecord!);
        timer.Start(options); clock.Advance(TimeSpan.FromSeconds(10)); timer.End(); SaveRecord(timer.LastRecord!);
        timer.Start(options); clock.Advance(TimeSpan.FromHours(25)); timer.Update(); SaveRecord(timer.LastRecord!);
        var records = _store.GetRecords();
        if (records.Count != 2 || records.All(r => r.EndReason != EndReason.DurationLimit || r.FocusDuration != TimeSpan.FromHours(24)) ||
            records.Any(r => r.PlannerItemId != item.Id) || plan.Read(scope).Single().Text != "Read two chapters")
            throw new InvalidOperationException("Focus or planner data did not survive the expected transitions.");
        _store.SaveDailyReflection(new DailyReflection(Today, 5, [], "Good day"));
        _store.SaveHabit(new(Guid.NewGuid(), "Reading", [1, 2, 3, 4, 5, 6, 7], Today));
        foreach (var english in new[] { false, true })
        {
            _english = english;
            for (var section = 0; section <= 8; section++) Navigate(section);
        }
        await VerifyWindowsBackup();
        Navigate(0);
        await Task.Delay(200);
        await CaptureFocus();
        if (!Directory.EnumerateFiles(Path.Combine(_data, "screenshots"), "*.png").Any())
            throw new InvalidOperationException("Focus screenshot was not saved.");
        Console.WriteLine("FOCUSBLOOM_SMOKE_OK: window, nine pages, timer limits, planner identity, Windows backup restored, screenshot saved");
    }

    private async Task VerifyWindowsBackup()
    {
        const string original = @"C:\Users\Sample\Pictures\study.png";
        const string archiveEntry = "assets/0000/study.png";
        var source = new SqliteFocusStore(Path.Combine(_data, "windows-fixture.db"));
        source.SaveSetting("focus.media", JsonSerializer.Serialize(new
        {
            backgroundId = "local:" + original,
            customImages = new[] { new { id = "local:" + original, name = "Study", file = original, custom = true } },
            customQuotes = new[] { new { id = "sample", text = "Keep going", custom = true } }
        }));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var file = Path.Combine(_data, "windows-fixture.fbbak");
        using (var zip = ZipFile.Open(file, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(source.DatabasePath, "database.sqlite");
            using (var writer = new StreamWriter(zip.CreateEntry(archiveEntry).Open())) writer.Write("image bytes");
            var manifest = new BackupManifest("FocusBloomBackup", 1, DateTimeOffset.Now, new Dictionary<string, int>(),
                [new(original, archiveEntry, 11)], [new(@"C:\Users\Sample\Pictures", "assets/0000")], []);
            using var output = zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(output, manifest, Json);
        }
        var target = new SqliteFocusStore(Path.Combine(_data, "restore-verification", "focusbloom.db"));
        var service = new LocalBackupService(target.DatabasePath);
        using var restore = await service.PrepareRestoreAsync(file);
        service.ApplyRestore(restore);
        var settings = JsonNode.Parse(target.GetSetting("focus.media")!)!;
        var image = settings["customImages"]![0]!["file"]!.GetValue<string>();
        if (File.ReadAllText(image) != "image bytes" || settings["backgroundId"]!.GetValue<string>() != "local:" + image ||
            settings["customQuotes"]![0]!["text"]!.GetValue<string>() != "Keep going")
            throw new InvalidOperationException("Windows backup paths or custom media were not restored.");
        var exported = await service.CreateAsync(Path.Combine(_data, "mac-fixture.fbbak"));
        if (exported.Manifest.Assets.Count != 1 || exported.Manifest.Missing.Count != 0)
            throw new InvalidOperationException("Restored assets did not survive re-export.");
    }
}
