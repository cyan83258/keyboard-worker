using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace KeyboardWorker;

// Command-line tasks that run without the pet window and then exit:
//   --image-diet                 merge identical pictures and shrink oversized ones (see ImageDiet)
//   --clean-unused               send pictures and sounds nothing refers to to the recycle bin (see AssetCleanup)
//   --install-sound-presets a b  add sound files as presets
//   --repair-selected-companion  switch the saved second pet back on
//   --music-probe                list the browser media sessions Windows reports
//   --update-probe               ask GitHub for the newest release of this program and report what the update button would see
//   --make-icon [folder]         regenerate Assets\pet.ico
//   --ai-probe [name]            send one tiny request per saved AI connection, plain and with reasoning effort / verbosity, and report what each server accepts
// Tasks that change the user's files refuse to run while the app itself is running, and write a short report next to the program.
public static class Maintenance
{
    public const string InstanceName = "Local\\KeyboardWorker.Desktop";
    static string Beside(string file) => Path.Combine(AppContext.BaseDirectory, file);
    static T Wait<T>(System.Threading.Tasks.Task<T> task) => task.GetAwaiter().GetResult();
    static void Wait(System.Threading.Tasks.Task task) => task.GetAwaiter().GetResult();

    // True when a command was given and handled; the caller then exits.
    public static bool TryRun(string[] args)
    {
        if (args.Contains("--music-probe")) { File.WriteAllLines(Beside("music-probe.txt"), Wait(System.Threading.Tasks.Task.Run(MusicMonitor.Probe))); return true; }
        if (args.Contains("--repair-selected-companion")) { RepairCompanion(); return true; }
        if (args.Contains("--install-sound-presets")) { InstallSounds(args.SkipWhile(a => a != "--install-sound-presets").Skip(1).ToArray()); return true; }
        if (args.Contains("--image-diet")) { Exclusive("image-diet-result.txt", ImageFolderDiet); return true; }
        if (args.Contains("--clean-unused")) { Exclusive("clean-unused-result.txt", CleanUnused); return true; }
        if (args.Contains("--ai-probe")) { File.WriteAllLines(Beside("ai-probe.txt"), Wait(System.Threading.Tasks.Task.Run(() => AiProbe(args.SkipWhile(a => a != "--ai-probe").Skip(1).FirstOrDefault() ?? "")))); return true; }
        if (args.Contains("--update-probe")) { File.WriteAllLines(Beside("update-probe.txt"), Wait(System.Threading.Tasks.Task.Run(UpdateProbe))); return true; }
        if (args.Contains("--make-icon")) { PetIcon.Generate(args.Length > 1 ? args[1] : Beside("Assets")); return true; }
        return false;
    }
    // Test data for the self-checks that start the real app (--duo-integration-test, --showcase-test).
    public static void PrepareSelfCheck(string[] args)
    {
        if (args.Contains("--duo-integration-test")) CompanionIntegrationTest.Prepare();
        if (!args.Contains("--showcase-test")) return;
        CompanionIntegrationTest.Prepare(); var prefs = Storage.Load(); var book = new AdventureBook(); book.Friend(prefs.ActivePresetId, "모모", 125).Favorite = "작은 책";
        book.Pair(prefs.ActivePresetId, prefs.Duo.PresetId).Bond = 42;
        var quest = new StoryQuest { CharacterId = prefs.ActivePresetId, Title = "달빛 도서관의 작은 열쇠", Premise = "친구들은 책갈피 사이에서 반짝이는 편지를 발견했어요.", Minutes = 5, ProgressMs = 300000 }; book.AddQuest(quest);
        book.AddStory(quest, "책 속에서 피어난 작은 별", "모모는 조심스럽게 작은 열쇠를 돌렸어요.\n\n도서관 문 너머에는 책 대신 작은 별들이 가지런히 놓여 있었죠. 루미는 가장 희미한 별 옆에 앉아 낮게 속삭였어요. ‘빛이 작아도 길을 비춰 주는구나.’\n\n두 친구는 별을 가져오지 않았어요. 대신 그 빛을 오래 바라본 순간을 마음속에 간직했어요. 돌아오는 길에는 평소보다 발걸음이 조금 가벼웠답니다.", new PetGame(), 99);
        book.State.Friends[prefs.ActivePresetId].Discovered = true; book.Save(true); Wait(OrderedStore.FlushAsync());
    }

