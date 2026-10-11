using System;
using System.Collections.Generic;

namespace KeyboardWorker;

// Follows which program the user is working in (the window in front) and decides when a remark about it is due.
// Windows of this app do not replace the tracked program: chatting with the pet is still "while using that program".
public sealed class AppTracker
{
    const long Settle = 30000, Gap = 300000;   // be in the program for half a minute; at most one remark every five minutes
    readonly Dictionary<string, long> remarked = new(StringComparer.OrdinalIgnoreCase);
    long since, lastRemark = long.MinValue / 2; int pick;
    public AppTracker(int firstPick = 0) { pick = firstPick; }
    public AppInFront Current { get; private set; } = new();
    // False while one of this app's own windows (or nothing) is in front.
    public bool InFront { get; private set; }
    public bool GameInFront => InFront && Current.Kind == "game";
    public string Summary => Current.Title.Length == 0 ? "아직 인식한 프로그램이 없어요" : Current.Title + " · " + AppKinds.Label(Current.Kind);
    string Key => Current.Path.Length > 0 ? Current.Path : Current.Process;

    // process: name of the program whose window is in front ("KeyboardWorker" for this app, "" when unknown); path: its file.
    public void Track(string process, string path, AwarenessOptions options, long now)
    {
        InFront = process.Length > 0 && process != "KeyboardWorker"; if (!InFront) return;
        string kind = AppKinds.Classify(process, path, options);
        if (path != Current.Path || process != Current.Process) { Current = new(process, path, AppKinds.Describe(path, process), kind); since = now; }
        else Current = Current with { Kind = kind };
    }
    // A game or work program has been in front long enough and has not been remarked on recently.
    public bool RemarkDue(AwarenessOptions options, long now) =>
        options.Apps && options.Comments && InFront && Current.Kind != "other" && now - since >= Settle && now - lastRemark >= Gap
        && (!remarked.TryGetValue(Key, out long at) || now - at >= options.CommentMinutes * 60000L);
    // Call when the remark is made. Returns which ready-made line to use, so they rotate.
    public int Remarked(long now) { remarked[Key] = now; lastRemark = now; return pick++; }
}
