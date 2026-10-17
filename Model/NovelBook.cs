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
    public string Model { get; set; } = "";                   // another model of that connection, for the novel only; "" = the connection's own
    public bool SystemPrompt { get; set; } = true;            // send system messages as such (off: as the user's)
    public bool Squash { get; set; }                          // join system messages that come in a row
    public bool MergeRoles { get; set; }                      // join any messages of one role that come in a row
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
    public NovelSummaryOptions Summary { get; set; } = new();           // how the story is summarized (NovelSummaries)
    public Dictionary<string, List<NovelSummary>> Summaries { get; set; } = new();   // by thread, in the order of the story
    public bool Welcomed { get; set; }                                  // the first-time note about the model was shown
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
        var s = State; s.Connection = SafeFiles.OneLine(s.Connection, 80); s.Model = SafeFiles.OneLine(s.Model, 200); s.MaxContext = Math.Clamp(s.MaxContext, 2000, 2000000); s.MaxOutput = Math.Clamp(s.MaxOutput, 256, 64000);
        s.Parameters ??= new(); s.Parameters.Normalize(); s.Prompts = NovelPrompts.Normalize(s.Prompts); s.First ??= ""; s.Second ??= ""; s.KeepTurns = Math.Clamp(s.KeepTurns, 30, StageLog.MaxKeep);
        s.Summary = NovelSummaries.Normalize(s.Summary);
        s.Summaries = (s.Summaries ?? new()).Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value.Where(x => x != null && x.Text != null && x.From <= x.To).OrderBy(x => x.From).TakeLast(NovelSummaries.MaxEntries).ToList());
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
    public IReadOnlyList<NovelSummary> SummariesOf(string thread) => State.Summaries.TryGetValue(thread, out var list) ? list : Array.Empty<NovelSummary>();
    // Keeps a summary of a run of parts; one that covered the same parts before is replaced.
    public void Keep(string thread, NovelSummary summary)
    {
        if (!State.Summaries.TryGetValue(thread, out var list)) State.Summaries[thread] = list = new();
        list.RemoveAll(s => s.From <= summary.To && s.To >= summary.From); list.Add(summary); list.Sort((a, b) => a.From.CompareTo(b.From)); Save();
    }
    public bool Forget(string thread, NovelSummary summary) { bool gone = State.Summaries.TryGetValue(thread, out var list) && list.Remove(summary); if (gone) Save(); return gone; }
    public void Rewrite(string thread, NovelSummary summary, string text) { if (!State.Summaries.TryGetValue(thread, out var list) || list.IndexOf(summary) is not (>= 0 and var at)) return; list[at] = summary with { Text = text.Trim() }; Save(); }
    public void Save() { Normalize(); OrderedStore.SaveJson(path, State); }
    // The key of the novel page of these characters (its rooms and threads hang on it).
    public static string Page(string first, string second) => "novel:" + first + (second.Length > 0 && second != first ? "+" + second : "");
}
