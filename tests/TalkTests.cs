using System;
using System.IO;

namespace KeyboardWorker;

public static class TalkTests
{
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        // Expressions
        Check(PetMood.Parse("감히 나를 깨우다니!\n[pet-mood:ANGRY]") == new PetReply("감히 나를 깨우다니!", "angry"), "Expression tag is read whatever its letter case");
        Check(PetMood.Normalize(" Shy ") == "shy" && PetMood.Normalize("grumpy") == "neutral" && PetMood.Normalize(null) == "neutral", "Unknown expressions fall back to neutral");
        Check(PetMood.Guess("감히 이 몸을 기다리게 하다니, 괘씸하군.") == "angry" && PetMood.Guess("무리하지 말고 조금 쉬어.") == "caring" && PetMood.Guess("후후, 훌륭하군!") == "happy" && PetMood.Guess("오늘 날씨는 흐림.") == "neutral", "Expression is guessed from the line when the model gives none");
        foreach (string mood in PetMood.Moods) Check(PetMood.Instruction.Contains(mood) && PetMood.PairInstruction.Contains(mood) && PetMood.LineInstruction.Contains(mood), "Every expression is explained to the model: " + mood);
        var pair = DuoInteraction.Parse("{\"first\":\"흥, 시끄럽다.\",\"first_mood\":\"angry\",\"second\":\"후후, 기분 좋아 보이네.\",\"second_mood\":\"Happy\"}");
        Check(pair.FirstMood == "angry" && pair.SecondMood == "happy", "Each pet gets its own expression from a pair reply");
        var plain = DuoInteraction.Parse("{\"first\":\"무리하지 마.\",\"second\":\"깜짝이야!\",\"second_mood\":\"confused\"}");
        Check(plain.FirstMood == "caring" && plain.SecondMood == "surprised", "Missing or unknown pair expressions are guessed from each line");
        Check(plain == new DuoReply("무리하지 마.", "깜짝이야!"), "Pair replies compare by their lines");

        // Automatic line schedule
        var options = new AutomaticOptions { RandomLines = true, RandomMinMinutes = 1, RandomMaxMinutes = 1, CooldownSeconds = 120, HourlyLimit = 2 }; var clock = new AutomaticClock((min, _) => min);
        Check(clock.Outlook(options, 0) == "대기 중", "Outlook before the schedule starts");
        clock.Observe(options, 0, true); Check(clock.Outlook(options, 0) == "약 61초 후" && !clock.RandomReady(59999) && clock.RandomReady(60000), "Random line becomes due after its interval");
        Check(clock.Outlook(options, 60000) == "곧" && clock.Reserve(false, options, 60000), "A due random line can be requested");
        Check(clock.Outlook(options, 61000) == "약 2분 후", "Next line waits for the interval and the cooldown");
        Check(!clock.Reserve(false, options, 120000) && clock.Reserve(false, options, 180000), "Cooldown is respected between automatic requests");
        Check(clock.Outlook(options, 181000).Contains("한도"), "Hourly limit is explained");
        clock.Observe(options, 200000, false); Check(clock.Outlook(options, 200000) == "대기 중" && !clock.RandomReady(999999), "Nothing is due while automatic lines are paused");

        // Story quest milestone
        var book = new AdventureBook(Path.Combine(root, "talk-book")); int reached = 0; book.QuestReached += _ => reached++;
        book.AddQuest(new StoryQuest { CharacterId = "hero", Title = "작은 모험", Minutes = 5 });
        book.Work("hero", "Hero", 4 * 60000); Check(reached == 0, "Quest goal is not announced early");
        book.Work("hero", "Hero", 60000); book.Work("hero", "Hero", 60000); Check(reached == 1, "Reaching the quest goal is announced exactly once");
        return count;
    }
}
