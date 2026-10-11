using System;
using System.IO;

namespace KeyboardWorker;

public static class PresetDialogueTests
{
    public static int Run(string root)
    {
        int count = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); count++; }
        string folder = Path.Combine(root, "preset-dialogues"); var store = new PresetStore(folder);
        var original = new Preferences { Lines = "A 재촉", PatLines = "A 쓰다듬기", ImagePaths = new() { ["idle"] = "a.png" } };
        var a = store.Add("A", original, new Persona { Name = "A 캐릭터" });
        var b = store.Add("B", new Preferences { Lines = "B 재촉", PatLines = "B 쓰다듬기" }, new Persona { Name = "B 캐릭터" });
        var current = a.Prepare(original, new ChatPreferences()).Settings;
        current.Lines = "A 새 재촉\nA 두 번째"; current.PatLines = "A 새 쓰다듬기";
        current.ImagePaths["idle"] = "draft.png";
        int notifications = 0; store.Changed += () => notifications++;
        store.SaveDialoguesAsync(current).GetAwaiter().GetResult();
        var savedA = store.Items.Find(p => p.Id == a.Id)!;
        Check(savedA.NudgeLines == current.Lines && savedA.PatLines == current.PatLines, "Dialogue editor persists both lines to active preset");
        Check(savedA.Images["idle"] == "a.png" && savedA.Profile.Name == "A 캐릭터", "Dialogue edit preserves other preset properties");
        Check(b.NudgeLines == "B 재촉" && b.PatLines == "B 쓰다듬기", "Other character dialogue remains independent");
        Check(notifications == 1, "Open preset view notified of dialogue changes");
        var bSettings = b.Prepare(current, new ChatPreferences()).Settings;
        Check(bSettings.Lines == "B 재촉" && bSettings.PatLines == "B 쓰다듬기" && bSettings.ActivePresetId == b.Id, "Switch character restores both dialogue categories");
        var reloaded = new PresetStore(folder);
        var aAgain = reloaded.Items.Find(p => p.Id == a.Id)!.Prepare(bSettings, new ChatPreferences()).Settings;
        Check(aAgain.Lines == current.Lines && aAgain.PatLines == current.PatLines, "Edited preset dialogue survives reload and switch back");
        aAgain.Lines = ""; aAgain.PatLines = ""; reloaded.SaveDialoguesAsync(aAgain).GetAwaiter().GetResult();
        var empty = new PresetStore(folder).Items.Find(p => p.Id == a.Id)!.Prepare(bSettings, new ChatPreferences()).Settings;
        Check(empty.Lines == "" && empty.PatLines == "", "Intentionally empty preset dialogue stays empty");
        current.ActivePresetId = "deleted-preset"; store.SaveDialoguesAsync(current).GetAwaiter().GetResult();
        Check(notifications == 1 && store.Items.Count == 2, "Missing active preset never overwrites another character");
        var legacy = new CharacterPreset { PatLines = null, NudgeLines = "old" };
        Check(legacy.Prepare(current, new ChatPreferences()).Settings.PatLines == new Preferences().PatLines, "Legacy preset without pat lines uses defaults without leaking another character's dialogue");
        return count;
    }
}
