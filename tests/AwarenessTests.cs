using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class AwarenessTests
{
    sealed class Lines : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; string json = "{\\\"lines\\\":[\\\"하나 " + Calls + "\\\",\\\"둘 " + Calls + "\\\",\\\"셋 " + Calls + "\\\"]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"" + json + "\"}]}}]}") });
        }
    }
    public static int Run()
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var options = new AwarenessOptions();
        Check(AppKinds.Classify("Code", @"C:\Users\me\AppData\Local\Programs\Microsoft VS Code\Code.exe", options) == "work" && AppKinds.Classify("WINWORD.EXE", "", options) == "work", "Editors and office programs count as work");
        Check(AppKinds.Classify("VALORANT-Win64-Shipping", "", options) == "game" && AppKinds.Classify("Whatever", @"D:\SteamLibrary\steamapps\common\Some Game\Whatever.exe", options) == "game", "Known games and programs in a game library count as games");
        Check(AppKinds.Classify("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", options) == "other" && AppKinds.Classify("explorer", @"C:\Windows\explorer.exe", options) == "other" && AppKinds.Classify("", "", options) == "other", "Browsers, the shell and unknown programs are left alone");
        Check(AppKinds.Classify("steam", @"C:\Program Files (x86)\Steam\steam.exe", options) == "other", "A store launcher by itself is not a game");
        var mine = options with { Games = "chrome.exe, mytool", Work = "valorant-win64-shipping" };
        Check(AppKinds.Classify("chrome", "", mine) == "game" && AppKinds.Classify("MyTool", "", mine) == "game" && AppKinds.Classify("VALORANT-Win64-Shipping", "", mine) == "work", "The user's own lists win over the built-in ones");
        Check(AppKinds.Classify("codex", "", options) == "other" && AppKinds.Classify("wowhelper", "", options) == "other", "Short names only match exactly");
        var normal = new AwarenessOptions { CommentMinutes = 1, Games = null!, Work = new string('x', 5000) }.Normalize();
        Check(normal.CommentMinutes == 5 && normal.Games == "" && normal.Work.Length == 2000 && new AwarenessOptions().GameLight && new Preferences().Awareness.Apps, "Awareness options are bounded and on by default");
        var game = new AppInFront("eldenring", @"D:\Games\ELDEN RING\eldenring.exe", "ELDEN RING", "game"); var editor = new AppInFront("Code", "", "Visual Studio Code", "work");
        Check(AppKinds.LocalLine(game, 0).Contains("ELDEN RING") && !AppKinds.LocalLine(game, 3).Contains("{app}") && AppKinds.LocalLine(editor, 0).Contains("Visual Studio Code"), "Ready-made lines name the program");
        Check(AppKinds.Instruction(game).Contains("핀잔") && AppKinds.Instruction(editor).Contains("격려") && AppKinds.Instruction(game).Contains("{last_line}") && AppKinds.Context(editor).Contains("Visual Studio Code") && AppKinds.Context(new AppInFront()) == "", "AI instructions tease about games and praise work");

        var video = new SongState("고양이 브이로그", "냥이 채널", true, "Chrome", false, true);
        var song = new SongState("별빛 산책", "가수A", false, "Chrome", true);
        var other = new SongState("어떤 강의", "", true, "MSEdge");
        Check(MusicMonitor.Kind(video) == "유튜브 영상" && MusicMonitor.Kind(song) == "유튜브 뮤직 노래" && MusicMonitor.Kind(other) == "브라우저에서 재생 중인 미디어", "Playing media is named by where it comes from");
        Check(MusicMonitor.Context(video).Contains("고양이 브이로그") && MusicMonitor.Context(video).Contains("냥이 채널") && MusicMonitor.Context(video).Contains("재생 중") && MusicMonitor.Context(song).Contains("일시정지") && MusicMonitor.Context(new SongState()) == "", "Chat is told the title, channel and state of what is playing");
        Check(MusicMonitor.Instruction(video).Contains("고양이 브이로그") && MusicMonitor.Instruction(video).Contains("{last_line}") && !MusicMonitor.Context(other).Contains("채널·가수"), "Unprompted media lines get the title; a missing channel is left out");

        Check(new DuoOptions().ShareLines, "Two pets share nudges and random lines by default");
        var chat = new ChatPreferences(); chat.Connections[0].Model = "test-model"; chat.Automatic = chat.Automatic with { Batch = true, BatchSize = 3 };
        var handler = new Lines(); using var http = new HttpClient(handler); var client = new AiClient(http); var batch = new AutomaticBatch();
        string first = batch.Get(chat, Array.Empty<ChatTurn>(), false, 0, "", CancellationToken.None, client, speaker: "").GetAwaiter().GetResult().Text;
        string partner = batch.Get(chat, Array.Empty<ChatTurn>(), false, 0, "", CancellationToken.None, client, speaker: "second").GetAwaiter().GetResult().Text;
        string again = batch.Get(chat, Array.Empty<ChatTurn>(), false, 0, "", CancellationToken.None, client, speaker: "").GetAwaiter().GetResult().Text;
        Check(first == "하나 1" && partner == "하나 2" && again == "둘 1" && handler.Calls == 2, "Each pet keeps its own prepared lines, so taking turns does not waste requests");
        return count;
    }
}
