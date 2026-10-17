using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// How the novel is written: which AI connection, how much it may read and write, its parameters, the prompt set, and
// how the window behaves. The novel's own - nothing here is shared with the chat or the character pages.
public sealed class NovelState
{
    public string Connection { get; set; } = "";              // name of the AI connection; "" = the one in use for chat
    public int MaxContext { get; set; } = 32000;              // tokens a request may hold; older parts of the story are left out first
    public int MaxOutput { get; set; } = 4000;                // tokens of one new part
    public AiParameters Parameters { get; set; } = new();     // temperature and the rest, for the novel only
    public bool Stream { get; set; } = true;                  // show the text as it is written
    public bool ShowThinking { get; set; }                    // keep and show what the model thought before writing
    public NovelPromptSet Prompts { get; set; } = NovelPrompts.Standard();
    public string First { get; set; } = "";                   // the characters of the story: one, or two
    public string Second { get; set; } = "";
    public int KeepTurns { get; set; } = StageLog.Keep;       // parts of a story a room keeps
    public Dictionary<string, string> Thoughts { get; set; } = new();   // "thread|ticks" -> what the model thought for that part
}
// The novel's settings (novel.json). The story itself is kept in the page talks (StageLog) under the novel's own rooms.
public sealed class NovelBook
{
    public const int MaxThoughts = 300, MaxThought = 30000;
    readonly string path;
    public NovelState State { get; }
    public NovelBook(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "novel.json"); State = Storage.ReadJson<NovelState>(path, 16L * 1024 * 1024) ?? new(); Normalize();
    }
    void Normalize()
    {
        var s = State; s.Connection = SafeFiles.OneLine(s.Connection, 80); s.MaxContext = Math.Clamp(s.MaxContext, 2000, 2000000); s.MaxOutput = Math.Clamp(s.MaxOutput, 256, 64000);
        s.Parameters ??= new(); s.Parameters.Normalize(); s.Prompts = NovelPrompts.Normalize(s.Prompts); s.First ??= ""; s.Second ??= ""; s.KeepTurns = Math.Clamp(s.KeepTurns, 30, StageLog.MaxKeep);
        s.Thoughts = (s.Thoughts ?? new()).Where(p => p.Value != null).TakeLast(MaxThoughts).ToDictionary(p => p.Key, p => p.Value[..Math.Min(p.Value.Length, MaxThought)]);
    }
    public static string Key(string thread, ChatTurn turn) => thread + "|" + turn.At.UtcTicks;
    public string Thought(string thread, ChatTurn turn) => State.Thoughts.GetValueOrDefault(Key(thread, turn), "");
    public void Keep(string thread, ChatTurn turn, string thought)
    {
        if (thought.Trim().Length == 0) return; State.Thoughts[Key(thread, turn)] = thought.Trim()[..Math.Min(thought.Trim().Length, MaxThought)];
        if (State.Thoughts.Count > MaxThoughts) foreach (var old in State.Thoughts.Keys.Take(State.Thoughts.Count - MaxThoughts).ToArray()) State.Thoughts.Remove(old);
        Save();
    }
    public void Save() { Normalize(); OrderedStore.SaveJson(path, State); }
    // The key of the novel page of these characters (its rooms and threads hang on it).
    public static string Page(string first, string second) => "novel:" + first + (second.Length > 0 && second != first ? "+" + second : "");
}
