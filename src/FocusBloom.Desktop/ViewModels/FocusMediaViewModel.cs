using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FocusBloom.Core.Storage;
using FocusBloom.Desktop.Presentation;

namespace FocusBloom.Desktop.ViewModels;

public sealed record FocusAsset(string Id, string Name, string File, string Author = "", string Source = "", string License = "", bool Custom = false);
public sealed record FocusQuote(string Id, string Text, string Author = "", string Work = "", string Translator = "", string Source = "", bool Custom = false)
{
    public string Name => Text.Length > 28 ? Text[..28] + "…" : Text;
    public string Attribution => string.Join(" · ", new[] { Author, Work, Translator.Length > 0 ? $"译者：{Translator}" : "" }.Where(part => part.Length > 0));
}

public sealed class FocusImage(FocusAsset asset, Action changed) : ObservableObject
{
    private bool _favorite;
    private bool _excluded;
    private ImageSource? _thumbnail;
    private bool _selected;
    public FocusAsset Asset { get; } = asset;
    public string Name => Asset.Name;
    public string Credit => $"{Asset.Author} · {Asset.License}";
    public ImageSource? Thumbnail => _thumbnail ??= FocusMediaViewModel.LoadImage(Asset.File, 320);
    public bool Favorite { get => _favorite; set { if (_favorite == value) return; _favorite = value; Notify(); changed(); } }
    public bool Excluded { get => _excluded; set { if (_excluded == value) return; _excluded = value; Notify(); changed(); } }
    public bool Selected { get => _selected; internal set { _selected = value; Notify(); } }
}

public sealed class FocusQuoteCard(FocusQuote quote, bool favorite, Action changed) : ObservableObject
{
    private bool _favorite = favorite;
    private bool _selected;
    public FocusQuote Quote { get; } = quote;
    public string Text => Quote.Text;
    public string Credit => Quote.Attribution;
    public bool Custom => Quote.Custom;
    public bool Favorite { get => _favorite; set { if (_favorite == value) return; _favorite = value; Notify(); changed(); } }
    public bool Selected { get => _selected; internal set { _selected = value; Notify(); } }
}

public sealed class FocusSoundCard(FocusAsset asset, bool favorite, Action changed) : ObservableObject
{
    private bool _favorite = favorite;
    private bool _selected;
    public FocusAsset Asset { get; } = asset;
    public string Name => Asset.Name;
    public string Credit => $"{Asset.Author} · {Asset.License}";
    public bool Favorite { get => _favorite; set { if (_favorite == value) return; _favorite = value; Notify(); changed(); } }
    public bool Selected { get => _selected; internal set { _selected = value; Notify(); } }
}

public sealed class MediaPreferences
{
    public string BackgroundId { get; set; } = "";
    public string QuoteId { get; set; } = "";
    public string SoundId { get; set; } = "";
    public double Volume { get; set; } = 35;
    public bool Muted { get; set; }
    public bool FavoritesOnly { get; set; }
    public bool ReminderSounds { get; set; } = true;
    public bool QuoteFavoritesOnly { get; set; }
    public List<string> FavoriteQuoteIds { get; set; } = [];
    public List<string> FavoriteSoundIds { get; set; } = [];
    public List<string> RemovedImageIds { get; set; } = [];
    public List<string> RemovedQuoteIds { get; set; } = [];
    public List<string> RemovedSoundIds { get; set; } = [];
    public List<string> FavoriteIds { get; set; } = [];
    public List<string> ExcludedIds { get; set; } = [];
    public List<string> ImageFolders { get; set; } = [];
    public List<FocusAsset> CustomImages { get; set; } = [];
    public List<FocusAsset> CustomSounds { get; set; } = [];
    public List<FocusQuote> CustomQuotes { get; set; } = [];
}

