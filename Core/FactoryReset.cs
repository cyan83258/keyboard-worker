using System;
using System.IO;

namespace KeyboardWorker;

// Putting the program back to how it was first installed. Like a settings import, it is only staged while the program
// runs (a marker file) and done at the next start, before anything is read - so nothing that is open writes itself back.
public static class FactoryReset
{
    const string Marker = "reset.pending";
    public static void Stage(string? root = null) { root ??= Storage.Root; Directory.CreateDirectory(root); File.WriteAllText(Path.Combine(root, Marker), DateTime.Now.ToString("o")); }
    // Empties the settings folder when a reset was staged. Returns what went wrong, or null.
    public static string? ApplyPending(string? root = null)
    {
        root ??= Storage.Root; string marker = Path.Combine(root, Marker); if (!File.Exists(marker)) return null;
        try
        {
            foreach (string folder in Directory.GetDirectories(root)) Directory.Delete(folder, true);
            foreach (string file in Directory.GetFiles(root)) if (!string.Equals(file, marker, StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            File.Delete(marker); return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Not tried again at every start: what could be removed is gone, the rest stays.
            try { File.Delete(marker); } catch (Exception inner) when (inner is IOException or UnauthorizedAccessException) { }
            return e.Message;
        }
    }
}
