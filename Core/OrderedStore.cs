using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class OrderedStore
{
    sealed record Job(Func<Task> Write, TaskCompletionSource Done);
    static readonly Channel<Job> jobs = Channel.CreateUnbounded<Job>(new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    static readonly ConcurrentDictionary<string, string> errors = new();
    static readonly Task worker = Task.Run(Run);
    public static event Action<string>? Failed;
    public static Task SaveJsonAsync<T>(string path, T immutableSnapshot) => Enqueue(async () =>
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                await JsonSerializer.SerializeAsync(stream, immutableSnapshot, new JsonSerializerOptions { WriteIndented = true }).ConfigureAwait(false);
            File.Move(temporary, path, true); errors.TryRemove(path, out _);
        }
        catch (Exception e)
        {
            string message = Path.GetFileName(path) + " 저장 실패: " + e.Message;
            if (errors.TryAdd(path, message)) Failed?.Invoke(message);
            throw;
        }
    });
    public static void SaveJson<T>(string path, T snapshot) => Observe(SaveJsonAsync(path, snapshot));
    public static Task FlushAsync() => Enqueue(() => Task.CompletedTask);
    public static string[] Errors => System.Linq.Enumerable.ToArray(errors.Values);
    static Task Enqueue(Func<Task> action)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); jobs.Writer.TryWrite(new(action, done)); return done.Task;
    }
    static async Task Run()
    {
        await foreach (var job in jobs.Reader.ReadAllAsync().ConfigureAwait(false))
            try { await job.Write().ConfigureAwait(false); job.Done.TrySetResult(); } catch (Exception e) { job.Done.TrySetException(e); }
    }
    static void Observe(Task task) => _ = task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
}