public sealed class FocusMediaViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IFocusStore _store;
    private readonly Action<string> _report;
    private readonly MediaPreferences _preferences;
    private readonly FocusAudio _audio = new();
    private bool _loading = true;
    private bool _sessionFixed;
    private FocusAsset _selectedBackground;
    private FocusQuote _selectedQuote;
    private FocusAsset _selectedSound;
    private FocusAsset? _currentImage;
    private FocusQuote? _currentQuote;
    private ImageSource? _background;
    private string? _loadedSound;
    private string? _editingQuote;
    private string _quoteText = "";
    private string _quoteAuthor = "";
    private string _quoteWork = "";
    private string _quoteTranslator = "";
    private string _quoteSource = "";
    private MediaPlayer? _cue;
    private readonly FocusAsset[] _defaultImages;
    private readonly FocusQuote[] _defaultQuotes;
    private readonly FocusAsset[] _defaultSounds;
    private bool _showFavoriteImages;
    private bool _showFavoriteQuotes;
    private bool _showFavoriteSounds;

    private sealed record Catalog(FocusAsset[] Images, FocusAsset[] Sounds);

    public FocusMediaViewModel(IFocusStore store, Action<string> report)
    {
        _store = store; _report = report;
        var dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        _audio.PlaybackStopped += (_, args) => dispatcher.BeginInvoke(new Action(() =>
        {
            if (args.Exception is not null) _report($"声音播放已停止：{args.Exception.Message}");
            Notify(nameof(IsPlaying)); Notify(nameof(AudioButtonText));
        }));
        var saved = store.GetSetting("focus.media");
        _preferences = saved is null ? new MediaPreferences() : JsonSerializer.Deserialize<MediaPreferences>(saved, Json)!;
        var root = Path.Combine(AppContext.BaseDirectory, "Resources", "Focus");
        var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(root, "catalog.json")), Json)!;
        BackgroundChoices.Add(new FocusAsset("", "每次专注随机", ""));
        BackgroundChoices.Add(new FocusAsset("theme", "纯色主题背景", ""));
        _defaultImages = catalog.Images.Select(asset => asset with { File = Path.Combine(root, asset.File) }).ToArray();
        foreach (var asset in _defaultImages.Concat(_preferences.CustomImages).Where(asset => !_preferences.RemovedImageIds.Contains(asset.Id))) AddImage(asset);
        QuoteChoices.Add(new FocusQuote("", "每次专注随机"));
        var phrases = JsonSerializer.Deserialize<string[]>(File.ReadAllText(Path.Combine(root, "quotes.json")), Json)!;
        _defaultQuotes = phrases.Select((text, index) => new FocusQuote($"default-{index}", text)).ToArray();
        foreach (var quote in _defaultQuotes.Concat(_preferences.CustomQuotes).Where(quote => !_preferences.RemovedQuoteIds.Contains(quote.Id))) AddQuote(quote);
        SoundChoices.Add(new FocusAsset("", "不播放", ""));
        _defaultSounds = catalog.Sounds.Select(asset => asset with { File = Path.Combine(root, asset.File) }).ToArray();
        foreach (var asset in _defaultSounds.Concat(_preferences.CustomSounds).Where(asset => !_preferences.RemovedSoundIds.Contains(asset.Id))) AddSound(asset);
        RefreshFolders();
        _selectedBackground = BackgroundChoices.FirstOrDefault(asset => asset.Id == _preferences.BackgroundId) ?? BackgroundChoices[0];
        _selectedQuote = QuoteChoices.FirstOrDefault(quote => quote.Id == _preferences.QuoteId) ?? QuoteChoices[0];
        _selectedSound = SoundChoices.FirstOrDefault(asset => asset.Id == _preferences.SoundId) ?? SoundChoices[0];
        _loading = false;
        UpdateSelections();
        Preview();
        ToggleAudioCommand = new RelayCommand(ToggleAudio);
        SaveQuoteCommand = new RelayCommand(SaveQuote);
        CancelQuoteCommand = new RelayCommand(CancelQuoteEdit);
        OpenImageSourceCommand = new RelayCommand(() => OpenSource(_currentImage?.Source));
        OpenSoundSourceCommand = new RelayCommand(() => OpenSource(SelectedSound.Source));
    }

    public ObservableCollection<FocusImage> Images { get; } = [];
    public ObservableCollection<FocusAsset> BackgroundChoices { get; } = [];
    public ObservableCollection<FocusQuote> QuoteChoices { get; } = [];
    public ObservableCollection<FocusAsset> SoundChoices { get; } = [];
    public ObservableCollection<FocusQuoteCard> Quotes { get; } = [];
    public ObservableCollection<FocusSoundCard> Sounds { get; } = [];
    public IEnumerable<FocusImage> VisibleImages => Images.Where(image => !ShowFavoriteImages || image.Favorite);
    public IEnumerable<FocusQuoteCard> VisibleQuotes => Quotes.Where(quote => !ShowFavoriteQuotes || quote.Favorite);
    public IEnumerable<FocusSoundCard> VisibleSounds => Sounds.Where(sound => !ShowFavoriteSounds || sound.Favorite);
    public IEnumerable<FocusAsset> QuickSounds => SoundChoices.OrderBy(sound => sound.Id.Length > 0).ThenByDescending(sound => Sounds.Any(card => card.Asset.Id == sound.Id && card.Favorite));
    public IEnumerable<FocusQuote> CustomQuotes => QuoteChoices.Where(quote => quote.Custom);
    public RelayCommand ToggleAudioCommand { get; }
    public RelayCommand SaveQuoteCommand { get; }
    public RelayCommand CancelQuoteCommand { get; }
    public RelayCommand OpenImageSourceCommand { get; }
    public RelayCommand OpenSoundSourceCommand { get; }
    public ImageSource? Background => _background;
    public bool HasBackground => _background is not null;
    public string CurrentBackgroundId => _currentImage?.Id ?? "theme";
    public string CurrentQuoteId => _currentQuote?.Id ?? "";
    public string QuoteText => _currentQuote?.Text ?? "";
    public string QuoteCredit => _currentQuote?.Attribution ?? "";
    public string QuoteSource => _currentQuote?.Source ?? "";
    public string ImageCredit => _currentImage is null ? "纯色主题背景" : $"{_currentImage.Name} · {_currentImage.Author}";
    public string ImageSourceInfo => _currentImage is null ? "跟随当前配色" : $"{_currentImage.Author} · {_currentImage.License}\n{_currentImage.Source}";
    public string SoundCredit => SelectedSound.Id.Length == 0 ? "选一段声音，陪你专注。" : $"{SelectedSound.Author} · {SelectedSound.License}";
    public string AudioButtonText => IsPlaying ? "暂停声音" : "播放声音";
    public bool IsPlaying => _audio.IsPlaying;
    public bool BackgroundRandom => SelectedBackground.Id.Length == 0;
    public bool BackgroundPlain => SelectedBackground.Id == "theme";
    public bool QuoteRandom => SelectedQuote.Id.Length == 0;
    public bool NoSound => SelectedSound.Id.Length == 0;
    public FocusAsset SelectedBackground { get => _selectedBackground; set { if (value is null || value == _selectedBackground) return; _selectedBackground = value; _preferences.BackgroundId = value.Id; Notify(); UpdateSelections(); Save(); Preview(); } }
    public FocusQuote SelectedQuote { get => _selectedQuote; set { if (value is null || value == _selectedQuote) return; _selectedQuote = value; _preferences.QuoteId = value.Id; Notify(); UpdateSelections(); Save(); Preview(); } }
    public FocusAsset SelectedSound
    {
        get => _selectedSound;
        set
        {
            if (value is null || value == _selectedSound) return;
            var playing = IsPlaying;
            _audio.Dispose(); _loadedSound = null;
            _selectedSound = value; _preferences.SoundId = value.Id;
            Notify(); Notify(nameof(SoundCredit)); UpdateSelections(); Save();
            if (playing && value.File.Length > 0) ToggleAudio();
            Notify(nameof(IsPlaying)); Notify(nameof(AudioButtonText));
        }
    }
    public bool FavoritesOnly { get => _preferences.FavoritesOnly; set { _preferences.FavoritesOnly = value; Notify(); Save(); Preview(); } }
    public bool QuoteFavoritesOnly { get => _preferences.QuoteFavoritesOnly; set { _preferences.QuoteFavoritesOnly = value; Notify(); Save(); Preview(); } }
    public bool ShowFavoriteImages { get => _showFavoriteImages; set { _showFavoriteImages = value; Notify(); Notify(nameof(VisibleImages)); } }
    public bool ShowFavoriteQuotes { get => _showFavoriteQuotes; set { _showFavoriteQuotes = value; Notify(); Notify(nameof(VisibleQuotes)); } }
    public bool ShowFavoriteSounds { get => _showFavoriteSounds; set { _showFavoriteSounds = value; Notify(); Notify(nameof(VisibleSounds)); } }
    public bool ReminderSounds { get => _preferences.ReminderSounds; set { _preferences.ReminderSounds = value; Notify(); Save(); } }
    public double Volume { get => _preferences.Volume; set { if (_preferences.Volume == value) return; _preferences.Volume = Math.Clamp(value, 0, 100); _audio.SetVolume(EffectiveVolume); Notify(); Save(); } }
    public bool Muted { get => _preferences.Muted; set { _preferences.Muted = value; _audio.SetVolume(EffectiveVolume); Notify(); Save(); } }
    private float EffectiveVolume => Muted ? 0 : (float)Volume / 100;
    public string NewQuoteText { get => _quoteText; set { _quoteText = value; Notify(); } }
    public string NewQuoteAuthor { get => _quoteAuthor; set { _quoteAuthor = value; Notify(); } }
    public string NewQuoteWork { get => _quoteWork; set { _quoteWork = value; Notify(); } }
    public string NewQuoteTranslator { get => _quoteTranslator; set { _quoteTranslator = value; Notify(); } }
    public string NewQuoteSource { get => _quoteSource; set { _quoteSource = value; Notify(); } }

    public void BeginSession()
    {
        _sessionFixed = true;
        RefreshFolders();
        var available = AvailableImages().ToArray();
        var image = SelectedBackground.Id == "theme" ? null : SelectedBackground.Id.Length > 0
            ? Images.FirstOrDefault(item => item.Asset.Id == SelectedBackground.Id && !item.Excluded && File.Exists(item.Asset.File))?.Asset
            : Pick(available, _currentImage?.Id, asset => asset.Id);
        var quote = SelectedQuote.Id.Length > 0 ? SelectedQuote : Pick(AvailableQuotes().ToArray(), _currentQuote?.Id, item => item.Id);
        SetScene(image, quote);
        _sessionFixed = true;
    }

    public void ReleaseSession() => _sessionFixed = false;
    private IEnumerable<FocusAsset> AvailableImages() => Images.Where(image => !image.Excluded && (!FavoritesOnly || image.Favorite) && File.Exists(image.Asset.File)).Select(image => image.Asset);
    private IEnumerable<FocusQuote> AvailableQuotes() => Quotes.Where(quote => !QuoteFavoritesOnly || quote.Favorite).Select(quote => quote.Quote);
    private static T? Pick<T>(T[] items, string? previous, Func<T, string> id) where T : class
    {
        var choices = items.Length > 1 ? items.Where(item => id(item) != previous).ToArray() : items;
        return choices.Length == 0 ? null : choices[Random.Shared.Next(choices.Length)];
    }
    private void Preview()
    {
        if (_loading || _sessionFixed) return;
        var image = SelectedBackground.Id == "theme" ? null : SelectedBackground.Id.Length > 0
            ? Images.FirstOrDefault(item => item.Asset.Id == SelectedBackground.Id && !item.Excluded)?.Asset : AvailableImages().FirstOrDefault();
        SetScene(image, SelectedQuote.Id.Length > 0 ? SelectedQuote : AvailableQuotes().FirstOrDefault());
    }
    private void SetScene(FocusAsset? image, FocusQuote? quote)
    {
        _currentImage = image; _currentQuote = quote;
        _background = image is null ? null : LoadImage(image.File, 1920);
        if (image is not null && _background is null) _report("背景文件不可用，当前使用主题背景。可在图文设置中重新选择。");
        foreach (var property in new[] { nameof(Background), nameof(HasBackground), nameof(QuoteText), nameof(QuoteCredit), nameof(QuoteSource), nameof(ImageCredit), nameof(ImageSourceInfo), nameof(CurrentBackgroundId), nameof(CurrentQuoteId) }) Notify(property);
    }

    public static ImageSource? LoadImage(string path, int width)
    {
        try
        {
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = width; bitmap.UriSource = new Uri(path, UriKind.Absolute); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
        }
        catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException) { return null; }
    }
    private void AddImage(FocusAsset asset)
    {
        if (Images.Any(image => image.Asset.File.Equals(asset.File, StringComparison.OrdinalIgnoreCase))) return;
        if (_preferences.RemovedImageIds.Contains(asset.Id)) return;
        var image = new FocusImage(asset, LibraryChanged);
        Images.Add(image); BackgroundChoices.Add(asset);
        image.Favorite = _preferences.FavoriteIds.Contains(asset.Id); image.Excluded = _preferences.ExcludedIds.Contains(asset.Id);
    }
    private void AddQuote(FocusQuote quote)
    {
        QuoteChoices.Add(quote);
        Quotes.Add(new FocusQuoteCard(quote, _preferences.FavoriteQuoteIds.Contains(quote.Id), LibraryChanged));
    }
    private void AddSound(FocusAsset asset)
    {
        SoundChoices.Add(asset);
        Sounds.Add(new FocusSoundCard(asset, _preferences.FavoriteSoundIds.Contains(asset.Id), LibraryChanged));
    }
    private void LibraryChanged()
    {
        if (_loading) return;
        UpdateSelections(); Save(); Preview();
        foreach (var name in new[] { nameof(VisibleImages), nameof(VisibleQuotes), nameof(VisibleSounds), nameof(QuickSounds) }) Notify(name);
    }
    private void UpdateSelections()
    {
        foreach (var image in Images) image.Selected = image.Asset.Id == SelectedBackground.Id;
        foreach (var quote in Quotes) quote.Selected = quote.Quote.Id == SelectedQuote.Id;
        foreach (var sound in Sounds) sound.Selected = sound.Asset.Id == SelectedSound.Id;
        foreach (var name in new[] { nameof(BackgroundRandom), nameof(BackgroundPlain), nameof(QuoteRandom), nameof(NoSound) }) Notify(name);
    }
    public void ImportImages(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths)
        {
            if (Images.Any(image => image.Asset.File.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            _preferences.RemovedImageIds.Remove("local:" + path);
            if (LoadImage(path, 320) is null) { _report($"无法读取图片：{Path.GetFileName(path)}"); continue; }
            AddImage(new FocusAsset("local:" + path, Path.GetFileNameWithoutExtension(path), path, "本地图片", "", "个人素材", true)); added++;
        }
        LibraryChanged();
        if (added > 0) _report($"已加入 {added} 张图片，新专注采用更新后的选择。");
    }
    public void ImportFolder(string folder)
    {
        if (!_preferences.ImageFolders.Contains(folder)) _preferences.ImageFolders.Add(folder);
        ImportImages(FolderImages(folder));
    }
    private static IEnumerable<string> FolderImages(string folder) => Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder).Where(path => new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant())) : [];
    private void RefreshFolders()
    {
        foreach (var folder in _preferences.ImageFolders.ToArray())
        {
            try { ImportImages(FolderImages(folder).Where(path => !_preferences.RemovedImageIds.Contains("local:" + path))); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { _report($"图片文件夹不可用：{folder}"); }
        }
    }
    public void RemoveImage(FocusImage image)
    {
        _preferences.RemovedImageIds.Add(image.Asset.Id);
        Images.Remove(image); BackgroundChoices.Remove(image.Asset);
        if (SelectedBackground.Id == image.Asset.Id) SelectedBackground = BackgroundChoices[0];
        LibraryChanged();
    }
    public void ImportSound(string path)
    {
        var asset = SoundChoices.FirstOrDefault(sound => sound.File.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (asset is null) { asset = new FocusAsset("local:" + path, Path.GetFileNameWithoutExtension(path), path, "本地音频", "", "个人素材", true); _preferences.RemovedSoundIds.Remove(asset.Id); AddSound(asset); }
        SelectedSound = asset; LibraryChanged();
    }
    public void RemoveSound(FocusSoundCard sound)
    {
        if (!sound.Asset.Custom) _preferences.RemovedSoundIds.Add(sound.Asset.Id);
        Sounds.Remove(sound); SoundChoices.Remove(sound.Asset);
        if (SelectedSound.Id == sound.Asset.Id) SelectedSound = SoundChoices[0];
        LibraryChanged();
    }
    public void RestoreImages()
    {
        foreach (var asset in _defaultImages) { _preferences.RemovedImageIds.Remove(asset.Id); AddImage(asset); }
        LibraryChanged();
    }
    public void RestoreQuotes()
    {
        foreach (var quote in _defaultQuotes) { _preferences.RemovedQuoteIds.Remove(quote.Id); if (!Quotes.Any(card => card.Quote.Id == quote.Id)) AddQuote(quote); }
        LibraryChanged();
    }
    public void RestoreSounds()
    {
        foreach (var asset in _defaultSounds) { _preferences.RemovedSoundIds.Remove(asset.Id); if (!Sounds.Any(card => card.Asset.Id == asset.Id)) AddSound(asset); }
        LibraryChanged();
    }
    private void ToggleAudio()
    {
        if (SelectedSound.File.Length == 0) { _report("请先选择一段声音。"); return; }
        try
        {
            if (IsPlaying) _audio.Pause();
            else
            {
                if (_loadedSound != SelectedSound.File) { _audio.Load(SelectedSound.File, EffectiveVolume); _loadedSound = SelectedSound.File; }
                _audio.Play();
            }
        }
        catch (Exception error) { _audio.Dispose(); _loadedSound = null; _report($"声音无法播放：{error.Message}"); }
        Notify(nameof(IsPlaying)); Notify(nameof(AudioButtonText));
    }
    public void EditQuote(FocusQuote quote)
    {
        if (!quote.Custom) return;
        _editingQuote = quote.Id; NewQuoteText = quote.Text; NewQuoteAuthor = quote.Author; NewQuoteWork = quote.Work; NewQuoteTranslator = quote.Translator; NewQuoteSource = quote.Source;
    }
    private void SaveQuote()
    {
        if (string.IsNullOrWhiteSpace(NewQuoteText)) { _report("请填写一句文字。"); return; }
        var quote = new FocusQuote(_editingQuote ?? Guid.NewGuid().ToString(), NewQuoteText.Trim(), NewQuoteAuthor.Trim(), NewQuoteWork.Trim(), NewQuoteTranslator.Trim(), NewQuoteSource.Trim(), true);
        var previous = QuoteChoices.FirstOrDefault(item => item.Id == quote.Id);
        if (previous is not null)
        {
            var card = Quotes.Single(item => item.Quote.Id == quote.Id);
            QuoteChoices[QuoteChoices.IndexOf(previous)] = quote;
            Quotes[Quotes.IndexOf(card)] = new FocusQuoteCard(quote, card.Favorite, LibraryChanged);
            if (SelectedQuote.Id == quote.Id) SelectedQuote = quote;
        }
        else AddQuote(quote);
        LibraryChanged(); CancelQuoteEdit(); Notify(nameof(CustomQuotes)); _report("文字已保存，下一次专注可采用。");
    }
    public void RemoveQuote(FocusQuote quote)
    {
        if (!quote.Custom) _preferences.RemovedQuoteIds.Add(quote.Id);
        QuoteChoices.Remove(quote); if (SelectedQuote.Id == quote.Id) SelectedQuote = QuoteChoices[0];
        var card = Quotes.FirstOrDefault(item => item.Quote.Id == quote.Id);
        if (card is not null) Quotes.Remove(card);
        if (_editingQuote == quote.Id) CancelQuoteEdit();
        LibraryChanged(); Notify(nameof(CustomQuotes));
    }
    public void ImportQuotes(string path)
    {
        foreach (var line in File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line))) AddQuote(new FocusQuote(Guid.NewGuid().ToString(), line.Trim(), Custom: true));
        LibraryChanged(); Notify(nameof(CustomQuotes)); _report("文字列表已导入，可在列表中补充出处。");
    }
    private void CancelQuoteEdit()
    {
        _editingQuote = null; NewQuoteText = ""; NewQuoteAuthor = ""; NewQuoteWork = ""; NewQuoteTranslator = ""; NewQuoteSource = "";
    }
    private void Save()
    {
        if (_loading) return;
        _preferences.CustomImages = Images.Where(image => image.Asset.Custom).Select(image => image.Asset).ToList();
        _preferences.CustomSounds = SoundChoices.Where(sound => sound.Custom).ToList();
        _preferences.CustomQuotes = CustomQuotes.ToList();
        _preferences.FavoriteIds = Images.Where(image => image.Favorite).Select(image => image.Asset.Id).ToList();
        _preferences.ExcludedIds = Images.Where(image => image.Excluded).Select(image => image.Asset.Id).ToList();
        _preferences.FavoriteQuoteIds = Quotes.Where(quote => quote.Favorite).Select(quote => quote.Quote.Id).ToList();
        _preferences.FavoriteSoundIds = Sounds.Where(sound => sound.Favorite).Select(sound => sound.Asset.Id).ToList();
        try { _store.SaveSetting("focus.media", JsonSerializer.Serialize(_preferences, Json)); }
        catch (Exception error) { _report($"素材偏好保存失败：{error.Message}"); }
    }
    private void OpenSource(string? address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http")) return;
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }
    public void OpenSoundSource(FocusAsset asset) => OpenSource(asset.Source);
    public void PlayReminder(bool restEnded)
    {
        if (!ReminderSounds || Muted) return;
        if (_cue is null) { _cue = new MediaPlayer(); _cue.MediaOpened += (_, _) => _cue.Play(); }
        _cue.Volume = EffectiveVolume;
        _cue.Open(new Uri(Path.Combine(AppContext.BaseDirectory, "Resources", "Focus", "Audio", restEnded ? "rest-end.wav" : "focus-end.wav")));
    }
    public void Dispose() { _audio.Dispose(); _cue?.Close(); }
}