    // Asks every connection that has a key for a one-word answer: as it is, then with a reasoning effort, with a verbosity, and with both.
    // The saved settings are not changed and the keys are never written out; only whether each request was accepted.
    static async System.Threading.Tasks.Task<string[]> AiProbe(string only)
    {
        var saved = new ChatRepository().Preferences; var lines = new System.Collections.Generic.List<string>(); using var http = new System.Net.Http.HttpClient { Timeout = Timeout.InfiniteTimeSpan }; var client = new AiClient(http);
        for (int i = 0; i < saved.Connections.Count; i++)
        {
            var connection = saved.Connections[i]; if (only.Length > 0 && connection.Name != only) continue;
            if (connection.Secret.Length == 0) { lines.Add($"{connection.Name} ({connection.Format}, {connection.Model}): no key saved, skipped"); continue; }
            foreach (var (label, effort, verbosity) in new[] { ("as saved", null, null), ("effort low", "low", null), ("effort none", "none", null), ("verbosity low", null, "low"), ("effort high + verbosity high", "high", "high") })
            {
                var settings = ChatRepository.Clone(saved); settings.Selected = i; settings.Streaming = false; settings.AutoContinue = false; settings.MaxOutputTokens = 512; settings.RequestTimeoutSeconds = 60; settings.Profile = new Persona { Name = "점검", Prompts = new PromptTemplates { Profile = "짧게 답한다." } };
                var parameters = settings.Connections[i].Parameters; if (effort != null) parameters.ReasoningEffort = effort; if (verbosity != null) parameters.Verbosity = verbosity;
                string result; var watch = System.Diagnostics.Stopwatch.StartNew();
                try { string answer = await client.Send(settings, Array.Empty<ChatTurn>(), "안녕? 한 단어로만 답해 줘.", CancellationToken.None); result = "OK (" + watch.ElapsedMilliseconds + " ms, " + answer.Length + " chars)"; }
                catch (Exception e) { result = "FAILED: " + SafeFiles.OneLine(e.Message, 300); }
                lines.Add($"{connection.Name} ({connection.Format}, {connection.Model}) · {label}: {result}");
            }
        }
        return lines.ToArray();
    }
    static async System.Threading.Tasks.Task<string[]> UpdateProbe()
    {
        var lines = new System.Collections.Generic.List<string> { "this version: " + AppInfo.Version, "update address: " + (Updater.Configured ? Updater.Repository : "(none)") };
        try { var release = await Updater.Check(CancellationToken.None); lines.Add($"latest release: {release.Tag} · {release.AssetName} · {release.Size} bytes · checksum {(release.Sha256.Length > 0 ? "yes" : "no")} · newer than this: {Updater.Newer(release, AppInfo.Version)}"); }
        catch (Exception e) { lines.Add("check: " + SafeFiles.OneLine(e.Message, 300)); }
        return lines.ToArray();
    }
    static void RepairCompanion()
    {
        var saved = Storage.Load(); var presets = new PresetStore();
        if (!presets.Items.Any(p => p.Id == saved.Duo.PresetId) || saved.Duo.PresetId == saved.ActivePresetId) throw new InvalidOperationException("복원할 둘째 프리셋이 없습니다.");
        saved.Duo = saved.Duo with { Enabled = true }; Wait(Storage.SaveAsync(saved));
    }
    static void InstallSounds(string[] files)
    {
        var settings = Storage.Load();
        foreach (var source in files) settings.SoundPresets.Add(new SoundPreset { Name = Path.GetFileNameWithoutExtension(source), Path = SoundAssets.Import(source, Storage.Root) });
        settings.Normalize(); Wait(Storage.SaveAsync(settings));
    }
    // Runs a task that changes the user's files: only while the app is closed, with its result (or failure) written to a report file.
    static void Exclusive(string reportFile, Func<string[]> task)
    {
        string report = Beside(reportFile);
        try
        {
            using var single = new Mutex(true, InstanceName, out bool free);
            if (!free) { File.WriteAllText(report, "STOPPED: Keyboard Worker is running.\n"); return; }
            File.WriteAllLines(report, task());
        }
        catch (Exception e) { File.WriteAllText(report, "FAILED: " + e + "\n"); }
    }
    static string[] References() => AssetCleanup.References(Storage.Load(), new PresetStore().Items, new PetGame().State);
    static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0") + " MB";

    // Replaced files go to the recycle bin and the settings are backed up first.
    static string[] ImageFolderDiet()
    {
        var settings = Storage.Load(); var presets = new PresetStore(); var game = new PetGame(); string root = Storage.Root, images = Path.Combine(root, "images");
        long Size() => Directory.Exists(images) ? new DirectoryInfo(images).GetFiles().Sum(f => f.Length) : 0; int Count() => Directory.Exists(images) ? Directory.GetFiles(images).Length : 0;
        long sizeBefore = Size(); int countBefore = Count();
        var plan = ImageDiet.Scan(root, AssetCleanup.References(settings, presets.Items, game.State));
        string safety = Path.Combine(root, "backups");
        Directory.CreateDirectory(safety);
        SettingsBackup.Export(root, Path.Combine(safety, "before-image-diet-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip"), false, true);
        // Every user of a duplicate is pointed at the kept copy and saved before any file leaves.
        settings.ImagePaths = settings.ImagePaths.ToDictionary(p => p.Key, p => ImageDiet.Map(plan.Merge, p.Value));
        Wait(Storage.SaveAsync(settings)); Wait(presets.RemapImagesAsync(plan.Merge)); game.RemapImages(plan.Merge); Wait(OrderedStore.FlushAsync());
        var result = ImageDiet.Apply(plan, References());
        var after = References().Where(p => p.StartsWith(images, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new[]
        {
            $"duplicates found: {plan.Merge.Count}, oversized found: {plan.Oversized.Length}",
            $"merged: {result.Merged}, resized: {result.Resized}, failed: {result.Failed}, saved: {Megabytes(result.SavedBytes)}",
            $"folder before: {countBefore} files, {Megabytes(sizeBefore)}", $"folder after: {Count()} files, {Megabytes(Size())}",
            $"referenced pictures: {after.Length}, missing after diet: {after.Count(p => !File.Exists(p))}"
        };
    }
    // Files in use and files added in the last 24 hours are kept; the rest goes to the recycle bin.
    static string[] CleanUnused()
    {
        string root = Storage.Root; var found = AssetCleanup.Scan(root, References(), DateTime.UtcNow); var result = AssetCleanup.Clean(root, found, References(), DateTime.UtcNow);
        var used = References().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new[]
        {
            $"unused found: {found.Length} files, {Megabytes(found.Sum(f => f.Bytes))} (images {found.Count(f => f.Path.Contains("\\images\\"))}, sounds {found.Count(f => f.Path.Contains("\\sounds\\"))})",
            $"sent to recycle bin: {result.Removed}, skipped: {result.Skipped}, failed: {result.Failed}",
            $"files in use: {used.Length}, missing after cleanup: {used.Count(p => !File.Exists(p))}"
        };
    }
}
