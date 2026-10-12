using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KeyboardWorker;

// Something the user set aside from a page talk: a whole exchange (Whole) or a passage picked out of an answer.
// It is a copy, so it stays when the talk itself is tidied away.
public sealed record StageClip(string Thread, string Name, DateTimeOffset At, string User, string Text, bool Whole, DateTimeOffset Saved);

// The clips (stage-clips.json).
public sealed class StageClips
{
    public const int Max = 1000;
    readonly string path; readonly List<StageClip> items;
    public event Action? Changed;
    public StageClips(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "stage-clips.json");
        items = (Storage.ReadJson<List<StageClip>>(path, 32L * 1024 * 1024) ?? new()).Where(c => c != null && c.Thread != null && c.Name != null && c.User != null && c.Text is { Length: > 0 }).TakeLast(Max).ToList();
    }
    public IReadOnlyList<StageClip> Items => items;
    public bool Has(string thread, ChatTurn turn) => items.Any(c => c.Whole && c.Thread == thread && c.At == turn.At);
    // Keeps a whole exchange, or lets go of it when it is already kept. True when it is kept afterwards.
    public bool Toggle(string thread, ChatTurn turn)
    {
        int had = items.RemoveAll(c => c.Whole && c.Thread == thread && c.At == turn.At);
        if (had == 0) Put(new(thread, turn.CharacterName, turn.At, turn.User, turn.Assistant, true, DateTimeOffset.Now));
        Save(); return had == 0;
    }
    // Keeps a passage of an answer. False when there is nothing to keep or the same passage is already there.
    public bool Excerpt(string thread, ChatTurn turn, string passage)
    {
        passage = (passage ?? "").Trim(); if (passage.Length == 0 || items.Any(c => !c.Whole && c.Thread == thread && c.At == turn.At && c.Text == passage)) return false;
        Put(new(thread, turn.CharacterName, turn.At, "", passage[..Math.Min(passage.Length, StageLog.MaxAnswer)], false, DateTimeOffset.Now)); Save(); return true;
    }
    void Put(StageClip clip) { items.Add(clip); if (items.Count > Max) items.RemoveRange(0, items.Count - Max); }
    public void Remove(StageClip clip) { if (items.Remove(clip)) Save(); }
    void Save() { OrderedStore.SaveJson(path, items.ToArray()); Changed?.Invoke(); }
    public static string Text(IEnumerable<StageClip> clips, bool markdown, Func<string, string> title)
    {
        var text = new StringBuilder(markdown ? "# Keyboard Worker 모아 둔 장면\r\n\r\n" : "Keyboard Worker 모아 둔 장면\r\n\r\n");
        foreach (var clip in clips)
        {
            string head = clip.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + " · " + title(clip.Thread) + (clip.Whole ? "" : " · 발췌");
            text.Append(markdown ? "### " + head : "[" + head + "]").Append("\r\n").Append(markdown ? "\r\n" : "");
            if (clip.User.Length > 0) text.Append(markdown ? "**나**: " : "나: ").Append(clip.User.Replace("\n", "\r\n")).Append("\r\n").Append(markdown ? "\r\n" : "");
            text.Append(clip.Text.Replace("\r\n", "\n").Replace("\n", "\r\n")).Append("\r\n\r\n");
        }
        return text.ToString();
    }
}

// A page talk as a file that can be read back in (JSON), for moving it to another PC or keeping it outside the program:
// the exchanges alone, or a whole chat room with its name, author's note and memory.
public static class StageArchive
{
    public const string Format = "keyboard-worker-talk";
    public const int MaxTurns = 5000;
    // Kind, Room, Note and Summary are only in the file of a whole room.
    sealed record Package(string Format, int Version, string Title, ChatTurn[] Turns, string? Kind = null, string? Room = null, string? Note = null, string? Summary = null);
    public sealed record RoomFile(string Kind, string Name, string Note, string Summary, ChatTurn[] Turns);
    static readonly JsonSerializerOptions options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static string Write(string title, IEnumerable<ChatTurn> turns) => JsonSerializer.Serialize(new Package(Format, 1, title, turns.ToArray()), options);
    // kind: which kind of page the room is from (StageMode.Kind), so that it is not read into another kind.
    public static string WriteRoom(string kind, string title, StageRoom room, IEnumerable<ChatTurn> turns) => JsonSerializer.Serialize(new Package(Format, 2, title, turns.ToArray(), kind, room.Name, room.Note, room.Summary), options);
    // Both throw InvalidOperationException, with a message for the user, when the text is not such a file.
    public static ChatTurn[] Read(string json) => Turns(Open(json)) is { Length: > 0 } turns ? turns : throw new InvalidOperationException("파일에 대화가 들어 있지 않습니다.");
    // A room may be empty of talk (only a note); a plain talk file is read as a room without note or memory.
    public static RoomFile ReadRoom(string json)
    {
        var package = Open(json);
        return new(package.Kind ?? "", SafeFiles.OneLine(package.Room ?? package.Title, 24) is { Length: > 0 } name ? name : "불러온 방", package.Note ?? "", package.Summary ?? "", Turns(package));
    }
    static Package Open(string json)
    {
        Package? package;
        try { package = JsonSerializer.Deserialize<Package>(json, options); }
        catch (JsonException) { throw new InvalidOperationException("대화 파일의 형식이 올바르지 않습니다."); }
        return package == null || package.Format != Format || package.Turns == null ? throw new InvalidOperationException("Keyboard Worker에서 내보낸 대화 파일(JSON)이 아닙니다.") : package;
    }
    static ChatTurn[] Turns(Package package) =>
        package.Turns.Where(t => t != null && t.User != null && t.Assistant is { Length: > 0 }).Take(MaxTurns)
            .Select(t => t with { CharacterName = t.CharacterName ?? "", TargetKey = t.TargetKey ?? "primary", Mode = t.Mode ?? "" }).ToArray();
}
