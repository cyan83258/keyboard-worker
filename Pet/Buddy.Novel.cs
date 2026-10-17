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
    // The chat settings as the novel uses them: its own connection, limits, parameters and streaming.
    public ChatPreferences NovelSettings()
    {
        var book = Novel.State; var settings = ChatRepository.Clone(Chat.Preferences);
        int named = book.Connection.Length == 0 ? -1 : settings.Connections.FindIndex(c => string.Equals(c.Name, book.Connection, StringComparison.OrdinalIgnoreCase)); if (named >= 0) settings.Selected = named;
        settings.Connections[settings.Selected].Parameters = book.Parameters.Snapshot();
        settings.MaxOutputTokens = book.MaxOutput; settings.MaxContextTokens = book.MaxContext; settings.Streaming = book.Stream; settings.UsageKind = "Novel";
        settings.RequestTimeoutSeconds = Math.Max(settings.RequestTimeoutSeconds, 300); return settings;
    }
    // The scene of a request: the cast, the user, the room's note and memory, the story so far and what to write now.
    public NovelScene NovelScene(StageCharacter[] cast, string thread, StageRoom room, string direction) =>
        new(cast.Select(c => c.Persona).ToArray(), Chat.Preferences.Profile.UserName ?? "", Chat.Preferences.Profile.UserNotes ?? "", room.Note, room.Summary, StageTalks.Turns(thread).ToArray(), direction);
    // The next part of the story in the room open on this page. Returns the part as it was kept.
    public async Task<ChatTurn> WriteNovel(string page, StageCharacter[] cast, string direction, CancellationToken cancellation, Action<string>? progress, Action<string>? thinking)
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        CancelAutomatic(); sending = true; using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = request;
        try
        {
            var book = Novel.State; var room = Rooms.Current(page); string thread = StageRooms.Thread(page, room); string thought = "";
            var settings = NovelSettings(); var messages = NovelPrompts.Build(book.Prompts, NovelScene(cast, thread, room, direction), book.MaxContext, book.MaxOutput);
            string text = await new AiClient().Compose(settings, messages, request.Token, settings.Streaming ? progress : null, book.ShowThinking ? t => { thought = t; thinking?.Invoke(t); } : null);
            request.Token.ThrowIfCancellationRequested();
            // The cast is named with " · " so that the memory reads the part as a story, not as one character's line.
            var turn = new ChatTurn(direction.Trim(), text.Trim(), DateTimeOffset.Now, string.Join(" · ", cast.Select(c => c.Name)) + " · 소설", thread, "소설");
            StageTalks.Limit = book.KeepTurns; StageTalks.Add(thread, turn); turn = StageTalks.Turns(thread)[^1];
            if (thought.Length > 0) Novel.Keep(thread, turn, thought);
            if (!closing) RememberQuietly(thread, room);
            return turn;
        }
        finally { activeChatCancellation = null; sending = false; }
    }
}
