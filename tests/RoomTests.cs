using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// Chat rooms of the pages (their notes and memory), and finding the background a scene moved to.
public static class RoomTests
{
    sealed class Summarizer : HttpMessageHandler
    {
        public string Request = "", Address = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = System.Text.RegularExpressions.Regex.Unescape(await request.Content!.ReadAsStringAsync(token)); Address = request.RequestUri!.ToString();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { choices = new[] { new { message = new { content = "- 둘은 바닷가에 갔다.\n- 루카는 약속을 했다." }, finish_reason = "stop" } } }), Encoding.UTF8, "application/json") };
        }
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        string home = Path.Combine(root, "rooms"); Directory.CreateDirectory(home);

        // ---- backgrounds, whatever they are called ----
        var titles = new[] { "내 방", "sandy beach daytime", "sandy beach night", "sandy beach sunset", "classroom", "school hallway morning", "building hallway", "park bench daytime", "playground", "seaside bus stop", "boy room light off", "bathroom light on" };
        Check(BackdropWords.Gloss("sandy beach night") == "바닷가 밤" && BackdropWords.Gloss("school hallway morning") == "학교 복도 아침" && BackdropWords.Gloss("building hallway") == "건물 복도" && BackdropWords.Gloss("boy room light off") == "남자아이 방 불 꺼짐" && BackdropWords.Gloss("seaside bus stop") == "바닷가 버스 정류장" && BackdropWords.Gloss("playground") == "놀이터", "An English title is told in Korean");
        Check(BackdropWords.Gloss("내 방") == "" && BackdropWords.Gloss("sparkle") == "" && BackdropWords.Listed(new[] { "내 방", "sandy beach night" }) == "내 방, sandy beach night(바닷가 밤)", "A Korean or unknown title is listed as it is");
        Check(BackdropWords.Find(titles, "sandy beach night") == "sandy beach night" && BackdropWords.Find(titles, " Sandy_Beach_Night ") == "sandy beach night" && BackdropWords.Find(titles, "sandybeachnight") == "sandy beach night" && BackdropWords.Find(titles, "sandy beach night(바닷가 밤)") == "sandy beach night", "A title is found however it is spelled");
        Check(BackdropWords.Find(titles, "바닷가 밤") == "sandy beach night" && BackdropWords.Find(titles, "해변") == "sandy beach daytime" && BackdropWords.Find(titles, "노을 지는 바다") == "sandy beach sunset" && BackdropWords.Find(titles, "the classroom") == "classroom" && BackdropWords.Find(titles, "교실") == "classroom" && BackdropWords.Find(titles, "내 방") == "내 방", "A place named in Korean or loosely finds its picture");
        Check(BackdropWords.Find(titles, "") == null && BackdropWords.Find(titles, "없음") == null && BackdropWords.Find(titles, "우주 정거장") == null && BackdropWords.Find(titles, "sparkle") == null, "A name that fits nothing finds nothing");
        Check(BackdropWords.Guess(titles, "바닷가에 도착해 모래를 밟는다", "classroom") == "sandy beach daytime" && BackdropWords.Guess(titles, "밤바다를 바라본다 · 파도 소리", "classroom") == "sandy beach night" && BackdropWords.Guess(titles, "해변을 걷는다", "sandy beach sunset") == null, "The narration says where the scene went");
        Check(BackdropWords.Guess(titles, "밤이 되어 해변이 어두워진다", "sandy beach daytime") == "sandy beach night" && BackdropWords.Guess(titles, "교실 문을 연다", "sandy beach night") == "classroom" && BackdropWords.Guess(titles, "고개를 끄덕인다", "classroom") == null && BackdropWords.Guess(titles, "", "classroom") == null && BackdropWords.Guess(titles, "우주로 날아간다", "classroom") == null, "The time of day follows; narration without a known place changes nothing");

        string pictures = Path.Combine(home, "backgrounds"); Directory.CreateDirectory(pictures);
        foreach (string name in new[] { "classroom.jpg", "sandy_beach_daytime.jpg", "sandy_beach_night.jpg" }) File.WriteAllBytes(Path.Combine(pictures, name), new byte[] { 1 });
        var book = new StageBook(Path.Combine(home, "book"), pictures); book.State.Backdrop = book.Backdrops.First(b => b.Title == "classroom").Path;
        Check(book.Titled("Sandy_Beach_Night")?.Title == "sandy beach night" && book.Titled("바닷가")?.Title == "sandy beach daytime" && book.Titled("달") == null, "The book finds a background by a loose name");
        Check(book.Move("classroom", "") == null && book.CurrentTitle == "classroom" && book.Move("바닷가 낮", "")?.Title == "sandy beach daytime" && book.CurrentTitle == "sandy beach daytime", "Naming the background on show changes nothing; naming another moves the scene");
        Check(book.Move("", "좋아. (밤이 된 해변에서 불꽃을 본다) 예쁘네.")?.Title == "sandy beach night" && book.Move("", "교실 얘기는 그만하자.") == null && book.CurrentTitle == "sandy beach night", "With no name the narration is followed, and talk about a place is not");
        book.State.AutoBackdrop = false; Check(book.Move("classroom", "(교실로 간다)") == null && book.CurrentTitle == "sandy beach night", "Nothing moves when the user changes backgrounds by hand");
        book.State.AutoBackdrop = true; book.State.Backdrop = book.Backdrops.First(b => b.Title == "classroom").Path; book.Save(); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        string moved = Path.Combine(home, "moved", "backgrounds"); Directory.CreateDirectory(moved); foreach (string file in Directory.GetFiles(pictures)) File.Copy(file, Path.Combine(moved, Path.GetFileName(file)));
        var elsewhere = new StageBook(Path.Combine(home, "book"), moved);
        Check(elsewhere.CurrentTitle == "classroom" && elsewhere.State.Backdrop.StartsWith(moved, StringComparison.OrdinalIgnoreCase), "A built-in background stays chosen when the program folder moves");
        Check(StageTags.BackdropInstruction(new[] { "classroom", "sandy beach night" }, "classroom").Contains("sandy beach night(바닷가 밤)") && StageTags.BackdropInstruction(new[] { "classroom" }, "classroom") == "", "The model is shown the backgrounds with what they are");

        // ---- rooms ----
        var rooms = new StageRooms(home); int saved = 0; rooms.Changed += () => saved++;
        var first = rooms.Current("g");
        Check(rooms.Rooms("g").Count == 1 && first.Id == "" && first.Name == "기본 방" && rooms.Thread("g") == "g" && first.Context() == "" && first.AutoSummary, "A page starts with one room, whose thread is the page's own");
        var second = rooms.Add("g", "여름 방학");
        Check(rooms.Rooms("g").Count == 2 && rooms.Current("g") == second && second.Id.Length == 8 && rooms.Thread("g") == "g#" + second.Id && rooms.Rooms("e").Count == 1 && rooms.Thread("e") == "e" && saved == 1, "A new room is opened with a thread of its own; other pages have their own rooms");
        Check(StageRooms.Split("g#" + second.Id) == ("g", second.Id) && StageRooms.Split("g|sms") == ("g|sms", "") && StageRooms.Split("pair:a+b#12345678") == ("pair:a+b", "12345678"), "A thread says which page and room it belongs to");
        Check(rooms.RoomName("g#" + second.Id) == "여름 방학" && rooms.RoomName("g") == "기본 방" && rooms.RoomName("e") == "" && rooms.RoomName("g#gone") == "지워진 방" && rooms.RoomName("nobody") == "", "A room is named where it helps to tell rooms apart");
        second.Note = "  둘은 여름 방학 동안 바닷가 마을에 머문다.  "; second.Summary = "- 첫날 기차를 놓쳤다."; rooms.Save();
        Check(second.Note == "둘은 여름 방학 동안 바닷가 마을에 머문다." && second.Context().Contains("[지금까지의 기억]") && second.Context().Contains("첫날 기차를 놓쳤다") && second.Context().Contains("[작가 노트]") && second.Context().IndexOf("[지금까지의 기억]") < second.Context().IndexOf("[작가 노트]"), "A room adds its memory and its note to the prompt");
        rooms.Select("g", first); Check(rooms.Current("g") == first && rooms.Thread("g") == "g", "Rooms are switched");
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); var again = new StageRooms(home);
        Check(again.Rooms("g").Count == 2 && again.Current("g").Id == "" && again.Rooms("g")[1].Name == "여름 방학" && again.Rooms("g")[1].Note.StartsWith("둘은"), "Rooms are saved");
        Check(again.Remove("g", again.Rooms("g")[0]) && again.Rooms("g").Count == 1 && again.Current("g").Name == "여름 방학" && !again.Remove("g", again.Current("g")), "A room can be deleted, but not the last one");
        bool full = false; try { for (int i = 0; i < StageRooms.MaxRooms + 1; i++) again.Add("many", "방 " + i); } catch (InvalidOperationException) { full = true; }
        Check(full && again.Rooms("many").Count == StageRooms.MaxRooms, "There is a limit to the rooms of a page");

        var noon = new DateTime(2026, 5, 10, 12, 0, 0);
        Check(StageRoomPanel.When(new DateTimeOffset(noon.AddHours(-3)), noon) == "오늘 09:00" && StageRoomPanel.When(new DateTimeOffset(noon.AddDays(-1)), noon) == "어제" && StageRoomPanel.When(new DateTimeOffset(noon.AddDays(-20)), noon) == "4월 20일" && StageRoomPanel.When(new DateTimeOffset(noon.AddYears(-1)), noon) == "2025. 5. 10.", "A room says when it was last talked in");

        // ---- memory ----
        var day = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero); var room = new StageRoom();
        var turns = Enumerable.Range(0, 12).Select(i => new ChatTurn("말 " + i, "답 " + i, day.AddMinutes(i), "루카", "g")).ToArray();
        Check(StageMemory.Pending(turns, room).Length == 12 && StageMemory.Due(turns, room, 10) && !StageMemory.Due(turns.Take(9).ToArray(), room, 10), "Talk that is not in the summary yet is counted");
        room.SummarizedAt = turns[7].At; Check(StageMemory.Pending(turns, room).Select(t => t.User).SequenceEqual(new[] { "말 8", "말 9", "말 10", "말 11" }) && !StageMemory.Due(turns, room, 10), "Only talk after the last summary is new");
        room.AutoSummary = false; room.SummarizedAt = default; Check(!StageMemory.Due(turns, room, 10), "A room that does not want automatic summaries gets none");
        string asked = StageMemory.Request("- 예전 일", new[] { new ChatTurn("바다 가자", "(웃으며) 좋다.", day, "루카", "g"), new ChatTurn("", "루카: 왔군.\n미오: 응!", day, "루카 · 미오", "pair:g+e") });
        Check(asked.Contains("[기존 요약]\n- 예전 일") && asked.Contains("사용자: 바다 가자\n루카: (웃으며) 좋다.") && asked.Contains("루카: 왔군.\n미오: 응!") && !asked.Contains("루카 · 미오:") && StageMemory.Request("", turns).Contains("(아직 없음)"), "The summary is asked for with the old summary and the new talk");
        Check(StageMemory.Request("", Enumerable.Range(0, 60).Select(i => new ChatTurn("m" + i, new string('가', 3000), day, "루카", "g")).ToArray()).Length < StageMemory.MaxTurns * (StageMemory.MaxTurnLength + 40), "A long talk is asked about within bounds");
        var chat = new ChatPreferences(); chat.Selected = 0; chat.Connections.Add(new AiConnection { Name = "요약용", Format = "Chat Completions", Endpoint = "http://localhost:9/v1/chat/completions", Model = "small", Header = "Authorization", Bearer = true });
        Check(StageMemory.Settings(chat, "요약용").Current.Model == "small" && StageMemory.Settings(chat, "").Current.Name == chat.Current.Name && StageMemory.Settings(chat, "없는 연결").Current.Name == chat.Current.Name && chat.Selected == 0, "The summary goes to the connection named for it, or the one in use");
        using var handler = new Summarizer(); using var http = new HttpClient(handler);
        string summary = StageMemory.Summarize(chat, "요약용", "- 예전 일", turns, CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult();
        Check(summary == "- 둘은 바닷가에 갔다.\n- 루카는 약속을 했다." && handler.Address.StartsWith("http://localhost:9/") && handler.Request.Contains("small") && handler.Request.Contains("기록 담당") && handler.Request.Contains("예전 일") && !handler.Request.Contains("햄스터"), "The summary is written by the chosen model from the instruction, not in a character's voice");
        return count;
    }
}
