using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace KeyboardWorker;

public static class CompanionIntegrationTest
{
    public static void Prepare()
    {
        Storage.UseTestRoot(Path.Combine(AppContext.BaseDirectory, "test-data", "duo-live-" + Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(Storage.Root);
        var settings = new Preferences { Left = SystemParameters.WorkArea.Left, Top = SystemParameters.WorkArea.Top + 100, Size = 160 };
        var store = new PresetStore();
        var first = store.Add("First fixture", settings, new Persona { Name = "First" });
        var second = store.Add("Second fixture", settings, new Persona { Name = "Second" });
        settings.ActivePresetId = first.Id; settings.Duo = new DuoOptions { Enabled = true, PresetId = second.Id, Left = settings.Left, Top = settings.Top };
        Storage.SaveAsync(settings).GetAwaiter().GetResult(); OrderedStore.FlushAsync().GetAwaiter().GetResult();
    }
    public static async Task Run(Buddy buddy)
    {
        async Task CheckVisible(string stage)
        {
            var companion = buddy.DisplayedCompanion ?? throw new Exception(stage + ": no secondary window"); await companion.Ready();
            if (!buddy.IsVisible || !companion.IsVisible || companion.Opacity <= 0) throw new Exception(stage + ": not both visible");
            var primary = new Rect(buddy.Left, buddy.Top, buddy.Width, buddy.Height); var second = new Rect(companion.Left, companion.Top, companion.Width, companion.Height);
            var overlap = Rect.Intersect(primary, second);
            if (!overlap.IsEmpty && overlap.Width * overlap.Height > Math.Min(primary.Width * primary.Height, second.Width * second.Height) * .65) throw new Exception(stage + ": pets overlap");
            Pastel.Capture(companion, Path.Combine(AppContext.BaseDirectory, "duo-live-" + stage + ".png"));
        }
        await CheckVisible("startup"); string selected = buddy.Pref.Duo.PresetId; buddy.SetCompanion(false);
        if (buddy.DisplayedCompanion != null) throw new Exception("Hide did not remove secondary window.");
        await buddy.LoadCompanion(selected); await CheckVisible("load-button"); await OrderedStore.FlushAsync(); var stored = Storage.Load();
        if (!stored.Duo.Enabled || stored.Duo.PresetId != selected || stored.Duo.Left == -9999 || stored.Duo.Top == -9999) throw new Exception("Load button did not persist enabled placement.");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "duo-live-result.txt"), "PASS: startup restores two visible separated pets; disabled-to-load creates secondary; enabled selection and repaired placement persisted; isolated user data\n");
    }
}
