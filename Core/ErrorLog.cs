using System;
using System.IO;

namespace KeyboardWorker;

// Unexpected errors are appended to error-log.txt in the settings folder so a problem can be reported after the fact.
public static class ErrorLog
{
    const string FileName = "error-log.txt"; const long Limit = 512 * 1024;
    static readonly object gate = new();
    public static string FilePath => Path.Combine(Storage.Root, FileName);
    public static void Write(string where, Exception error) => Write(where, error.ToString());
    public static void Write(string where, string detail)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Storage.Root); string path = FilePath;
                // Keep the newer half when the file grows large.
                if (File.Exists(path) && new FileInfo(path).Length > Limit) { string text = File.ReadAllText(path); File.WriteAllText(path, "(이전 기록은 지웠습니다)\r\n\r\n" + text[(text.Length / 2)..]); }
                string version = typeof(ErrorLog).Assembly.GetName().Version?.ToString() ?? "?";
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {where} · 버전 {version} · Windows {Environment.OSVersion.Version} · 테마 {Pastel.Theme}\r\n{detail}\r\n\r\n");
            }
        }
        catch { }
    }
}
