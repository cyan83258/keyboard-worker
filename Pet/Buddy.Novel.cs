using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace KeyboardWorker;

// The novel: stories with one or two of the characters, written by the AI part by part. It has its own rooms (with
// author's note and memory, like the pages), its own prompt set (SillyTavern presets can be read in) and its own AI settings.
public sealed partial class Buddy
{
    NovelBook? novel; Window? novelWindow;
    public NovelBook Novel => novel ??= new NovelBook();
    public void OpenNovel(string? id = null) { Open(ref novelWindow, () => new NovelWindow(this), w => novelWindow = w); if (id != null) (novelWindow as NovelWindow)?.Select(id); }
    // The chat settings as the novel uses them: its own connection (and model), limits, parameters and streaming.
    public ChatPreferences NovelSettings()
    {
        var book = Novel.State; var settings = ChatRepository.Clone(Chat.Preferences);
        int named = book.Connection.Length == 0 ? -1 : settings.Connections.FindIndex(c => string.Equals(c.Name, book.Connection, StringComparison.OrdinalIgnoreCase)); if (named >= 0) settings.Selected = named;
        var connection = settings.Connections[settings.Selected]; connection.Parameters = book.Parameters.Snapshot(); if (book.Model.Length > 0) connection.Model = book.Model;
        settings.MaxOutputTokens = book.MaxOutput; settings.MaxContextTokens = book.MaxContext; settings.Streaming = book.Stream; settings.UsageKind = "Novel";
        settings.RequestTimeoutSeconds = Math.Max(settings.RequestTimeoutSeconds, 300); return settings;
    }
    // The scene of a request: the cast, the user, the room's note and memory, the story so far (summarized parts as their
    // summaries, without the parts the user closed the eye on) and what to write now.
    public NovelScene NovelScene(StageCharacter[] cast, string thread, StageRoom room, string direction)
    {
        var (digest, story) = NovelSummaries.Apply(Novel.State.Summary, StageTalks.Turns(thread), Novel.SummariesOf(thread));
        return new(cast.Select(c => c.Persona).ToArray(), Chat.Preferences.Profile.UserName ?? "", Chat.Preferences.Profile.UserNotes ?? "", room.Note, room.Summary, story.Where(t => !Novel.IsHidden(thread, t)).ToArray(), direction, digest);
    }
    // Which parts (places in the story, from 0) the next request sends as they are, which as summaries, and which not at all.
    public (int[] Sent, int[] Summarized, int[] Hidden, int[] Left) NovelSent(StageCharacter[] cast, string page)
    {
        var room = Rooms.Current(page); string thread = StageRooms.Thread(page, room); var all = StageTalks.Turns(thread); var book = Novel.State;
        var scene = NovelScene(cast, thread, room, ""); var sent = scene.Story.TakeLast(NovelPrompts.Kept(book.Prompts, scene, book.MaxContext, book.MaxOutput)).Select(t => t.At).ToHashSet();
        var summaries = Novel.SummariesOf(thread); bool digest = scene.Digest.Length > 0;
        int[] Where(Func<ChatTurn, bool> test) => Enumerable.Range(0, all.Count).Where(i => test(all[i])).ToArray();
        var hidden = Where(t => Novel.IsHidden(thread, t)); var shown = Where(t => sent.Contains(t.At));
        var summarized = digest ? Where(t => !sent.Contains(t.At) && summaries.Any(s => t.At.UtcTicks >= s.From && t.At.UtcTicks <= s.To)) : Array.Empty<int>();
        return (shown, summarized, hidden, Enumerable.Range(0, all.Count).Except(shown).Except(summarized).Except(hidden).ToArray());
    }
    // ---- summaries ----
    // The settings a summary is asked with: the summary's own connection, else the novel's; the connection's own parameters
    // (a story's high temperature does not suit a summary), no streaming.
    ChatPreferences SummarySettings()
    {
        var book = Novel.State; var settings = ChatRepository.Clone(Chat.Preferences); string name = book.Summary.Connection.Length > 0 ? book.Summary.Connection : book.Connection;
        int named = name.Length == 0 ? -1 : settings.Connections.FindIndex(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)); if (named >= 0) settings.Selected = named;
        if (book.Summary.Connection.Length == 0 && book.Model.Length > 0) settings.Connections[settings.Selected].Model = book.Model;
        settings.MaxOutputTokens = Math.Max(settings.MaxOutputTokens, 4000); settings.Streaming = false; settings.UsageKind = "NovelSummary"; settings.RequestTimeoutSeconds = Math.Max(settings.RequestTimeoutSeconds, 300); return settings;
    }
    readonly System.Collections.Generic.HashSet<string> summarizing = new();
    // Summarizes the given parts (places in the story), a run at a time. progress: "3 / 5". Returns how many summaries were written.
    public async Task<int> SummarizeNovel(string thread, StageCharacter[] cast, System.Collections.Generic.IEnumerable<int> places, CancellationToken cancellation, Action<string>? progress = null)
    {
        if (!summarizing.Add(thread)) throw new InvalidOperationException("이미 요약하는 중이에요.");
        try
        {
            var options = Novel.State.Summary; var story = StageTalks.Turns(thread).ToArray(); var runs = NovelSummaries.Runs(places.Where(i => i >= 0 && i < story.Length), options.Group); int done = 0; var client = new AiClient();
            foreach (var run in runs)
            {
                progress?.Invoke($"{done + 1} / {runs.Count}");
                var (system, text) = NovelSummaries.Request(options, story, run, Novel.SummariesOf(thread), string.Join(", ", cast.Select(c => c.Name)));
                string answer = await client.Compose(SummarySettings(), new[] { new PromptMessage(PromptMessage.System, system), new PromptMessage(PromptMessage.User, text) }, cancellation);
                cancellation.ThrowIfCancellationRequested();
                Novel.Keep(thread, new NovelSummary(story[run[0]].At.UtcTicks, story[run[^1]].At.UtcTicks, NovelSummaries.Clean(answer), DateTimeOffset.Now)); done++;
            }
            return done;
        }
        finally { summarizing.Remove(thread); }
    }
    // After a part is written: the waiting parts are summarized in the background when automatic summaries are on and enough wait.
    // A failure is not shown; they are tried again after the next part.
    async void SummarizeQuietly(string thread, StageCharacter[] cast)
    {
        var options = Novel.State.Summary; var story = StageTalks.Turns(thread); var summaries = Novel.SummariesOf(thread);
        if (!NovelSummaries.Due(options, story, summaries) || summarizing.Contains(thread)) return;
        try { await SummarizeNovel(thread, cast, NovelSummaries.Ready(options, story, summaries), CancellationToken.None); } catch (Exception) { }
    }
    // The messages of one request, shaped as the API settings say.
    public System.Collections.Generic.List<PromptMessage> NovelMessages(NovelScene scene)
    {
        var book = Novel.State; return NovelPrompts.Shape(NovelPrompts.Build(book.Prompts, scene, book.MaxContext, book.MaxOutput), book.SystemPrompt, book.Squash, book.MergeRoles);
    }
    // The next part of the story in the room open on this page, written to the direction (kept with it, as its input).
    // Returns the part as it was kept.
    public async Task<ChatTurn> WriteNovel(string page, StageCharacter[] cast, string direction, CancellationToken cancellation, Action<string>? progress, Action<string>? thinking)
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        CancelAutomatic(); sending = true; using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = request;
        try
        {
            var book = Novel.State; var room = Rooms.Current(page); string thread = StageRooms.Thread(page, room); string thought = "";
            var settings = NovelSettings(); var messages = NovelMessages(NovelScene(cast, thread, room, direction));
            string text = await new AiClient().Compose(settings, messages, request.Token, settings.Streaming ? progress : null, book.ShowThinking ? t => { thought = t; thinking?.Invoke(t); } : null);
            request.Token.ThrowIfCancellationRequested();
            // The cast is named with " · " so that the memory reads the part as a story, not as one character's line.
            var turn = new ChatTurn(direction.Trim(), text.Trim(), DateTimeOffset.Now, string.Join(" · ", cast.Select(c => c.Name)) + " · 소설", thread, "소설");
            StageTalks.Limit = book.KeepTurns; StageTalks.Add(thread, turn); turn = StageTalks.Turns(thread)[^1];
            if (thought.Length > 0) Novel.Keep(thread, turn, thought);
            if (!closing) SummarizeQuietly(thread, cast);
            return turn;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
}