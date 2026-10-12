using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// One chat room of a page: its own thread of talk, an author's note that is added to the prompt only here, and a memory
// (a summary of what happened in this room, kept up to date by the AI or written by the user).
public sealed class StageRoom
{
    public const int MaxNote = 4000, MaxSummary = 6000;
    public string Id { get; set; } = "";            // "" = the first room; its thread is the plain key, as before there were rooms
    public string Name { get; set; } = "기본 방";
    public string Note { get; set; } = "";
    public string Summary { get; set; } = "";
    public bool AutoSummary { get; set; } = true;
    public DateTimeOffset SummarizedAt { get; set; }   // the talk up to here is in the summary
    // What this room adds to the prompt.
    public string Context() =>
        (Summary.Trim().Length > 0 ? "\n[지금까지의 기억]\n이 대화에서 전에 있었던 일의 요약입니다. 이미 일어난 일로 기억하고 그 위에서 이어 가되, 요약을 그대로 읊지는 않습니다.\n" + Summary.Trim() : "")
        + (Note.Trim().Length > 0 ? "\n[작가 노트]\n이 대화에서 반드시 따르는 추가 지시입니다.\n" + Note.Trim() : "");
}
public sealed class StageRoomSet
{
    public string Current { get; set; } = "";
    public List<StageRoom> Rooms { get; set; } = new();
}
public sealed class StageRoomState
{
    public Dictionary<string, StageRoomSet> Sets { get; set; } = new();   // by page: "character", "character|mode" or "pair:a+b"
    public string SummaryConnection { get; set; } = "";                    // name of the AI connection that writes summaries; "" = the one in use
    public int SummaryEvery { get; set; } = 10;                            // new exchanges before the memory is brought up to date
    public bool HidePanel { get; set; }                                    // the list of rooms on the pages is folded away
}

// The chat rooms of the pages (stage-rooms.json). A page always has at least one room.
public sealed class StageRooms
{
    public const int MaxRooms = 20;
    readonly string path;
    public StageRoomState State { get; }
    public event Action? Changed;
    public StageRooms(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "stage-rooms.json"); State = Storage.ReadJson<StageRoomState>(path, 16L * 1024 * 1024) ?? new(); State.Sets ??= new(); Fit();
    }
    void Fit()
    {
        State.SummaryConnection = SafeFiles.OneLine(State.SummaryConnection, 80); State.SummaryEvery = Math.Clamp(State.SummaryEvery, 4, 50);
        foreach (var key in State.Sets.Keys.ToArray())
        {
            var set = State.Sets[key]; if (set == null) { State.Sets.Remove(key); continue; }
            set.Rooms = (set.Rooms ?? new()).Where(r => r != null && r.Id != null).DistinctBy(r => r.Id).Take(MaxRooms).ToList(); set.Current ??= "";
            foreach (var room in set.Rooms)
            {
                room.Name = SafeFiles.OneLine(room.Name, 24) is { Length: > 0 } name ? name : "채팅방";
                room.Note = Cut(room.Note, StageRoom.MaxNote); room.Summary = Cut(room.Summary, StageRoom.MaxSummary);
            }
        }
    }
    static string Cut(string? text, int length) { text = (text ?? "").Replace("\r\n", "\n").Trim(); return text[..Math.Min(text.Length, length)]; }
    StageRoomSet Set(string key)
    {
        if (!State.Sets.TryGetValue(key, out var set)) State.Sets[key] = set = new();
        if (set.Rooms.Count == 0) set.Rooms.Add(new StageRoom());
        return set;
    }
    public IReadOnlyList<StageRoom> Rooms(string key) => Set(key).Rooms;
    public StageRoom Current(string key) { var set = Set(key); return set.Rooms.FirstOrDefault(r => r.Id == set.Current) ?? set.Rooms[0]; }
    public void Select(string key, StageRoom room) { var set = Set(key); if (set.Current == room.Id || !set.Rooms.Contains(room)) return; set.Current = room.Id; Save(); }
    public StageRoom Add(string key, string name)
    {
        var set = Set(key); if (set.Rooms.Count >= MaxRooms) throw new InvalidOperationException("채팅방은 " + MaxRooms + "개까지 만들 수 있어요.");
        var room = new StageRoom { Id = Guid.NewGuid().ToString("N")[..8], Name = name }; set.Rooms.Add(room); set.Current = room.Id; Save(); return room;
    }
    // Takes a room away (never the last one). The caller clears its thread of talk.
    public bool Remove(string key, StageRoom room)
    {
        var set = Set(key); if (set.Rooms.Count < 2 || !set.Rooms.Remove(room)) return false;
        if (set.Current == room.Id) set.Current = set.Rooms[0].Id; Save(); return true;
    }
    // The key of a room's thread of talk in StageLog.
    public static string Thread(string key, StageRoom room) => room.Id.Length == 0 ? key : key + "#" + room.Id;
    public string Thread(string key) => Thread(key, Current(key));
    public static (string Key, string Room) Split(string thread) { int mark = thread.LastIndexOf('#'); return mark < 0 ? (thread, "") : (thread[..mark], thread[(mark + 1)..]); }
    // The name of the room a thread belongs to, when it is worth telling (there is more than one room, or it is not the first).
    public string RoomName(string thread)
    {
        var (key, id) = Split(thread); if (!State.Sets.TryGetValue(key, out var set)) return id.Length > 0 ? "지워진 방" : "";
        var room = set.Rooms.FirstOrDefault(r => r.Id == id); return room == null ? (id.Length > 0 ? "지워진 방" : "") : set.Rooms.Count > 1 || id.Length > 0 ? room.Name : "";
    }
    public void Save() { Fit(); OrderedStore.SaveJson(path, State); Changed?.Invoke(); }
}
