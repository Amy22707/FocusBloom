using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using FocusBloom.Infrastructure.Backup;
using FocusBloom.Infrastructure.Storage;

namespace FocusBloom.Mac;

internal sealed partial class MainWindow
{
    private sealed record Asset(string Id, string Name, string File);
    private sealed record Quote(string Id, string Text, string Author = "", bool Custom = false);
    private sealed record Catalog(Asset[] Images, Asset[] Sounds);
    private static readonly string ResourceFolder = Path.Combine(AppContext.BaseDirectory, "Resources", "Focus");
    private static readonly Lazy<Catalog> BuiltInMedia = new(() => JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(ResourceFolder, "catalog.json")), Json)!);
    private static readonly Lazy<Quote[]> BuiltInQuotes = new(() => JsonSerializer.Deserialize<Quote[]>(File.ReadAllText(Path.Combine(ResourceFolder, "quotes.json")), Json)!);
    private static readonly string[] ImagePatterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tiff"];
    private bool _backupBusy;
    private DateTimeOffset _lastBackupCheck;
    private Process? _sound;
    private readonly List<Bitmap> _bitmaps = [];
    private JsonObject Media() => _store.GetSetting("focus.media") is { } saved ? JsonNode.Parse(saved)!.AsObject() : new();
    private void SaveMedia(JsonObject value) => _store.SaveSetting("focus.media", value.ToJsonString());
    private static JsonArray List(JsonObject obj, string key)
    {
        if (obj[key] is JsonArray array) return array;
        var created = new JsonArray(); obj[key] = created; return created;
    }
    private static string? Value(JsonObject node, string key) => node[key]?.GetValue<string>();
    private static readonly Lazy<Dictionary<string, string>> EnglishLabels = new(() =>
    {
        using var stream = typeof(MainWindow).Assembly.GetManifestResourceStream("FocusBloom.English.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    });
    private string BuiltInName(string name) => _english ? EnglishLabels.Value.GetValueOrDefault(name, name) : name;
    private IEnumerable<Asset> Assets(JsonObject media, string key, IEnumerable<Asset> defaults) => defaults
        .Select(item => item with { File = Path.Combine(ResourceFolder, item.File), Name = BuiltInName(item.Name) })
        .Concat(media[key]?.Deserialize<Asset[]>(Json) ?? []);
    private static IEnumerable<Quote> Quotes(JsonObject media)
    {
        var edited = (media["editedDefaultQuotes"]?.Deserialize<Quote[]>(Json) ?? []).ToDictionary(item => item.Id);
        var removed = (media["removedQuoteIds"]?.Deserialize<string[]>(Json) ?? []).ToHashSet();
        return BuiltInQuotes.Value.Select(item => edited.GetValueOrDefault(item.Id, item))
            .Concat(media["customQuotes"]?.Deserialize<Quote[]>(Json) ?? []).DistinctBy(q => q.Id).Where(q => !removed.Contains(q.Id));
    }
    private string SelectedQuote()
    {
        var media = Media();
        var quote = Quotes(media).FirstOrDefault(q => q.Id == Value(media, "quoteId"));
        return quote?.Text ?? T("执着于理想，纯粹于当下。", "Stay true to your ideals and present in the moment.");
    }
    private Bitmap? SelectedBackground()
    {
        var media = Media(); var id = Value(media, "backgroundId") ?? "lake-blue-hour";
        var image = Assets(media, "customImages", BuiltInMedia.Value.Images).FirstOrDefault(asset => asset.Id == id);
        return image is null ? null : ReadBitmap(image.File);
    }
    private Bitmap? ReadBitmap(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var stream = File.OpenRead(path);
            var image = Bitmap.DecodeToWidth(stream, 1280);
            _bitmaps.Add(image);
            return image;
        }
        catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException) { return null; }
    }
    private string CopyMedia(string path, string kind)
    {
        var directory = Path.Combine(_data, kind); Directory.CreateDirectory(directory);
        var local = Path.Combine(directory, Guid.NewGuid().ToString("N")[..8] + "-" + Path.GetFileName(path));
        File.Copy(path, local); return local;
    }
    private void UpdateSound()
    {
        if (_sound is not null)
        {
            if (!_sound.HasExited) _sound.Kill();
            _sound.Dispose(); _sound = null;
        }
        if (!OperatingSystem.IsMacOS() || _timer.State != FocusBloom.Core.Focus.FocusState.Focusing) return;
        var media = Media(); var selected = Value(media, "soundId");
        if (Assets(media, "customSounds", BuiltInMedia.Value.Sounds).FirstOrDefault(item => item.Id == selected) is not { } sound || !File.Exists(sound.File)) return;
        var info = new ProcessStartInfo("/usr/bin/afplay") { UseShellExecute = false }; info.ArgumentList.Add(sound.File);
        _sound = Process.Start(info);
    }
    private LocalBackupService Backups => new(_store.DatabasePath);
    private BackupOptions BackupPreferences => _store.GetSetting(LocalBackupService.OptionsKey) is { } json ? JsonSerializer.Deserialize<BackupOptions>(json, Json) ?? new() : new();
    private void SaveBackupPreferences(BackupOptions options) => _store.SaveSetting(LocalBackupService.OptionsKey, JsonSerializer.Serialize(options));
    private string BackupFolder(BackupOptions options) => string.IsNullOrEmpty(options.Directory) ? Backups.DefaultDirectory : options.Directory;

    private void SettingsPage()
    {
        var language = Choice(["简体中文", "English"], _english ? 1 : 0);
        language.SelectionChanged += (_, _) => { _english = language.SelectedIndex == 1; _store.SaveSetting("appearance.language", _english ? "en" : "zh-CN"); Refresh(); };
        string[] themes = ["System", "Light", "Dark"];
        var theme = Choice([T("跟随系统", "System"), T("浅色", "Light"), T("深色", "Dark")], Math.Max(0, Array.IndexOf(themes, _store.GetSetting("appearance.mode"))));
        theme.SelectionChanged += (_, _) => { _store.SaveSetting("appearance.mode", themes[theme.SelectedIndex]); ApplyAppearance(); };
        var colors = Choice([T("苔绿", "Moss"), T("水色", "Water"), T("淡紫", "Lavender"), T("陶土", "Clay"), T("麦芽", "Wheat")], Math.Max(0, Array.IndexOf(_colors, _store.GetSetting("mac.accent"))));
        colors.SelectionChanged += (_, _) => { _store.SaveSetting("mac.accent", _colors[colors.SelectedIndex]); ApplyAppearance(); };
        _page.Children.Add(Card(Label(T("外观", "Appearance"), 17, true), Row(Field("语言/Language", language), Field(T("明暗", "Theme"), theme), Field(T("配色", "Color"), colors))));
        _page.Children.Add(MediaSettings());
        var prefs = BackupPreferences;
        var automatic = new CheckBox { Content = T("每天自动备份", "Back up every day"), IsChecked = prefs.Automatic };
        automatic.IsCheckedChanged += async (_, _) => { SaveBackupPreferences(BackupPreferences with { Automatic = automatic.IsChecked == true }); await RunAutomaticBackup(); };
        var backupDirectory = Label(BackupFolder(prefs));
        var screenshotDirectory = Label(_store.GetSetting("mac.screenshotDirectory") ?? Path.Combine(_data, "screenshots"));
        _page.Children.Add(Card(Label(T("数据备份与恢复", "Backup & Restore"), 17, true),
            Label(T("备份包含自定义背景、声音、句子及照片，也可恢复 Windows 版创建的 .fbbak。", "Backups include your backgrounds, sounds, quotes, and photos. You can restore a .fbbak created on Windows.")),
            automatic, backupDirectory, Row(AsyncButton(T("创建备份", "Create backup"), CreateBackup, true),
                AsyncButton(T("从备份恢复", "Restore backup"), RestoreBackup), AsyncButton(T("选择备份目录", "Choose backup folder"), async () =>
                {
                    if (await ChooseFolder(T("选择备份目录", "Choose backup folder")) is not { } path) return;
                    SaveBackupPreferences(BackupPreferences with { Directory = path }); backupDirectory.Text = path;
                }))));
        _page.Children.Add(Card(Label(T("截图", "Screenshots"), 17, true), screenshotDirectory,
            AsyncButton(T("选择截图目录", "Choose screenshot folder"), async () =>
            {
                if (await ChooseFolder(T("选择截图目录", "Choose screenshot folder")) is not { } path) return;
                _store.SaveSetting("mac.screenshotDirectory", path); screenshotDirectory.Text = path;
            })));
        _page.Children.Add(Card(Label(T("关于", "About"), 17, true), Label(T("一刻专注 · FocusBloom 是离线专注与计划工具。", "FocusBloom is an offline focus and planning app.")),
            Button(T("项目仓库", "Project repository"), () => Process.Start(new ProcessStartInfo("https://github.com/Amy22707/FocusBloom") { UseShellExecute = true }))));
    }

    private Control MediaSettings()
    {
        var media = Media();
        var removedImages = (media["removedImageIds"]?.Deserialize<string[]>(Json) ?? []).ToHashSet();
        var removedSounds = (media["removedSoundIds"]?.Deserialize<string[]>(Json) ?? []).ToHashSet();
        var images = new[] { new Asset("theme", T("主题纯色", "Theme color"), "") }.Concat(Assets(media, "customImages", BuiltInMedia.Value.Images).Where(a => !removedImages.Contains(a.Id))).ToArray();
        var sounds = new[] { new Asset("", T("不播放", "No sound"), "") }.Concat(Assets(media, "customSounds", BuiltInMedia.Value.Sounds).Where(a => !removedSounds.Contains(a.Id))).ToArray();
        var quotes = new[] { new Quote("", T("默认句子", "Default quote")) }.Concat(Quotes(media)).ToArray();
        var background = Choice(images.Select(a => a.Name), Math.Max(0, Array.FindIndex(images, a => a.Id == (Value(media, "backgroundId") ?? "lake-blue-hour"))));
        var sound = Choice(sounds.Select(a => a.Name), Math.Max(0, Array.FindIndex(sounds, a => a.Id == Value(media, "soundId"))));
        var quote = Choice(quotes.Select(q => q.Text), Math.Max(0, Array.FindIndex(quotes, q => q.Id == Value(media, "quoteId"))));
        quote.MaxWidth = 330;
        background.SelectionChanged += (_, _) => { media["backgroundId"] = images[background.SelectedIndex].Id; SaveMedia(media); };
        sound.SelectionChanged += (_, _) => { media["soundId"] = sounds[sound.SelectedIndex].Id; SaveMedia(media); };
        quote.SelectionChanged += (_, _) => { media["quoteId"] = quotes[quote.SelectedIndex].Id; SaveMedia(media); };
        return Card(Label(T("专注素材", "Focus media"), 17, true), Field(T("背景", "Background"), background), Field(T("声音", "Sound"), sound), Field(T("句子", "Quote"), quote),
            Row(AsyncButton(T("添加背景", "Add background"), () => AddMedia("customImages", "backgroundId", ImagePatterns)),
                AsyncButton(T("添加声音", "Add sound"), () => AddMedia("customSounds", "soundId", ["*.mp3", "*.wav", "*.m4a", "*.aiff"])),
                AsyncButton(T("添加句子", "Add quote"), async () =>
                {
                    var text = Input("", true);
                    if (await Form(T("添加句子", "Add quote"), [Field(T("句子", "Quote"), text)], () =>
                    {
                        var item = new Quote(Guid.NewGuid().ToString("N"), Required(text), Custom: true);
                        List(media, "customQuotes").Add(JsonSerializer.SerializeToNode(item, Json)); media["quoteId"] = item.Id; SaveMedia(media);
                    })) Refresh();
                })));
    }

    private async Task AddMedia(string key, string selection, string[] patterns)
    {
        if (await OpenFile(T("选择素材", "Choose media"), patterns) is not { } file) return;
        var media = Media(); var local = CopyMedia(file, "focus-media");
        var asset = new Asset("local:" + local, Path.GetFileNameWithoutExtension(file), local);
        var value = JsonSerializer.SerializeToNode(asset, Json)!.AsObject(); value["custom"] = true;
        List(media, key).Add(value); media[selection] = asset.Id; SaveMedia(media); Refresh();
    }

    private async Task<string?> ChooseFolder(string title)
    {
        var folder = (await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title })).FirstOrDefault();
        return folder?.TryGetLocalPath();
    }
    private async Task CreateBackup()
    {
        if (_backupBusy) return;
        if (await SaveFile(T("保存备份", "Save backup"), Path.GetFileNameWithoutExtension(LocalBackupService.NewFileName()), "fbbak") is not { } file) return;
        _backupBusy = true;
        try
        {
            var backup = await Backups.CreateAsync(file);
            Report(backup.Manifest.Missing.Count == 0 ? file : T($"已保存。{backup.Manifest.Missing.Count} 个原始素材不可读取。", $"Saved. {backup.Manifest.Missing.Count} source assets could not be read."));
        }
        finally { _backupBusy = false; }
    }
    private async Task RunAutomaticBackup()
    {
        if (_backupBusy || !LocalBackupService.AutomaticDue(BackupPreferences, DateTimeOffset.Now)) return;
        _backupBusy = true;
        try
        {
            var options = BackupPreferences; var directory = BackupFolder(options);
            var result = await Backups.CreateAsync(Path.Combine(directory, LocalBackupService.NewFileName("FocusBloom-auto")));
            SaveBackupPreferences(BackupPreferences with { LastAutomatic = result.Manifest.CreatedAt, LastFile = result.File, LastBackup = result.Manifest.CreatedAt });
            LocalBackupService.PruneAutomatic(directory, options.Keep);
        }
        catch (Exception error) { Report(error.Message); }
        finally { _backupBusy = false; }
    }
    private async Task RestoreBackup()
    {
        if (Busy || _backupBusy) { Report(T("请先结束计时或等待备份完成。", "Finish the timer or wait for the backup to complete.")); return; }
        if (await OpenFile(T("选择备份文件", "Select backup"), "*.fbbak") is not { } file) return;
        var info = LocalBackupService.Inspect(file);
        var message = T($"恢复 {info.CreatedAt.LocalDateTime:g} 的备份？会先保存当前资料的备份。", $"Restore the backup from {info.CreatedAt.LocalDateTime:g}? Your current data will be backed up first.");
        if (!await Form(T("确认恢复", "Confirm restore"), [Label(message)], async () =>
        {
            _backupBusy = true;
            try
            {
                var service = Backups; using var prepared = await service.PrepareRestoreAsync(file);
                await service.CreateAsync(Path.Combine(BackupFolder(BackupPreferences), LocalBackupService.NewFileName("FocusBloom-before-restore")));
                service.ApplyRestore(prepared);
                _ = new SqliteFocusStore(_store.DatabasePath);
            }
            finally { _backupBusy = false; }
        }, T("恢复", "Restore"))) return;
        LoadCheckpoint(); _english = _store.GetSetting("appearance.language") == "en"; ApplyAppearance(); Refresh();
        Report(T("备份已恢复。", "Backup restored."));
    }
}
