using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KeyboardWorker;

// Mod files: what is read from them, what is refused, and what the enabled ones add to the program.
public static class ModTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        string home = Path.Combine(root, "mods-home"), files = Path.Combine(root, "mods-files"); Directory.CreateDirectory(home); Directory.CreateDirectory(files);
        try
        {
            // Reading a mod file
            string sample = JsonSerializer.Serialize(ModFile.Sample(), ModFile.Writing); var parsed = ModFile.Parse(sample);
            Check(parsed.Id == "my-first-mod" && parsed.Themes!.Single().Id == "night" && parsed.LinePacks!.Single().Lines!.Count == 2 && parsed.Profiles!.Single().Values!.Count == 5 && parsed.PageModes!.Single().Length == "길게" && parsed.Summary == "테마 1 · 대사 팩 1 · 상황 프로필 1 · 페이지 모드 1", "The sample the program writes is a mod the program reads");
            Check(sample.Contains("\"format\": 1") && sample.Contains("\"linePacks\"") && sample.Contains("밤하늘팝") && !sample.Contains("\\u"), "A mod file is plain readable JSON, Korean included");
            var loose = ModFile.Parse("{ // 주석도 됩니다\n \"FORMAT\": 1, \"id\": \" My Mod!! \", \"themes\": [ { \"id\": \"Sea Blue\", \"accentHue\": -30, \"secondHue\": 9999, \"saturation\": 50 }, ], }");
            Check(loose.Id == "mymod" && loose.Name == "mymod" && loose.Themes!.Single() is { Id: "seablue", Name: "seablue", AccentHue: 330, Saturation: 1.3 } && loose.Themes![0].SecondHue is >= 0 and < 360, "Names are made safe, numbers are brought into range, and comments or trailing commas do not matter");
            var mixed = ModFile.Parse("""
                { "format": 1, "id": "mix", "name": "섞인 모드\n둘째 줄", "description": "설명",
                  "linePacks": [ { "id": "ok", "name": "좋은 팩", "price": -5, "lines": { "random": "한 줄", "no-such-situation": "버려짐", "greet-night": "  " } }, { "id": "empty", "lines": { "bogus": "x" } }, { "id": "ok", "lines": { "random": "중복 id" } } ],
                  "profiles": [ { "name": "조용히", "values": { "typing": false, "no-such-switch": true } }, { "name": "", "values": { "typing": true } }, { "name": "빈 것", "values": { "bogus": true } } ],
                  "pageModes": [ { "id": "Letter", "name": "편지", "layout": "something-else", "prompt": "편지로 쓴다", "length": "아주아주 길게" }, { "id": "sms2", "layout": "messenger" } ],
                  "scripts": [ "run-me.exe" ], "unknownKind": { "a": 1 } }
                """);
            Check(mixed.Name == "섞인 모드 둘째 줄" && mixed.LinePacks!.Single() is { Id: "ok", Price: 0 } && mixed.LinePacks![0].Lines!.Keys.Single() == "random" && mixed.Profiles!.Single().Values!.Keys.Single() == "typing", "Only known situations and switches are kept; empty or repeated entries are left out");
            Check(mixed.PageModes!.Count == 2 && mixed.PageModes[0] is { Id: "letter", Layout: StageMode.Stage, Length: "" } && mixed.PageModes[1] is { Layout: StageMode.Messenger, Name: "sms2" } && !JsonSerializer.Serialize(mixed, ModFile.Writing).Contains("run-me") && !JsonSerializer.Serialize(mixed, ModFile.Writing).Contains("unknownKind"), "An unknown layout becomes the stage, an unknown length is ignored, and kinds the program does not know are not carried along");
            Check(Throws(() => ModFile.Parse("not json")) && Throws(() => ModFile.Parse("null")) && Throws(() => ModFile.Parse("{\"format\":2,\"id\":\"x\",\"themes\":[{\"id\":\"a\"}]}")) && Throws(() => ModFile.Parse("{\"format\":1,\"id\":\"!!!\",\"themes\":[{\"id\":\"a\"}]}")) && Throws(() => ModFile.Parse("{\"format\":1,\"id\":\"x\"}")) && Throws(() => ModFile.Parse("{\"format\":1,\"id\":\"x\",\"linePacks\":[{\"id\":\"p\",\"lines\":{\"bogus\":\"x\"}}]}")) && Throws(() => ModFile.Parse(new string(' ', ModFile.MaxBytes + 1))), "A file that is not a mod, has no id, is of another format, adds nothing usable or is too large is refused");
            var long1 = ModFile.Parse(JsonSerializer.Serialize(new ModFile { Id = new string('a', 200), Name = new string('가', 500), Description = new string('나', 5000), PageModes = new() { new() { Id = "m", Prompt = new string('다', 9000) } } }));
            Check(long1.Id.Length == 40 && long1.Name.Length == 60 && long1.Description.Length == 400 && long1.PageModes!.Single().Prompt.Length == 4000, "Every text in a mod is cut to size");

            // Installing, switching and removing
            var store = ModStore.Reload(home); int changes = 0; store.Changed += () => changes++;
            Check(store.Mods.Count == 0 && store.LinePacks.Count == 0 && store.Profiles.Count == 0 && store.PageModes.Count == 0 && Pastel.Themes.Length == 5 && LinePacks.All.Length == LinePacks.BuiltIn.Length && StageMode.All.Count == 2, "Without mods there is only what is built in");
            string first = Path.Combine(files, "아무 이름.json"); File.WriteAllText(first, sample); var installed = store.Install(first);
            Check(installed.Id == "my-first-mod" && File.Exists(Path.Combine(home, "mods", "my-first-mod.kwmod.json")) && store.Mods.Single().Enabled && changes == 1 && File.Exists(first), "A mod is copied into the mods folder under its own id; the file it came from is left where it was");
            Check(Pastel.Themes.Length == 6 && Pastel.Themes[5] == ("mod-my-first-mod-night", "밤하늘팝") && LinePacks.All.Last() is { Id: "mod-my-first-mod-greetings", Price: 0 } && LinePacks.Find("mod-my-first-mod-greetings") != null && store.Profiles.Single() is { Name: "방송 중", Id: "mod-my-first-mod-0" } && StageMode.All.Count == 3 && StageMode.Find("mod-my-first-mod-letter") is { Name: "손편지", Length: "길게", Source: "나의 첫 모드" }, "What an enabled mod adds shows up beside the built-in themes, line packs, profiles and page modes");
            Check(StageMode.Find("mod-my-first-mod-letter").Prompt.StartsWith("\n[손편지]\n") && StageMode.Find("mod-my-first-mod-letter").Thread("g") == "g|mod-my-first-mod-letter", "A page mode from a mod brings its instruction and has a thread of its own");
            var pink = Pastel.Sample("mod-my-first-mod-night", "#FFB9DC"); var plain = Pastel.Sample("strawberry", "#FFB9DC");
            Check(((System.Windows.Media.SolidColorBrush)pink).Color != ((System.Windows.Media.SolidColorBrush)plain).Color && ((System.Windows.Media.SolidColorBrush)pink).Color.B > ((System.Windows.Media.SolidColorBrush)pink).Color.R && ((System.Windows.Media.SolidColorBrush)Pastel.Sample("mod-my-first-mod-night", "#FFFFFF")).Color == System.Windows.Media.Colors.White, "A mod theme recolours the pinks towards its hue and leaves white alone");
            var saved = new Preferences { Theme = "mod-my-first-mod-night" }; saved.Normalize(); Check(saved.Theme == "mod-my-first-mod-night", "A mod's theme can be the saved theme");
            store.Enable("my-first-mod", false); store.Enable("my-first-mod", false);
            Check(!store.Mods.Single().Enabled && Pastel.Themes.Length == 5 && LinePacks.All.Length == LinePacks.BuiltIn.Length && StageMode.All.Count == 2 && store.Profiles.Count == 0 && changes == 2 && StageMode.Find("mod-my-first-mod-letter").Id == "stage", "Switching a mod off takes away what it added, and the page goes back to the standard mode");
            saved.Normalize(); Check(saved.Theme == "strawberry", "A theme whose mod is off falls back to the standard theme");
            Check(ModStore.Reload(home).Mods.Single().Enabled == false, "Whether a mod is on is remembered");
            store = ModStore.Reload(home); store.Enable("my-first-mod", true); Check(Pastel.Themes.Length == 6 && store.Mods.Single().Enabled, "It can be switched on again");
            // A newer file with the same id replaces the old one
            var update = ModFile.Sample(); update.Version = "2.0"; update.Themes = null; string second = Path.Combine(files, "update.json"); File.WriteAllText(second, JsonSerializer.Serialize(update, ModFile.Writing)); store.Install(second);
            Check(store.Mods.Single().File.Version == "2.0" && Pastel.Themes.Length == 5 && Directory.GetFiles(Path.Combine(home, "mods"), "*.kwmod.json").Length == 1, "Installing a mod with the same id updates it");
            // A broken file in the folder is reported, not fatal
            File.WriteAllText(Path.Combine(home, "mods", "broken.kwmod.json"), "{ nope"); File.WriteAllText(Path.Combine(home, "mods", "notes.txt"), "not a mod"); store = ModStore.Reload(home);
            Check(store.Mods.Count == 1 && store.Problems.Single().StartsWith("broken.kwmod.json") && ModStore.Text(store.Mods[0]).Contains("\"version\": \"2.0\""), "A file that cannot be read is listed as a problem and the others still load; a mod's file can be read back for viewing");
            Check(Throws(() => store.Install(Path.Combine(home, "mods", "broken.kwmod.json"))) && store.Mods.Count == 1, "A broken file cannot be installed");
            store.Remove("my-first-mod"); store.Remove("no-such-mod");
            Check(store.Mods.Count == 0 && !File.Exists(Path.Combine(home, "mods", "my-first-mod.kwmod.json")) && StageMode.All.Count == 2 && LinePacks.All.Length == LinePacks.BuiltIn.Length, "Removing a mod deletes its file and what it added");
            // Two mods cannot collide, with each other or with what is built in
            foreach (string id in new[] { "one", "two" }) { var twin = ModFile.Sample(); twin.Id = id; twin.Themes![0].Id = "strawberry"; twin.PageModes![0].Id = "sms"; string path = Path.Combine(files, id + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(twin, ModFile.Writing)); store.Install(path); }
            Check(Pastel.Themes.Select(t => t.Id).Distinct().Count() == 7 && StageMode.All.Select(m => m.Id).Distinct().Count() == 4 && LinePacks.All.Select(p => p.Id).Distinct().Count() == LinePacks.BuiltIn.Length + 2 && StageMode.Find("sms").Layout == StageMode.Messenger && Pastel.Themes[0].Id == "strawberry", "Everything a mod adds is named after the mod, so nothing built in can be replaced");
        }
        finally { ModStore.Reload(Path.Combine(root, "mods-none")); }
        Check(Pastel.Themes.Length == 5 && StageMode.All.Count == 2, "Afterwards only the built-in content is left");
        return count;
    }
}
