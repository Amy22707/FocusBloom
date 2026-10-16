using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FocusBloom.Core.Calendar;
using FocusBloom.Core.Focus;
using FocusBloom.Core.Goals;
using FocusBloom.Core.Projects;
using FocusBloom.Core.Reflection;
using FocusBloom.Core.Todos;
using FocusBloom.Desktop.ViewModels;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;
using Xunit;

namespace FocusBloom.Tests;

public sealed class LocalBackupTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [Fact]
    public async Task PortableArchiveRestoresAllDataAndRebasesMediaIdsCoversAndWidgetImagesWithoutNameCollisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomBackupRoundtrip", Guid.NewGuid().ToString());
        try
        {
            var source = new SqliteFocusStore(Path.Combine(root, "source", "focusbloom.db")); var day = new DateOnly(2026, 10, 17);
            var project = new FocusProject(Guid.NewGuid(), "论文", "学习", "#9981AE"); source.SaveFolder("学习"); source.SaveProject(project);
            var folder = new TodoFolder(Guid.NewGuid(), "论文", "未分类"); source.SaveTodoFolder(folder); var todo = new TodoItem(Guid.NewGuid(), folder.Id, "实验", DueDate: day, Important: true, ResumeNote: "跑第二组实验"); source.SaveTodo(todo);
            var at = DateTimeOffset.Now.AddHours(-1); var record = new FocusRecord(Guid.NewGuid(), project.Id, TimerMode.CountUp, at, at.AddMinutes(30), EndReason.Manual, new[] { new FocusInterval(IntervalKind.Focus, at, at.AddMinutes(30)) }, Strict: true, TodoId: todo.Id, InterceptionCount: 2); source.SaveRecord(record); source.SaveOutcome(new(record.Id, "完成实验", 2, "组", "整理结果"));
            var files = new[] { Path.Combine(root, "original", "one", "同名.png"), Path.Combine(root, "original", "two", "同名.png"), Path.Combine(root, "original", "one", "动态.mp4"), Path.Combine(root, "original", "two", "环境.wav") };
            for (var index = 0; index < files.Length; index++) { Directory.CreateDirectory(Path.GetDirectoryName(files[index])!); await File.WriteAllTextAsync(files[index], "素材内容" + index); }
            var images = files.Take(3).Select(path => new FocusAsset("local:" + path, "自定义显示名 " + Path.GetFileName(path), path, Custom: true)).ToList(); var sound = new FocusAsset("local:" + files[3], "我的声音", files[3], Custom: true);
            var media = new MediaPreferences { CustomImages = images, CustomSounds = new() { sound }, CustomQuotes = new() { new("custom-quote", "保留我的句子。", "我", Custom: true) }, BackgroundId = images[0].Id, SoundId = sound.Id, FavoriteIds = new() { images[0].Id }, ExcludedIds = new() { images[1].Id }, ImageOrder = images.Select(item => item.Id).Reverse().ToList(), ImageFolders = new() { Path.GetDirectoryName(files[0])! } };
            source.SaveSetting("focus.media", JsonSerializer.Serialize(media, Json)); source.SaveSetting("widgets.Dates", JsonSerializer.Serialize(new DesktopWidgetPreferences(BackgroundMode: "图片", BackgroundImage: files[0]))); source.SaveSetting("widgets.calendar.view", "月");
            var dateEvent = new DateEvent(Guid.NewGuid(), "提交论文", day, CoverPath: files[1]); source.SaveDateEvent(dateEvent); var plan = new CalendarPlan(Guid.NewGuid(), "实验安排", day, new(14, 0), TodoId: todo.Id); source.SaveCalendarPlan(plan);
            var course = new SchoolCourse(Guid.NewGuid(), "系统课", 1, 1, 2); source.SaveCourse(course); var change = new CourseChange(Guid.NewGuid(), course.Id, day, true); source.SaveCourseChange(change);
            var habit = new Habit(Guid.NewGuid(), "阅读", new[] { 1, 2, 3, 4, 5, 6, 7 }, day.AddDays(-10)); source.SaveHabit(habit); source.SetHabitCheckIn(habit.Id, day, true); var goal = new FocusGoal(Guid.NewGuid(), "读十天", GoalMetric.CheckInDays, 10, GoalCycle.Total, day.AddDays(-10), HabitId: habit.Id); source.SaveGoal(goal); source.SaveDailyReflection(new(day, 4, new[] { "学习" }, "顺利", "做完实验", "继续")); source.SaveSetting("ui.navigation.collapsed", "True");
            var backupFile = Path.Combine(root, "portable.fbbak"); var backup = await new LocalBackupService(source.DatabasePath).CreateAsync(backupFile); Assert.Empty(backup.Manifest.Missing); Assert.Equal(4, backup.Manifest.Assets.Count); Assert.Equal(1, backup.Manifest.Counts["专注记录"]); Assert.Equal(1, backup.Manifest.Counts["待办"]);
            Directory.Delete(Path.Combine(root, "original"), true); source.SaveProject(project with { Name = "备份之后的修改" });
            var target = new SqliteFocusStore(Path.Combine(root, "other-computer", "focusbloom.db")); var current = new FocusProject(Guid.NewGuid(), "当前内容", "本机", "#123456"); target.SaveProject(current); var options = new BackupOptions(true, Path.Combine(root, "my-backups"), 3); target.SaveSetting(LocalBackupService.OptionsKey, JsonSerializer.Serialize(options));
            var service = new LocalBackupService(target.DatabasePath); using var prepared = await service.PrepareRestoreAsync(backupFile); Assert.Contains(target.GetProjects(), item => item.Id == current.Id); service.ApplyRestore(prepared);
            var reopened = new SqliteFocusStore(target.DatabasePath); Assert.DoesNotContain(reopened.GetProjects(), item => item.Id == current.Id); Assert.Contains(reopened.GetProjects(), item => item == project); Assert.Equal(todo, Assert.Single(reopened.GetTodos())); Assert.Equal(record.Id, Assert.Single(reopened.GetRecords()).Id); Assert.Equal(2, reopened.GetRecords()[0].InterceptionCount); Assert.Equal("完成实验", Assert.Single(reopened.GetOutcomes()).Summary);
            Assert.Equal(plan, Assert.Single(reopened.GetCalendarPlans())); Assert.Equal(course, Assert.Single(reopened.GetCourses())); Assert.Equal(change, Assert.Single(reopened.GetCourseChanges())); Assert.Single(reopened.GetHabitCheckIns()); Assert.Equal(goal, Assert.Single(reopened.GetGoals())); Assert.Equal("顺利", Assert.Single(reopened.GetDailyReflections()).Feeling); Assert.Equal("True", reopened.GetSetting("ui.navigation.collapsed")); Assert.Equal(JsonSerializer.Serialize(options), reopened.GetSetting(LocalBackupService.OptionsKey));
            var restoredMedia = JsonSerializer.Deserialize<MediaPreferences>(reopened.GetSetting("focus.media")!, Json)!; Assert.Equal("保留我的句子。", Assert.Single(restoredMedia.CustomQuotes).Text);
            for (var index = 0; index < 3; index++) { Assert.Equal("素材内容" + index, File.ReadAllText(restoredMedia.CustomImages[index].File)); Assert.StartsWith(Path.Combine(root, "other-computer", "restored-media"), restoredMedia.CustomImages[index].File); Assert.Equal("local:" + restoredMedia.CustomImages[index].File, restoredMedia.CustomImages[index].Id); Assert.Equal(images[index].Name, restoredMedia.CustomImages[index].Name); }
            Assert.Equal(restoredMedia.CustomImages[0].Id, restoredMedia.BackgroundId); Assert.Equal(restoredMedia.CustomImages[0].Id, Assert.Single(restoredMedia.FavoriteIds)); Assert.Equal(restoredMedia.CustomImages[1].Id, Assert.Single(restoredMedia.ExcludedIds)); Assert.Equal(restoredMedia.CustomImages.Select(item => item.Id).Reverse(), restoredMedia.ImageOrder); Assert.True(Directory.Exists(Assert.Single(restoredMedia.ImageFolders))); Assert.Equal("素材内容3", File.ReadAllText(Assert.Single(restoredMedia.CustomSounds).File));
            Assert.Equal(restoredMedia.CustomImages[1].File, Assert.Single(reopened.GetDateEvents()).CoverPath); var widget = JsonSerializer.Deserialize<DesktopWidgetPreferences>(reopened.GetSetting("widgets.Dates")!)!; Assert.Equal(restoredMedia.CustomImages[0].File, widget.BackgroundImage);
            Assert.DoesNotContain(Directory.GetDirectories(Path.Combine(service.ProfileDirectory, "backup-work")), path => path != prepared.Directory);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task MissingSourceMediaIsListedAndUnusableArchiveNeverReplacesCurrentData()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomBackupErrors", Guid.NewGuid().ToString());
        try
        {
            var store = new SqliteFocusStore(Path.Combine(root, "focusbloom.db")); var missing = Path.Combine(root, "missing.png"); store.SaveSetting("focus.media", JsonSerializer.Serialize(new MediaPreferences { CustomImages = new() { new("local:" + missing, "遗失的素材", missing, Custom: true) } }, Json));
            var service = new LocalBackupService(store.DatabasePath); var file = Path.Combine(root, "backup.fbbak"); var result = await service.CreateAsync(file); Assert.Equal(missing, Assert.Single(result.Manifest.Missing)); Assert.Empty(result.Manifest.Assets); Assert.Equal(result.Manifest.CreatedAt, LocalBackupService.Inspect(file).CreatedAt);
            var malformed = Path.Combine(root, "damaged.fbbak"); File.Copy(file, malformed); using (var zip = ZipFile.Open(malformed, ZipArchiveMode.Update)) { zip.GetEntry("database.sqlite")!.Delete(); using var writer = new StreamWriter(zip.CreateEntry("database.sqlite").Open()); writer.Write("损坏的数据库"); }
            await Assert.ThrowsAnyAsync<Exception>(() => service.PrepareRestoreAsync(malformed)); Assert.Single(store.GetProjects()); Assert.Equal(missing, JsonSerializer.Deserialize<MediaPreferences>(store.GetSetting("focus.media")!, Json)!.CustomImages[0].File); Assert.Empty(Directory.GetDirectories(Path.Combine(root, "backup-work")));
            var canceled = Path.Combine(root, "canceled.fbbak"); using var token = new CancellationTokenSource(); token.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CreateAsync(canceled, cancellationToken: token.Token)); Assert.False(File.Exists(canceled));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public void DailyScheduleAndRetentionPreserveManualAndRecoveryBackups()
    {
        var root = Path.Combine(Path.GetTempPath(), "FocusBloomBackupRetention", Guid.NewGuid().ToString()); Directory.CreateDirectory(root);
        try
        {
            var now = DateTimeOffset.Now; Assert.False(LocalBackupService.AutomaticDue(new(), now)); Assert.True(LocalBackupService.AutomaticDue(new(true), now)); Assert.False(LocalBackupService.AutomaticDue(new(true, LastAutomatic: now.AddMinutes(-1)), now)); Assert.True(LocalBackupService.AutomaticDue(new(true, LastAutomatic: now.AddDays(-1)), now));
            for (var index = 0; index < 4; index++) { var file = Path.Combine(root, $"FocusBloom-auto-{index}.fbbak"); File.WriteAllText(file, "自动备份"); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(index - 4)); }
            File.WriteAllText(Path.Combine(root, "FocusBloom-manual.fbbak"), "手动"); File.WriteAllText(Path.Combine(root, "FocusBloom-before-restore.fbbak"), "恢复前"); LocalBackupService.PruneAutomatic(root, 2);
            Assert.Equal(4, Directory.GetFiles(root).Length); Assert.True(File.Exists(Path.Combine(root, "FocusBloom-auto-3.fbbak"))); Assert.True(File.Exists(Path.Combine(root, "FocusBloom-auto-2.fbbak"))); Assert.True(File.Exists(Path.Combine(root, "FocusBloom-manual.fbbak"))); Assert.True(File.Exists(Path.Combine(root, "FocusBloom-before-restore.fbbak")));
        }
        finally { Directory.Delete(root, true); }
    }
}
