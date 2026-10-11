using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace KeyboardWorker;

// AI requests: story quests and chat messages.
public sealed partial class Buddy
{
    public async System.Threading.Tasks.Task GenerateAdventure(bool force)
    {
        if (Auto.Busy || IsChatBusy) { if (force) throw new InvalidOperationException("진행 중인 AI 답변이 끝난 뒤 다시 시도해 주세요."); return; }
        var source = Auto.Begin("이야기를 쓰는 중…");
        try
        {
            var quest = Adventure.State.Quests.FirstOrDefault(q => q.Ready && q.StoryId.Length == 0); var preferences = Chat.Preferences.Snapshot();
            if (quest != null)
            {
                var preset = Presets.Items.FirstOrDefault(p => p.Id == quest.CharacterId); if (preset != null) preferences.Profile = preset.Profile.Snapshot();
                var story = await AdventureAi.Story(preferences, quest.Snapshot(), Adventure.State.Options, source.Token, automatic: !force);
                source.Token.ThrowIfCancellationRequested(); Adventure.AddStory(quest, story.Title, story.Body, Game, Random.Shared.Next(100)); Auto.Status = "새 이야기가 이야기책에 도착했어요 ♡";
                var arrived = Adventure.State.Stories.LastOrDefault();
                if (arrived != null) Celebrate("새 이야기 도착", arrived.Title, arrived.Loot.Length > 0 ? "이야기책에 담았어요 · 보상: " + arrived.Loot : "이야기책에 담았어요. 지금 읽어 볼까요?", "이야기 읽기", 3);
            }
            else
            {
                if (Adventure.State.Quests.Any(q => q.StoryId.Length == 0)) { if (force) throw new InvalidOperationException("진행 중인 퀘스트의 작업 시간을 먼저 채워 주세요."); return; }
                var generated = await AdventureAi.Quest(preferences, CharacterId, Adventure.State.Options, source.Token, automatic: !force);
                source.Token.ThrowIfCancellationRequested();
                Adventure.AddQuest(generated);
                Auto.Status = "새 이야기 퀘스트를 받았어요 ♡";
                Celebrate("새 퀘스트", generated.Title, $"키보드 작업 {generated.Minutes}분을 채우면 이야기가 완성돼요.", "퀘스트 보기", 2);
            }
            Adventure.State.LastAi = DateTimeOffset.Now; Adventure.Save(true);
        }
        catch (OperationCanceledException) { if (force) throw; }
        catch (Exception e) { Auto.Failed("이야기 생성", e); if (force) throw; }
        finally { Auto.End(source); }
    }
    // media: true / false = the sender chose to tell (or not tell) what is playing; null = follow the setting.
    public async System.Threading.Tasks.Task<ChatTurn> SendMessage(string text, bool quick, CancellationToken cancellation, bool? media = null)
    {
        if (IsChatBusy) throw new InvalidOperationException("이전 답변을 기다리거나 먼저 생성을 중단해 주세요.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 6000) throw new InvalidOperationException("메시지는 1~6000자로 입력해 주세요.");
        string target = Chat.Preferences.Target; var partner = companion;
        if (target != "primary" && partner == null) throw new InvalidOperationException("둘째 친구를 먼저 불러내 주세요.");
        CancelAutomatic();
        sending = true; using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation); activeChatCancellation = requestCancellation;
        CurrentMessageUser = text; BeginChatThinking(); MessageStarted?.Invoke(text);
        if (target != "primary") partner?.BeginThinking();
        string? partialShown = null; bool streaming = Chat.Preferences.Streaming;
        using var pump = new StreamPump(partial =>
        {
            if (closing) return; partialShown = partial; MessageProgress?.Invoke(partial);
            if ((quick || Chat.Preferences.PetBubble) && !hidden) { if (target == "secondary") partner?.Progress(partial); else bubble.UpdateStreaming(partial); }
        }, streaming);
        try
        {
            string around = await Surroundings(media) + Auto.RecentPair;
            if (target == "both")
            {
                var settings = Chat.Preferences.Snapshot(); settings.Profile.Extra += "\n" + DialogueModes.Rules[settings.Mode] + Adventure.Context(CharacterId, settings.Profile.Name) + Feelings(false) + around;
                var second = partner!.Preset.Profile.Snapshot(); second.Extra += "\n" + DialogueModes.Rules[settings.Mode] + Adventure.Context(partner.Preset.Id, second.Name) + Feelings(true) + around;
                var scene = Pref.Duo with { Prompt = "사용자가 두 친구에게 말했습니다: " + text + "\n각 캐릭터가 자신의 관점으로 자연스럽게 답하세요. 대화 모드: " + settings.Mode + Adventure.Pair(CharacterId, partner.Preset.Id).Context(settings.Profile.Name, second.Name) + "\nJSON만 반환: {\"first\":\"대표 친구 답변\",\"second\":\"둘째 친구 답변\"}" };
                var pairReply = await DuoInteraction.Generate(settings, second, scene, "", Chat.Context("both"), requestCancellation.Token, usageKind: "ChatBoth");
                requestCancellation.Token.ThrowIfCancellationRequested();
                var turn = new ChatTurn(text.Trim(), settings.Profile.Name + ": " + pairReply.First + "\n\n" + second.Name + ": " + pairReply.Second, DateTimeOffset.Now, settings.Profile.Name + " + " + second.Name, "both", settings.Mode);
                Chat.Add(turn); Adventure.TalkPair(CharacterId, partner.Preset.Id); MessageReceived?.Invoke(turn);
                // Each pet shows the expression chosen for its own line.
                EndChatThinking(); partner.EndThinking(); ChatMood(pairReply.FirstMood); partner.Express(pairReply.SecondMood);
                if (quick || settings.PetBubble) { Say(pairReply.First); partner.Say(pairReply.Second); PlayVoice(false, "speak", pairReply.FirstMood, pairReply.First); }
                NoteChat(target); return turn;
            }
            var persona = target == "secondary" ? partner!.Preset.Profile : Chat.Preferences.Profile;
            string id = target == "secondary" ? partner!.Preset.Id : CharacterId;
            var result = await conversation.Send(text, quick, Pref.AdvancedImages, requestCancellation.Token, streaming ? pump.Report : null, Game.ChatContext() + Adventure.Context(id, persona.Name) + Feelings(target == "secondary") + around, target == "secondary" ? persona : null, target + ":" + id);
            pump.Flush();
            if (closing) return result.Turn;
            EndChatThinking(); if (target == "secondary") partner?.Express(result.Mood); else ChatMood(result.Mood); MessageReceived?.Invoke(result.Turn);
            if (quick || Chat.Preferences.PetBubble) { if (target == "secondary") partner?.Say(result.Turn.Assistant); else Say(result.Turn.Assistant); PlayVoice(target == "secondary", "speak", result.Mood, result.Turn.Assistant); }
            NoteChat(target); return result.Turn;
        }
        catch
        {
            pump.Flush(); MessageFailed?.Invoke("생성 중단 또는 수신 실패 · 대화 기록에 저장하지 않았어요.");
            if (partialShown != null && bubble.IsVisible && bubble.IsReply) bubble.ShowText(partialShown + "\n(미완성 · 저장하지 않음)", true, Pref.Desktop.ReplySeconds);
            throw;
        }
        finally { activeChatCancellation = null; sending = false; CurrentMessageUser = ""; EndChatThinking(); partner?.EndThinking(); }
    }
}
