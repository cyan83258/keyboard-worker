using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

// Character pages: names of standing pictures, the pictures themselves, the saved sets; and the wording that keeps a character in its own voice.
public static class StageTests
{
    static BitmapSource Picture(int width, int height, Func<int, int, (byte B, byte G, byte R, byte A)> colour)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) { var (b, g, r, a) = colour(x, y); int at = (y * width + x) * 4; pixels[at] = b; pixels[at + 1] = g; pixels[at + 2] = r; pixels[at + 3] = a; }
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); picture.Freeze(); return picture;
    }
    static byte[] Pixels(BitmapSource picture) { var converted = new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0); var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(pixels, converted.PixelWidth * 4, 0); return pixels; }
    static void Save(BitmapSource picture, string path) { var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(picture)); using var file = File.Create(path); encoder.Save(file); }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
        string home = Path.Combine(root, "stage"); Directory.CreateDirectory(home);

        // Names from file names
        Check(StageTags.FromFile(@"C:\art\Smile.png") == "웃음" && StageTags.FromFile("SAD_02.png") == "슬픔" && StageTags.FromFile("angry (3).jpg") == "화남" && StageTags.FromFile("gil_blush.png") == "부끄러움" && StageTags.FromFile("default.png") == "기본" && StageTags.FromFile("웃음.png") == "웃음" && StageTags.FromFile("토라짐-1.png") == "삐짐", "Usual file names are recognised as feelings, in English or Korean, with numbers after them");
        Check(StageTags.FromFile("교복 차림.png") == "교복 차림" && StageTags.FromFile("비 오는 날_2.png") == "비 오는 날" && StageTags.FromFile("0001.png") == "0001" && StageTags.FromFile(new string('가', 60) + ".png").Length == 20 && StageTags.FromFile("a[b],c.png") == "ab c", "Any other name becomes a situation of its own, kept short and free of marks");
        Check(StageTags.Clean("  ") == "기본" && StageTags.Clean(null) == "기본" && StageTags.Clean("밤\n산책") == "밤 산책", "A name is one short line; an empty one is the everyday picture");
        var sprites = new List<StageSprite> { new() { Tag = "교복", Path = "u.png" }, new() { Tag = "기본", Path = "d.png" }, new() { Tag = "웃음", Path = "s.png" }, new() { Tag = "화남", Path = "a.png" } };
        Check(StageTags.Choose(sprites, "화남", "happy")!.Path == "a.png" && StageTags.Choose(sprites, " 교복 ", "neutral")!.Path == "u.png" && StageTags.Choose(sprites, "없는 이름", "happy")!.Path == "s.png" && StageTags.Choose(sprites, "", "sleepy")!.Path == "d.png" && StageTags.Choose(sprites, "", "neutral")!.Path == "d.png", "The picture is the one named, else the one for the feeling, else the everyday one");
        Check(StageTags.Choose(sprites.Take(1).ToList(), "", "sad")!.Path == "u.png" && StageTags.Choose(new List<StageSprite>(), "웃음", "happy") == null, "With nothing fitting the first picture is used; with no pictures there is none");
        sprites.Add(new() { Tag = "웃음", Path = "s2.png" }); var names = StageTags.Names(sprites);
        Check(names.SequenceEqual(new[] { "교복", "기본", "웃음", "화남" }) && StageTags.Instruction(names).Contains("교복, 기본, 웃음, 화남") && StageTags.Instruction(names).Contains("[sprite:이름]") && StageTags.Instruction(new[] { "기본" }) == "" && StageTags.Instruction(Array.Empty<string>()) == "", "The AI is offered each name once, and is not asked when there is nothing to choose");
        Check(StageTags.Read("흥, 늦었군.\n[sprite:화남]") == ("흥, 늦었군.", "화남", "") && StageTags.Read("좋아. [SPRITE: 웃음 ]  ") == ("좋아.", "웃음", "") && StageTags.Read("표식 없음") == ("표식 없음", "", ""), "The mark at the end of an answer is read and taken off");
        Check(StageTags.Read("나가자.\n[bg:비 오는 거리]\n[sprite:웃음]") == ("나가자.", "웃음", "비 오는 거리") && StageTags.Read("[sprite:기본] 그래. [bg: 교실 ]") == ("그래.", "기본", "교실"), "A change of background is read as well, wherever the marks stand");
        Check(StageTags.Display("오늘은 [sp") == "오늘은" && StageTags.Display("오늘은 [sprite:웃") == "오늘은" && StageTags.Display("나가자 [BG:거") == "나가자" && StageTags.Display("나가자 [bg:거리] 어서") == "나가자 어서" && StageTags.Display("오늘은 [괄호] 그대로") == "오늘은 [괄호] 그대로" && StageTags.Display("그냥 대사") == "그냥 대사", "Marks are not shown while an answer arrives, whole or in part");
        Check(StageTags.BackdropTitle(@"C:\bg\교실_저녁.jpg") == "교실 저녁" && StageTags.BackdropTitle("room-02.png") == "room" && StageTags.BackdropTitle("123.jpg") == "123" && StageTags.Clean(" ", "배경") == "배경", "A background is called what its file is called");
        var titles = new[] { "교실", "내 방", "비 오는 거리" };
        Check(StageTags.BackdropInstruction(titles, "내 방").Contains("지금 배경: 내 방") && StageTags.BackdropInstruction(titles, "").Contains("지금 배경: 없음") && StageTags.BackdropInstruction(titles, "").Contains("교실, 내 방, 비 오는 거리") && StageTags.BackdropInstruction(titles, "").Contains("[bg:이름]") && StageTags.BackdropInstruction(new[] { "교실" }, "") == "", "The AI is told the background on show and may only change it when there is another to change to");

        // Only the page's own character speaks
        var rest = new[] { "미오", "세나" };
        Check(StageTalk.OwnLine("루카: 흥, 왔군.", "루카", rest) == "흥, 왔군." && StageTalk.OwnLine("루카 ： 흥.\n루카: 또 왔나.", "루카", rest) == "흥.\n또 왔나." && StageTalk.OwnLine("  평범한 대사  ", "루카", rest) == "평범한 대사", "A name in front of a line is taken off");
        Check(StageTalk.OwnLine("루카: 흥, 왔군.\n\n미오: 어서 와!", "루카", rest) == "흥, 왔군." && StageTalk.OwnLine("늦었군.\n세나: 죄송합니다.\n미오: 하하.", "루카", rest) == "늦었군.", "The answer ends where another character would start talking");
        Check(StageTalk.OwnLine("미오: 혼자만 말하면 그대로", "루카", rest) == "미오: 혼자만 말하면 그대로" && StageTalk.OwnLine("시간은 12:30이다. 미오는 어디 갔지?", "루카", rest) == "시간은 12:30이다. 미오는 어디 갔지?" && StageTalk.Shown("루카: 흥 [sprite:화", "루카") == "흥", "Ordinary colons and names inside a sentence are left alone; an arriving answer is cleaned the same way");
        // The talks on the pages are their own history, apart from the ordinary chat
        string chatHome = Path.Combine(home, "chat"); Directory.CreateDirectory(chatHome); var log = new StageLog(chatHome); var at = DateTimeOffset.Now; int logged = 0; log.Changed += () => logged++;
        log.Add("g", new ChatTurn("첫 말", "루카: 왔군.\n\n미오: 나도!", at, "루카", "g")); log.Add("g", new ChatTurn("둘째 말", "(웃으며) 그래.", at, "루카", "g")); log.Add("g|sms", new ChatTurn("문자", "뭐냐.", at, "루카", "g|sms")); log.Add("e", new ChatTurn("미오에게", "응!", at, "미오", "e"));
        Check(log.Turns("g").Count == 2 && log.Turns("g|sms").Single().User == "문자" && log.Turns("e").Count == 1 && log.Turns("nobody").Count == 0 && log.Count == 4 && logged == 4, "Each character and each page mode has a thread of its own");
        var mine = StageTalk.Context(log, "g", "루카", rest, 12);
        Check(mine.Select(t => t.User).SequenceEqual(new[] { "첫 말", "둘째 말" }) && mine[0].Assistant == "왔군." && mine[1].Assistant == "(웃으며) 그래." && StageTalk.Context(log, "g", "루카", rest, 1).Single().User == "둘째 말" && StageTalk.Context(log, "g", "루카", rest, 0).Length == 0, "The model is shown only this thread, as much as asked for, without names in front");
        for (int i = 0; i < StageLog.Keep + 20; i++) log.Add("long", new ChatTurn("m" + i, new string('가', i == 20 ? 30000 : 40), at, "루카", "long"));
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); var reopenedLog = new StageLog(chatHome);
        Check(reopenedLog.Turns("long").Count == StageLog.Keep && reopenedLog.Turns("long")[0].User == "m20" && reopenedLog.Turns("long")[0].Assistant.Length == StageLog.MaxAnswer && reopenedLog.Turns("g").Count == 2, "A thread keeps its newest exchanges, cut to size, and is saved");
        reopenedLog.Clear("g"); reopenedLog.Clear("nobody"); OrderedStore.FlushAsync().GetAwaiter().GetResult(); Check(new StageLog(chatHome).Turns("g").Count == 0 && new StageLog(chatHome).Turns("g|sms").Count == 1, "Clearing one thread leaves the others");
        // Looking after a thread by hand: edit, delete, tidy, a limit of its own, and a file that can be read restored
        string careHome = Path.Combine(home, "care"); Directory.CreateDirectory(careHome); var care = new StageLog(careHome) { Limit = 100 }; var careDay = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 120; i++) care.Add("g", new ChatTurn("말 " + i, "답 " + i, careDay.AddMinutes(i), "루카", "g"));
        Check(care.Turns("g").Count == 100 && care.Turns("g")[0].User == "말 20" && care.Threads.Single() == "g", "The limit the user chose drops the oldest exchanges");
        var thirdTurn = care.Turns("g")[2];
        Check(care.Replace("g", thirdTurn, thirdTurn with { Assistant = "고친 답" }) && care.Turns("g")[2].Assistant == "고친 답" && care.Turns("g")[2].User == "말 22" && !care.Replace("g", thirdTurn, thirdTurn) && !care.Replace("nobody", thirdTurn, thirdTurn), "An exchange can be edited in place");
        Check(care.Remove("g", care.Turns("g")[0]) && care.Turns("g").Count == 99 && care.Turns("g")[0].User == "말 21" && !care.Remove("g", thirdTurn), "One exchange can be deleted");
        Check(care.Trim("g", 200) == 0 && care.Trim("g", 50) == 49 && care.Turns("g").Count == 50 && care.Turns("g")[^1].User == "말 119" && care.Trim("nobody", 0) == 0, "Tidying keeps the newest exchanges");
        string archiveText = StageArchive.Write("루카 · 마주 보기", care.Turns("g")); var restored = StageArchive.Read(archiveText);
        Check(archiveText.Contains("답 119") && restored.Length == 50 && restored[0] == care.Turns("g")[0] && restored[^1].Assistant == "답 119", "A thread is written to a file and read restored the same");
        var elsewhere = new StageLog(Path.Combine(careHome, "other")); elsewhere.Add("e", new ChatTurn("나중 말", "나중 답", careDay.AddDays(1), "미오", "e"));
        Check(elsewhere.Import("e", restored) == 50 && elsewhere.Turns("e").Count == 51 && elsewhere.Turns("e")[^1].User == "나중 말" && elsewhere.Turns("e")[0].User == "말 70" && elsewhere.Import("e", restored) == 0, "Imported exchanges are put in order of time, once");
        int refusalCount = 0; foreach (string bad in new[] { "not json", "{}", "{\"format\":\"something-else\",\"turns\":[]}", "{\"format\":\"keyboard-worker-fewKept\",\"turns\":[]}" }) try { StageArchive.Read(bad); } catch (InvalidOperationException) { refusalCount++; }
        Check(refusalCount == 4, "A file that is not a fewKept is refused with a message");
        Check(care.Trim("g", 0) == 50 && care.Threads.Count == 0, "A thread tidied to nothing is gone");
        var fewKept = new StageTalkOptions { KeepTurns = 5 }; fewKept.Normalize(); var manyKept = new StageTalkOptions { KeepTurns = 99999 }; manyKept.Normalize();
        Check(fewKept.KeepTurns == 50 && manyKept.KeepTurns == StageLog.MaxKeep && new StageTalkOptions().KeepTurns == StageLog.Keep, "The limit stays within bounds");

        // What the user sets aside is a copy that outlives the fewKept
        var setAside = new StageClips(careHome); var likedTurn = new ChatTurn("좋아하는 말", "(웃으며) 잊지 마라. 이건 약속이다.", careDay, "루카", "g"); int clipCount = 0; setAside.Changed += () => clipCount++;
        Check(!setAside.Has("g", likedTurn) && setAside.Toggle("g", likedTurn) && setAside.Has("g", likedTurn) && !setAside.Has("e", likedTurn) && setAside.Items.Single().Whole && setAside.Items[0].Text == likedTurn.Assistant, "A whole exchange is set aside");
        Check(setAside.Excerpt("g", likedTurn, "  이건 약속이다. ") && !setAside.Excerpt("g", likedTurn, "이건 약속이다.") && !setAside.Excerpt("g", likedTurn, "   ") && setAside.Items.Count == 2 && !setAside.Items[1].Whole && setAside.Items[1].Text == "이건 약속이다." && setAside.Items[1].User == "", "A passage is set aside once, trimmed");
        OrderedStore.FlushAsync().GetAwaiter().GetResult(); var setAsideAgain = new StageClips(careHome);
        Check(setAsideAgain.Items.Count == 2 && setAsideAgain.Has("g", likedTurn) && clipCount == 2, "Clips are saved");
        string clipWritten = StageClips.Text(setAsideAgain.Items, true, key => key == "g" ? "루카 · 마주 보기" : key);
        Check(clipWritten.Contains("루카 · 마주 보기 · 발췌") && clipWritten.Contains("**나**: 좋아하는 말") && clipWritten.Contains("이건 약속이다."), "Clips are written out with where they came from");
        Check(!setAsideAgain.Toggle("g", likedTurn) && !setAsideAgain.Has("g", likedTurn) && setAsideAgain.Items.Single().Text == "이건 약속이다.", "Setting aside again lets go of the exchange and keeps the passage");
        setAsideAgain.Remove(setAsideAgain.Items[0]); Check(setAsideAgain.Items.Count == 0, "A clip can be removed");
        var pageCast = new[] { new Buddy.StageCharacter("g", "루카", new Persona(), null, "대표", "루카"), new Buddy.StageCharacter("e", "미오", new Persona(), null, "둘째", "미오") };
        Check(StageHistoryWindow.Title("g", pageCast, StageMode.BuiltIn) == "루카 · 마주 보기" && StageHistoryWindow.Title("e|sms", pageCast, StageMode.BuiltIn) == "미오 · 문자 메시지" && StageHistoryWindow.Title("pair:g+e", pageCast, StageMode.BuiltIn) == "루카 ♡ 미오 · 둘만의 페이지" && StageHistoryWindow.Title("gone", pageCast, StageMode.BuiltIn).StartsWith("(지워진 캐릭터)"), "A thread is named by whose page it is");

        var ordinary = new ChatRepository(Path.Combine(chatHome, "ordinary")); Check(ordinary.Turns.Count == 0 && !File.Exists(Path.Combine(chatHome, "chat-history.json")), "Nothing of a page's talk is written to the ordinary chat history");

        // Length, room and the page's own prompt
        var talk = new StageTalkOptions(); var king = new Persona { Name = "루카", UserName = "잡종" };
        Check(talk.Length == "중간" && talk.MaxTokens == 2048 && talk.Prompt == StageTalkOptions.StandardPrompt && StageTalkOptions.Lengths.Select(l => l.Name).SequenceEqual(new[] { "매우 짧게", "짧게", "중간", "길게", "매우 길게" }) && StageTalkOptions.Guide("없는 길이") == StageTalkOptions.Guide("중간"), "A page talk has five lengths to choose from and starts in the middle");
        string instruction = StageTalk.Instruction(talk, StageMode.BuiltIn[0], king);
        Check(instruction.Contains("루카와(과) 사용자(잡종)") && instruction.Contains("답변 길이: 2~4문장") && !instruction.Contains("{name}") && !instruction.Contains("{length}") && instruction.Contains("성격을 무르게 만들지 말고") && instruction.Contains("(괄호) 안에 짧게"), "The page prompt is filled in with the names and the wanted length, and asks for the character's own depth");
        talk.Length = "매우 길게"; Check(StageTalk.Instruction(talk, StageMode.BuiltIn[0], king).Contains("여러 문단") && StageTalk.Instruction(talk, new StageMode("x", "편지", StageMode.Stage, "\n편지로 쓴다", "짧게"), king).Contains("답변 길이: 1~2문장"), "The chosen length is passed on; a mode adds its own instruction and may fix the length");
        var odd = new StageTalkOptions { Length = "아무거나", MaxTokens = 5, ContextTurns = 999, Prompt = "  " }; odd.Normalize();
        Check(odd.Length == "중간" && odd.MaxTokens == 256 && odd.ContextTurns == 60 && odd.Prompt == StageTalkOptions.StandardPrompt && new StageTalkOptions { Prompt = "내 프롬프트 {name}", MaxTokens = 99999 }.Snapshot() is { Prompt: "내 프롬프트 {name}" }, "Talk settings stay within range, and an empty prompt goes back to the standard one");
        // Text messages share nothing with the page: their own options, prompt, modes and threads
        var texting = StageTalkOptions.Texting(); string textInstruction = StageTalk.Instruction(texting, StageMode.BuiltIn[1], king);
        Check(texting.Length == "짧게" && texting.Prompt == StageTalkOptions.StandardTextPrompt && textInstruction.StartsWith("\n[문자 메시지]\n") && textInstruction.Contains("휴대폰 메신저") && textInstruction.Contains("한 번에 보내는 분량: 1~2문장") && !textInstruction.Contains("{name}") && !textInstruction.Contains("마주 보고 이야기하는 장면") && !instruction.Contains("메신저"), "Text messages have a prompt of their own, and the page's prompt says nothing of them");
        var blank = new StageTalkOptions { Prompt = " " }; blank.Normalize(StageTalkOptions.StandardTextPrompt); Check(blank.Prompt == StageTalkOptions.StandardTextPrompt, "An emptied texting prompt goes back to the texting standard");
        var freshState = new StageState(); freshState.Texting.MaxTokens = 3000; var stateCopy = freshState.Snapshot();
        Check(freshState.Talk.Prompt == StageTalkOptions.StandardPrompt && freshState.Texting.Prompt == StageTalkOptions.StandardTextPrompt && stateCopy.Texting.MaxTokens == 3000 && stateCopy.Talk.MaxTokens == 2048 && !ReferenceEquals(stateCopy.Texting, freshState.Texting), "The page and the text messages keep separate settings");
        Check(StageMode.Of(StageMode.Stage).Single().Id == "stage" && StageMode.Of(StageMode.Messenger).Single().Id == "sms" && StageMode.Find("sms", StageMode.Messenger).Id == "sms" && StageMode.Find("stage", StageMode.Messenger).Id == "sms" && StageMode.Find("sms").Id == "stage", "A mode belongs to the page or to the text messages, never both");
        Check(StageMode.Kind("g") == StageMode.Stage && StageMode.Kind("g#12345678") == StageMode.Stage && StageMode.Kind("g|sms") == StageMode.Messenger && StageMode.Kind("g|sms#12345678") == StageMode.Messenger && StageMode.Kind("pair:g+e#1") == StageMode.Pair && StageMode.Kind("g|unknown") == StageMode.Stage, "A thread says which kind of page it is from");
        var keptRoom = new StageRoom { Id = "abcd1234", Name = "여름 방학", Note = "바닷가 마을", Summary = "- 기차를 놓쳤다" }; var roomTurns = new[] { new ChatTurn("가자", "좋다.", DateTimeOffset.Now, "루카", "g") };
        var roomFile = StageArchive.ReadRoom(StageArchive.WriteRoom(StageMode.Stage, "루카", keptRoom, roomTurns));
        Check(roomFile is { Kind: StageMode.Stage, Name: "여름 방학", Note: "바닷가 마을", Summary: "- 기차를 놓쳤다" } && roomFile.Turns.Single() == roomTurns[0], "A room is written to a file with its talk, note and memory, and read back");
        var bare = StageArchive.ReadRoom(StageArchive.Write("루카 · 마주 보기", roomTurns)); var noTalk = StageArchive.ReadRoom(StageArchive.WriteRoom(StageMode.Messenger, "루카", new StageRoom { Name = "빈 방", Note = "노트만" }, Array.Empty<ChatTurn>()));
        Check(bare is { Kind: "", Note: "", Summary: "" } && bare.Name == "루카 · 마주 보기" && bare.Turns.Length == 1 && noTalk is { Kind: StageMode.Messenger, Name: "빈 방", Note: "노트만" } && noTalk.Turns.Length == 0, "A plain talk file is read as a room; a room may be empty of talk");
        Check(StageMode.BuiltIn.Length == 2 && StageMode.Find(null).Id == "stage" && StageMode.Find("sms", StageMode.Messenger).Layout == StageMode.Messenger && StageMode.Find("없는 모드").Id == "stage" &&StageMode.BuiltIn[0].Thread("g") == "g" && StageMode.BuiltIn[1].Thread("g") == "g|sms", "Two page modes are built in, each with its own thread per character");

        // Stage directions apart from the speech
        var beats = StageScript.Parse("(팔짱을 낀다) 흥, 왔군. （작게 웃으며） 앉아라.");
        Check(beats.Count == 2 && beats[0] == new StageBeat("팔짱을 낀다", "흥, 왔군.") && beats[1] == new StageBeat("작게 웃으며", "앉아라."), "What is in brackets is taken out as a direction for the line that follows");
        Check(StageScript.Parse("그냥 대사다.").Single() == new StageBeat("", "그냥 대사다.") && StageScript.Parse("").Count == 0 && StageScript.Parse("  ()  ").Count == 0, "Speech without brackets is one beat with no direction");
        Check(StageScript.Parse("잘 가라. (돌아서며 손을 든다)").Single() == new StageBeat("돌아서며 손을 든다", "잘 가라.") && StageScript.Parse("(말없이 바라본다)").Single() == new StageBeat("말없이 바라본다", "") && StageScript.Parse("(한숨) (고개를 젓는다) 됐다.").Single() == new StageBeat("한숨 · 고개를 젓는다", "됐다."), "A direction after the last line belongs to it; one alone stands alone; two in a row are joined");
        Check(StageScript.Parse("오늘(월요일)은 쉬는 날이다.").Single().Line == "오늘(월요일)은 쉬는 날이다." && StageScript.Parse("흥. (속으로: 귀찮군) 가자.")[1] == new StageBeat("속으로: 귀찮군", "가자.") && StageScript.Parse("뭐냐. (아직 쓰는 중").Last().Narration == "아직 쓰는 중", "A short bracket glued to a word stays in the sentence; an unfinished one (still arriving) is already a direction");
        Check(StageScript.Speech("(웃으며) 그래.\n(끄덕) 가자.") == "그래.\n가자." && StageScript.Speech("(혼자 생각한다)") == "", "The speech alone can be taken for places that show no directions");
        var one = StageScript.Pages(StageScript.Parse("(웃으며) 짧은 대사다."), 60);
        Check(one.Single() == new StageBeat("웃으며", "짧은 대사다."), "A short answer is one page");
        string longAnswer = "(창밖을 보며) 첫째 문장이다. 둘째 문장도 있다! 셋째 문장은 조금 더 길게 이어진다. 넷째 문장으로 끝낸다? (돌아보며) 다섯째 문장.";
        var paged = StageScript.Pages(StageScript.Parse(longAnswer), 24);
        Check(paged.Count >= 4 && paged.All(p => p.Line.Length <= 24) && paged.Take(paged.Count - 1).All(p => p.Narration == "창밖을 보며") && paged[^1] == new StageBeat("돌아보며", "다섯째 문장.") && string.Join(" ", paged.Select(p => p.Line)) == "첫째 문장이다. 둘째 문장도 있다! 셋째 문장은 조금 더 길게 이어진다. 넷째 문장으로 끝낸다? 다섯째 문장.", "A long answer is cut into pages between sentences, each page keeping its direction, with nothing lost");
        var endless = StageScript.Pages(new[] { new StageBeat("", string.Join(" ", Enumerable.Repeat("끝없는말", 30))) }, 30);
        Check(endless.Count > 3 && endless.All(p => p.Line.Length <= 30 && p.Line.Length > 0) && StageScript.Pages(new[] { new StageBeat("끄덕인다", "") }, 30).Single() == new StageBeat("끄덕인다", "……"), "A sentence longer than a page is cut at a space; a direction with no speech still gets a page");
        Check(StageScript.Pages(StageScript.Parse("첫 줄.\n둘째 줄."), 60).Single().Line == "첫 줄.\n둘째 줄." && StageScript.Pages(StageScript.Parse("가. 나. 다."), 3).Count == 1, "Paragraphs that fit stay on one page; the page size is never smaller than a few words");

        // The folding headings keep their titles
        var folded = new List<bool>(); var content = new System.Windows.Controls.StackPanel(); var heading = StageParts.Fold("", content, false, now => folded.Add(now)); StageParts.Retitle(heading, "미리보기 (3/40장)");
        void Click() => heading.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check((string)heading.Content == "▼ 미리보기 (3/40장)" && content.Visibility == System.Windows.Visibility.Visible, "A heading shows its title and its content");
        Click(); Check((string)heading.Content == "▶ 미리보기 (3/40장)" && content.Visibility == System.Windows.Visibility.Collapsed && folded.SequenceEqual(new[] { true }), "Folding hides the content and keeps the title");
        Click(); Check((string)heading.Content == "▼ 미리보기 (3/40장)" && content.Visibility == System.Windows.Visibility.Visible, "Unfolding brings the content back, title and all");
        Click(); StageParts.Retitle(heading, "미리보기 (4/40장)"); Check((string)heading.Content == "▶ 미리보기 (4/40장)" && StageParts.Fold("처음부터 접힘", content, true, _ => { }).Content as string == "▶ 처음부터 접힘", "A new title does not unfold a folded list");
        // Hearts
        Check(HeartMeter.Filled(0) == 0 && HeartMeter.Filled(100) == .5 && HeartMeter.Filled(200) == 1 && HeartMeter.Filled(550) == 2.75 && HeartMeter.Filled(1000) == 5 && HeartMeter.Filled(5000) == 5 && HeartMeter.Filled(-3) == 0 && HeartMeter.Hearts == 5, "Closeness fills five hearts, one for every 200");
        // A plain backdrop becomes transparent; the character stays
        var white = Picture(40, 40, (x, y) => x >= 10 && x < 30 && y >= 10 && y < 30 ? (x >= 18 && x < 22 && y >= 18 && y < 22 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)40, (byte)60, (byte)200, (byte)255)) : ((byte)(250 + x % 4), (byte)252, (byte)255, (byte)255));
        var cleared = Pixels(StageAssets.ClearBackdrop(white)); int At(int x, int y) => (y * 40 + x) * 4;
        Check(cleared[At(0, 0) + 3] == 0 && cleared[At(39, 39) + 3] == 0 && cleared[At(5, 20) + 3] == 0 && cleared[At(0, 0)] == 0, "The backdrop connected to the edges is cleared, slight shades included");
        Check(cleared[At(15, 15) + 3] == 255 && cleared[At(15, 15) + 2] == 200 && cleared[At(20, 20) + 3] == 255 && cleared[At(20, 20)] == 255, "The character is kept, and so is the same colour inside it");
        var transparent = Picture(20, 20, (x, y) => x > 5 && x < 15 ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0));
        Check(Pixels(StageAssets.ClearBackdrop(transparent)).SequenceEqual(Pixels(transparent)), "A picture that is already transparent is left exactly as it is");
        var busy = Picture(20, 20, (x, y) => ((byte)(x * 12), (byte)(y * 12), (byte)(x * y % 255), (byte)255));
        Check(Pixels(StageAssets.ClearBackdrop(busy)).Where((value, index) => index % 4 == 3 && value == 0).Count() < 20, "A busy backdrop is not mistaken for a plain one");

        // Importing
        string art = Path.Combine(home, "art"), data = Path.Combine(home, "data"); Directory.CreateDirectory(art); Directory.CreateDirectory(data);
        string smile = Path.Combine(art, "smile.png"), room = Path.Combine(art, "room.png"), big = Path.Combine(art, "big.png"), fake = Path.Combine(art, "fake.png");
        Save(white, smile); Save(Picture(64, 36, (x, y) => ((byte)x, (byte)y, (byte)90, (byte)255)), room); Save(Picture(2400, 300, (x, y) => ((byte)200, (byte)10, (byte)10, (byte)255)), big); File.WriteAllText(fake, "not a picture");
        string kept = StageAssets.ImportSprite(smile, data, false), cut = StageAssets.ImportSprite(smile, data, true), back = StageAssets.ImportBackground(room, data), wide = StageAssets.ImportSprite(big, data, false);
        Check(new[] { kept, cut, back, wide }.All(p => File.Exists(p) && Path.GetDirectoryName(p) == Path.Combine(data, "images") && Guid.TryParseExact(Path.GetFileNameWithoutExtension(p), "N", out _)) && kept.EndsWith(".png") && back.EndsWith(".jpg"), "Pictures are copied into the images folder under names of their own; backgrounds as JPEG");
        Check(Pixels(ImageAssets.Read(kept, 40))[3] == 255 && Pixels(ImageAssets.Read(cut, 40))[3] == 0 && File.ReadAllBytes(smile).Length > 0, "The backdrop is only cleared when asked, and the original file is not touched");
        using (var stream = File.OpenRead(wide)) Check(BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0].PixelWidth == StageAssets.SpriteSide, "An oversized picture is stored no larger than a window can show");
        Check(Throws(() => StageAssets.ImportSprite(fake, data, false)) && Throws(() => StageAssets.ImportBackground(Path.Combine(art, "missing.png"), data)), "A file that is not a picture is refused");
        var pictures = new StagePictures(); var small = pictures.Get(wide, 300); var again = pictures.Get(wide, 390);
        Check(small != null && ReferenceEquals(small, again) && small!.PixelWidth == 400 && pictures.Get(wide, 900)!.PixelWidth == 1000 && pictures.Get("", 300) == null && pictures.Get(Path.Combine(art, "missing.png"), 300) == null && pictures.Get(fake, 300) == null, "A page decodes each picture once per size step, no larger than it is drawn; a missing or broken file is simply empty");

        // The saved sets and the shared backgrounds
        var book = new StageBook(data, Path.Combine(home, "no-shipped")); int changes = 0; book.Changed += () => changes++;
        book.AddSprite("a", " Smile \n", kept); book.AddSprite("a", "", cut); var first = book.AddBackdrop("내 방", "실내", back);
        Check(book.Set("a").Sprites.Select(s => s.Tag).SequenceEqual(new[] { "Smile", "기본" }) && book.State.Backdrop == back && book.CurrentTitle == "내 방" && book.Set("b").Sprites.Count == 0 && changes == 3 && book.State.AutoBackdrop, "Standing pictures are kept per character; backgrounds are shared, and the first one is put on show");
        book.AddBackdrop("교실", "학교", back + "2"); book.AddBackdrop("운동장", "학교", back + "3"); book.AddBackdrop(" ", "", back + "4");
        Check(book.State.Backdrop == back && book.Titled(" 교실 ")!.Path == back + "2" && book.Titled("없는 곳") == null && book.Titled("") == null && book.State.Backdrops[3].Title == "배경" && book.Groups.SequenceEqual(new[] { "실내", "학교", "" }), "Backgrounds are found by title and sorted into folders, with the unsorted ones last");
        for (int i = book.Set("a").Sprites.Count; i < StageBook.MaxSprites; i++) book.AddSprite("a", "t" + i, kept);
        for (int i = book.State.Backdrops.Count; i < StageBook.MaxBackdrops; i++) book.AddBackdrop("b" + i, "", back + "x" + i);
        Check(Throws(() => book.AddSprite("a", "over", kept)) && Throws(() => book.AddBackdrop("over", "", "one-more.jpg")) && book.Set("a").Sprites.Count == 40 && book.State.Backdrops.Count == 60, "There is room for 40 standing pictures per character and 60 backgrounds");
        book.Set("a").Sprites.RemoveRange(2, 38); book.State.Backdrops.RemoveRange(2, 58); book.Set("a").Sprites.Add(new StageSprite { Tag = "원격", Path = @"\\evil\share\x.png" }); book.State.Backdrops.Add(new StageBackdrop { Title = "원격", Path = "//evil/b.jpg" });
        book.State.Backdrop = "gone.jpg"; book.State.AutoBackdrop = false; book.State.FoldBackdrops = true; book.Save(); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var loaded = new StageBook(data, Path.Combine(home, "no-shipped"));
        Check(loaded.Set("a").Sprites.Count == 2 && loaded.Set("a").Sprites[0].Path == kept && loaded.State.Backdrops.Select(b => b.Title).SequenceEqual(new[] { "내 방", "교실" }) && loaded.State.Backdrops[0].Group == "실내" && loaded.State.Backdrop == "" && !loaded.State.AutoBackdrop && loaded.State.FoldBackdrops && !loaded.State.FoldSprites, "Everything is saved; network paths are dropped and a background that is no longer listed is not on show");
        Check(StageBook.Files(loaded.State).OrderBy(p => p).SequenceEqual(new[] { kept, cut, back, back + "2" }.OrderBy(p => p)) && !StageBook.Files(null).Any(), "Every file a page uses can be listed");
        foreach (string path in new[] { kept, cut, back, wide }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
        var unused = AssetCleanup.Scan(data, Array.Empty<string>(), DateTime.UtcNow).Select(f => f.Path).ToArray();
        Check(unused.SequenceEqual(new[] { wide }), "Tidying up leaves the pictures of the pages alone and finds the one nothing uses");
        // A file from before backgrounds were shared: each character had its own list
        string legacy = Path.Combine(home, "legacy"); Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "stage.json"), "{\"a\":{\"Sprites\":[{\"Tag\":\"웃음\",\"Path\":\"C:\\\\i\\\\s.png\"}],\"Backgrounds\":[\"C:\\\\i\\\\room.jpg\",\"C:\\\\i\\\\yard.jpg\"],\"Background\":\"C:\\\\i\\\\yard.jpg\"},\"b\":{\"Sprites\":[],\"Backgrounds\":[\"C:\\\\i\\\\room.jpg\",\"C:\\\\i\\\\sea.jpg\"],\"Background\":\"\"}}");
        var moved = StageBook.Load(legacy);
        Check(moved.Sets!.Count == 2 && moved.Sets["a"].Sprites.Single().Tag == "웃음" && moved.Backdrops.Select(b => b.Path).SequenceEqual(new[] { @"C:\i\room.jpg", @"C:\i\yard.jpg", @"C:\i\sea.jpg" }) && moved.Backdrops.Select(b => b.Title).SequenceEqual(new[] { "배경 1", "배경 2", "배경 3" }) && moved.Backdrop == @"C:\i\yard.jpg" && moved.Sets["a"].Backgrounds == null, "Backgrounds that belonged to single characters are gathered into the shared list, each once");
        Check(StageBook.Load(Path.Combine(home, "nothing-here")).Sets!.Count == 0, "No file means an empty book");
        string cleanedJson = SettingsBackup.DropNetworkPaths("{\"Sets\":{\"a\":{\"Sprites\":[{\"Tag\":\"x\",\"Path\":\"\\\\\\\\evil\\\\s\\\\a.png\"}]}},\"Backdrops\":[{\"Title\":\"t\",\"Path\":\"//evil/b.jpg\"},{\"Title\":\"u\",\"Path\":\"C:/b.jpg\"}],\"Backdrop\":\"//evil/b.jpg\",\"BubbleFontPath\":\"\\\\\\\\evil\\\\f.ttf\",\"a\":{\"Backgrounds\":[\"//evil/old.jpg\"]}}");
        Check(!cleanedJson.Contains("evil") && cleanedJson.Contains("C:/b.jpg"), "An imported backup cannot point a page or a font at a network location");

        // Fonts for the lines
        string fontHome = Path.Combine(home, "font-data"); Directory.CreateDirectory(fontHome); string system = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        Check(FontAssets.Load("") == null && FontAssets.Load(null) == null && FontAssets.Load(@"\\evil\f.ttf") == null && FontAssets.Load(Path.Combine(fontHome, "missing.ttf")) == null && ReferenceEquals(FontAssets.Get(""), FontAssets.Standard) && FontAssets.Name("").Contains("기본"), "Without a chosen font the standard one is used");
        string notFont = Path.Combine(fontHome, "fake.ttf"); File.WriteAllText(notFont, new string('x', 500)); string text = Path.Combine(fontHome, "note.txt"); File.WriteAllText(text, new string('x', 500));
        Check(Throws(() => FontAssets.Import(notFont, fontHome)) && Throws(() => FontAssets.Import(text, fontHome)) && Throws(() => FontAssets.Import(Path.Combine(fontHome, "missing.ttf"), fontHome)) && FontAssets.Load(notFont) == null && !Directory.Exists(Path.Combine(fontHome, "fonts")), "A file that is not a font is refused and nothing is copied");
        if (File.Exists(system))
        {
            string copied = FontAssets.Import(system, fontHome), second = FontAssets.Import(system, fontHome);
            Check(Path.GetDirectoryName(copied) == Path.Combine(fontHome, "fonts") && copied.EndsWith(".ttf") && copied != second && FontAssets.Import(copied, fontHome) == copied, "A font is copied into the settings folder; one already there is used as it is");
            var family = FontAssets.Load(copied); Check(family != null && ReferenceEquals(family, FontAssets.Load(copied)) && FontAssets.Name(copied) == "Arial" && ReferenceEquals(FontAssets.Get(copied), family), "The font is opened once and known by its name");
            Check(FontAssets.Prune(fontHome, new[] { copied }, DateTime.Now) == 0 && File.Exists(second), "A copy made a moment ago is not removed yet");
            Check(FontAssets.Prune(fontHome, new[] { copied, "" }, DateTime.Now.AddHours(2)) == 1 && File.Exists(copied) && !File.Exists(second) && FontAssets.Prune(Path.Combine(home, "nothing-here"), Array.Empty<string>(), DateTime.Now) == 0, "Copies that no setting uses are removed; the chosen one stays");
        }
        var styled = new Preferences { BubbleFontPath = @"\\evil\f.ttf", StageFontPath = @"C:\f\a.ttf", StageFontSize = 500 }; styled.Desktop.BubbleFont = 3; styled.Normalize();
        Check(styled.BubbleFontPath == "" && styled.StageFontPath == @"C:\f\a.ttf" && styled.StageFontSize == FontAssets.MaxSize && styled.Desktop.BubbleFont == FontAssets.MinSize && new Preferences().StageFontSize == 16 && new Preferences().BubbleFontPath == "", "Font choices are local files and sizes stay within range");
        // Talking on the page
        var characterPrompt = CharacterPrompt.Build(new Persona { Name = "왕", Personality = "오만하고 차갑다" });
        Check(characterPrompt.EndsWith(CharacterPrompt.OwnVoice) && characterPrompt.Contains("오만하고 차갑다") && CharacterPrompt.OwnVoice.Contains("프로필에 없는 다정함") && !CharacterPrompt.Build(new Persona(), false).Contains("[캐릭터성 우선]"), "Every character prompt ends with the rule that the profile decides the voice");

        // Prompts saved with the earlier wording are brought up to date, other edits kept
        const string oldNudge = "키보드 입력이 약 {idle_seconds}초 동안 없어서 앱이 요청한 자동 한마디입니다. 캐릭터의 성격·말투·관계에 맞게 부담 없는 재촉이나 응원 대사 1~2문장만 말하세요. 생각하거나 쉬는 중일 수 있으니 꾸짖거나 죄책감을 주지 마세요. 실제 화면이나 작업 내용을 본 것처럼 말하지 마세요. 직전 대사와 다른 표현을 쓰세요. 직전 자동 대사: {last_line}\n대사만 짧고 자연스럽게 끝까지 출력하세요.";
        const string oldRandom = "앱이 요청한 랜덤 한마디입니다. 답장을 요구하지 않는 짧은 혼잣말·소소한 이야기·다정한 응원 중 하나를 캐릭터 설정과 말투에 맞춰 1~2문장으로 표현하세요. 매번 작업을 재촉하지 말고 실제 화면이나 입력 내용을 본 것처럼 말하지 마세요. 직전 대사와 소재를 반복하지 마세요. 직전 자동 대사: {last_line}\n대사만 짧게 출력하세요.";
        var fresh = new PromptTemplates(); var renewed = new PromptTemplates { Nudge = oldNudge, Random = oldRandom, Chat = "- 내가 넣은 규칙\n- 사용자가 힘들어하면 생산성 재촉보다 공감과 편안한 휴식을 우선한다. 평소에는 가벼운 응원을 한다.", Profile = "당신은 사용자의 데스크톱 펫 '{name}'으로 대화한다. 아래 프로필을 일관되게 반영하되, 사용자의 현재 요청과 감정을 먼저 살핀다.\n[내가 넣은 줄] {extra}" }.Normalize();
        Check(renewed.Nudge == fresh.Nudge && renewed.Random == fresh.Random && renewed.Chat.StartsWith("- 내가 넣은 규칙\n- 사용자가 힘들어하면 재촉하지 않는다.") && renewed.Profile.Contains("가장 우선이며") && renewed.Profile.EndsWith("[내가 넣은 줄] {extra}"), "Saved prompts get the new wording while the user's own lines stay");
        Check(!fresh.Nudge.Contains("응원 대사") && !fresh.Random.Contains("다정한 응원") && !fresh.Chat.Contains("가벼운 응원") && fresh.Nudge.Contains("{idle_seconds}") && fresh.Random.Contains("{last_line}") && fresh.Normalize() == fresh && new PromptTemplates { Nudge = "내 재촉 프롬프트" }.Normalize().Nudge == "내 재촉 프롬프트", "The defaults no longer ask every character to be cheerful, and a prompt the user wrote is not changed");
        var duo = new DuoOptions { Prompt = "앞부분. 서로의 성격·말투에 맞는 1문장씩, 장난·응원·소소한 잡담을 주제로 해 주세요. 뒷부분." }.Normalize();
        Check(duo.Prompt.StartsWith("앞부분. ") && duo.Prompt.EndsWith(" 뒷부분.") && duo.Prompt.Contains("억지로 다정하게 만들지 마세요") && !new DuoOptions().Prompt.Contains("장난·응원"), "The pair prompt no longer asks for cheering whatever the two are like");
        Check(Enumerable.Range(1, 6).All(stage => !Affection.Tone(stage).Contains("다정") && !Affection.Tone(stage).Contains("애정")) && Affection.Context(600).Contains("성격은 바뀌지 않습니다") && !PairMood.Context(new PairMoodState { Sulking = "a", SulkingDay = "d" }, "a", "비", "d").Contains("귀엽"), "Closeness and sulking are described without prescribing a sweet tone");
        var cold = new AppInFront("x", "", "게임", "game"); var work = new AppInFront("y", "", "메모장", "work");
        Check(AppKinds.Instruction(cold).Contains("성격과 말투 그대로") && AppKinds.Instruction(work).Contains("프로필에 없는 다정함을 덧붙이지 마세요") && !AppKinds.Instruction(work).Contains("칭찬하거나 격려해 주세요") && DialogueModes.Rules["응원"].Contains("퉁명스러운 캐릭터는 퉁명스럽게"), "Remarks about programs and the cheering mode follow the character's own manner");
        // Backgrounds that come with the program
        string shipped = Path.Combine(home, "shipped"); Directory.CreateDirectory(Path.Combine(shipped, "거리")); Directory.CreateDirectory(Path.Combine(home, "own"));
        File.Copy(back, Path.Combine(shipped, "city_morning.jpg")); File.Copy(back, Path.Combine(shipped, "거리", "street_night.JPG")); File.Copy(kept, Path.Combine(shipped, "거리", "비 오는 골목-2.png")); File.WriteAllText(Path.Combine(shipped, "readme.txt"), "not a picture");
        var builtIn = StageBook.ReadBuiltIn(shipped);
        Check(builtIn.Select(b => b.Title).OrderBy(n => n).SequenceEqual(new[] { "city morning", "street night", "비 오는 골목" }.OrderBy(n => n)) && builtIn.Single(b => b.Title == "city morning").Group == StageBook.BuiltInGroup && builtIn.Where(b => b.Title != "city morning").All(b => b.Group == "거리") && StageBook.ReadBuiltIn(Path.Combine(home, "nothing-here")).Length == 0, "Pictures in the program's backgrounds folder are titled by their file names and grouped by their folders; other files are ignored");
        var withShipped = new StageBook(Path.Combine(home, "own"), shipped); var mineOnly = withShipped.AddBackdrop("내 방", StageBook.BuiltInGroup, back);
        Check(withShipped.Backdrops.Count == 4 && withShipped.State.Backdrops.Count == 1 && withShipped.Backdrops[0].Title == "내 방" && mineOnly.Group == "" && withShipped.Titled("STREET NIGHT")!.Path.EndsWith("street_night.JPG") && withShipped.Groups.Contains("거리") && withShipped.Groups.Contains(StageBook.BuiltInGroup) && StageBook.IsBuiltIn(withShipped.Titled("city morning")!) && !StageBook.IsBuiltIn(mineOnly), "They are offered beside the user's own backgrounds, found by title like any other, and kept apart from them");
        withShipped.State.Backdrop = withShipped.Titled("street night")!.Path; withShipped.Save(); OrderedStore.FlushAsync().GetAwaiter().GetResult();
        var shippedAgain = new StageBook(Path.Combine(home, "own"), shipped);
        Check(shippedAgain.CurrentTitle == "street night" && shippedAgain.State.Backdrops.Single().Title == "내 방" && !StageBook.Files(shippedAgain.State).Any(p => p.Contains("shipped")), "A built-in background can be the one on show; only the user's own are saved in the file");
        Check(new StageBook(Path.Combine(home, "own"), Path.Combine(home, "nothing-here")).State.Backdrop == "" && StageBook.BuiltIn.Count == 0, "Without the folder there are none, and a choice that pointed at one is cleared");
        return count;
    }
}
