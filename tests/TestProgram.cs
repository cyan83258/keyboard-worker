using System;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// Unit checks live outside the shipped app: dotnet run --project tests -c Release
public static class TestProgram
{
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--verify-sounds"))
            {
                var results = args.SkipWhile(a => a != "--verify-sounds").Skip(1).Select(path => Path.GetFileName(path) + ": PASS " + SoundPresetTests.VerifyAudio(path).ToString("0.000") + "s").ToArray();
                File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "sound-verification.txt"), results); foreach (var line in results) Console.WriteLine(line); return 0;
            }
            if (args.Contains("--mixer-probe")) { Console.WriteLine("Volume mixer session level: " + (AppVolume.Get(_ => true)?.ToString() ?? "no audio session")); return 0; }
            // Earlier runs leave thousands of small files behind; start clean.
            try { string old = Path.Combine(AppContext.BaseDirectory, "test-data"); if (Directory.Exists(old)) Directory.Delete(old, true); } catch (IOException) { }
            Storage.UseTestRoot(Path.Combine(AppContext.BaseDirectory, "test-data", "settings-root"));
            SelfTest.Run(); OrderedStore.FlushAsync().GetAwaiter().GetResult();
            Console.Write(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "self-test-result.txt"))); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("FAIL: " + e); return 1; }
    }
}
