using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace KeyboardWorker;

public sealed class CharacterPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public Persona Profile { get; set; } = new();
    public Dictionary<string, string> Images { get; set; } = new();
    public Dictionary<string, ImageAdjustment> ImageAdjustments { get; set; } = new();
    public bool Advanced { get; set; }
    public bool Flip { get; set; }
    public string NudgeLines { get; set; } = "";
    public string? PatLines { get; set; }
    public MotionSettings? Motion { get; set; }
    public Dictionary<string, string>? LineBook { get; set; }
    public Dictionary<string, string>? VoicePaths { get; set; }
    public CharacterPreset Snapshot() => new() { Id = Id, Name = Name, Profile = Profile.Snapshot(), Images = new(Images), ImageAdjustments = new(ImageAdjustments ?? new()), Advanced = Advanced, Flip = Flip, NudgeLines = NudgeLines, PatLines = PatLines, Motion = Motion, LineBook = new(LineBook ?? new()), VoicePaths = new(VoicePaths ?? new()) };
    public (Preferences Settings, ChatPreferences Chat) Prepare(Preferences current, ChatPreferences chat)
    {
        var settings = current.Snapshot();
        settings.ImagePaths = new(Images);
        settings.SkinPath = "";
        settings.AdvancedImages = Advanced;
        settings.FlipImages = Flip;
        settings.Lines = NudgeLines;
        settings.ActivePresetId = Id;
        settings.PatLines = PatLines ?? new Preferences().PatLines;
        settings.ImageAdjustments = new(ImageAdjustments ?? new());
        settings.LineBook = new(LineBook ?? new());
        settings.VoicePaths = new(VoicePaths ?? new());
        Motion?.Apply(settings.Desktop);
        var nextChat = chat.Snapshot(); nextChat.Profile = Profile.Snapshot(); return (settings, nextChat);
    }
    public static CharacterPreset Capture(string name, Preferences settings, Persona profile) => new()
    {
        Name = name.Trim(),
        Profile = profile.Snapshot(),
        Images = new(settings.ImagePaths),
        ImageAdjustments = new(settings.ImageAdjustments),
        Advanced = settings.AdvancedImages,
        Flip = settings.FlipImages,
        NudgeLines = settings.Lines,
        PatLines = settings.PatLines,
        Motion = MotionSettings.Capture(settings.Desktop),
        LineBook = new(settings.LineBook),
        VoicePaths = new(settings.VoicePaths)
    };
}
public sealed class PresetStore
{
    readonly string path;
    public List<CharacterPreset> Items { get; }
    public event Action? Changed;
    public PresetStore(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "character-presets.json");
        Items = Storage.ReadJson<List<CharacterPreset>>(path, 32 * 1024 * 1024) ?? new();
        Items = Items.Where(p => p != null && p.Profile != null && p.Images != null && !string.IsNullOrWhiteSpace(p.Id)).Take(50).ToList();
        foreach (var item in Items) foreach (var key in item.Images.Where(p => SafeFiles.IsNetworkPath(p.Value)).Select(p => p.Key).ToArray()) item.Images.Remove(key);
        foreach (var item in Items) { item.LineBook = KeyboardWorker.LineBook.Normalize(item.LineBook); item.VoicePaths = VoiceSlots.Normalize(item.VoicePaths); }
    }
    // Lines and voice clips of a preset that is not the current pet (the second pet is edited this way).
    public Task SaveLinesAsync(string id, string nudge, string pat, Dictionary<string, string> book)
    {
        int index = Items.FindIndex(p => p.Id == id); if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot(); next.NudgeLines = nudge; next.PatLines = pat; next.LineBook = KeyboardWorker.LineBook.Normalize(book); Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public Task SaveVoicesAsync(string id, Dictionary<string, string> clips)
    {
        int index = Items.FindIndex(p => p.Id == id); if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot(); next.VoicePaths = VoiceSlots.Normalize(clips); Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    void Save() { OrderedStore.SaveJson(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); }
    public Task SaveDialoguesAsync(Preferences settings)
    {
        int index = Items.FindIndex(p => p.Id == settings.ActivePresetId);
        if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot(); next.NudgeLines = settings.Lines; next.PatLines = settings.PatLines; next.LineBook = new(settings.LineBook); next.VoicePaths = new(settings.VoicePaths);
        Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public Task SaveAppearanceAsync(Preferences settings)
    {
        int index = Items.FindIndex(p => p.Id == settings.ActivePresetId);
        if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot();
        next.Images = new(settings.ImagePaths);
        next.ImageAdjustments = new(settings.ImageAdjustments);
        next.Advanced = settings.AdvancedImages;
        next.Flip = settings.FlipImages;
        Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public Task RemapImagesAsync(IReadOnlyDictionary<string, string> merge)
    {
        for (int i = 0; i < Items.Count; i++) { var next = Items[i].Snapshot(); next.Images = next.Images.ToDictionary(p => p.Key, p => ImageDiet.Map(merge, p.Value)); Items[i] = next; }
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public Task SaveFlipAsync(string id, bool flip)
    {
        int index = Items.FindIndex(p => p.Id == id); if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot(); next.Flip = flip; Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public Task SavePromptsAsync(string id, PromptTemplates prompts)
    {
        int index = Items.FindIndex(p => p.Id == id); if (index < 0) return Task.CompletedTask;
        var next = Items[index].Snapshot(); next.Profile.Prompts = prompts; Items[index] = next;
        var saved = OrderedStore.SaveJsonAsync(path, Items.Select(p => p.Snapshot()).ToArray()); Changed?.Invoke(); return saved;
    }
    public CharacterPreset Add(string name, Preferences settings, Persona profile)
    {
        if (Items.Count >= 50) throw new InvalidOperationException("프리셋은 최대 50개까지 저장할 수 있습니다.");
        var item = CharacterPreset.Capture(name, settings, profile); Items.Add(item); Save(); return item;
    }
    public void Update(CharacterPreset selected, string name, Preferences settings, Persona profile)
    {
        var next = CharacterPreset.Capture(name, settings, profile); next.Id = selected.Id; Items[Items.IndexOf(selected)] = next; Save();
    }
    public void Delete(CharacterPreset selected) { Items.Remove(selected); Save(); }
    public CharacterPreset AddImported(CharacterPreset preset)
    {
        if (Items.Count >= 50) throw new InvalidOperationException("프리셋은 최대 50개까지 저장할 수 있습니다.");
        Items.Add(preset); Save(); return preset;
    }
}
