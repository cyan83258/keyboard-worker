using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace KeyboardWorker;

// A goal for the week that the user writes, edits and deletes. Amount is counted in the kind's own unit (milliseconds for work).
public sealed class WeeklyGoal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Kind { get; set; } = "work";
    public int Target { get; set; } = 1;
    public long Amount { get; set; }
    public bool Done { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public long Value => Kind == "work" ? Amount / 60000 : Amount;
    public WeeklyGoal Snapshot() => (WeeklyGoal)MemberwiseClone();
}
public static class GoalKinds
{
    public static readonly (string Key, string Title, string Unit, int Low, int High, int Coins)[] All =
    {
        ("work", "키보드 작업 시간", "분", 10, 6000, 40), ("days", "출석한 날", "일", 1, 7, 30), ("chats", "캐릭터와 대화", "번", 1, 500, 20),
        ("pats", "쓰다듬기", "번", 1, 70, 15), ("events", "미니 이벤트 성공", "번", 1, 21, 30), ("manual", "직접 체크 (내가 센다)", "번", 1, 100, 10)
    };
    public static (string Key, string Title, string Unit, int Low, int High, int Coins) Find(string key) => All.FirstOrDefault(k => k.Key == key) is { Key: not null } kind ? kind : All[0];
}

// A short challenge that appears while working: type for so long before the time is up.
public sealed class MiniEvent
{
    public string Kind { get; set; } = "";
    public string Title { get; set; } = "";
    public long TargetMs { get; set; }
    public long ProgressMs { get; set; }
    public DateTimeOffset Ends { get; set; }
    public int Coins { get; set; }
    public MiniEvent Snapshot() => (MiniEvent)MemberwiseClone();
}
public sealed record Achievement(string Id, string Title, string Description, string Fact, long Target, int Coins);

public sealed class ProgressState
{
    // attendance
    public string LastDay { get; set; } = "";
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public int TotalDays { get; set; }
    // the week (named by its Monday)
    public string Week { get; set; } = "";
    public int WeekDays { get; set; }
    public List<WeeklyGoal> Goals { get; set; } = new();
    public int ManualRewards { get; set; }
    // lifetime counters: pats, chats, gifts, events, goals, todos
    public Dictionary<string, long> Counters { get; set; } = new();
    public Dictionary<string, string> Achievements { get; set; } = new();   // id -> day reached
    public bool Seeded { get; set; }                                         // what was already earned before achievements existed has been counted
    public HashSet<string> Unlocked { get; set; } = new();                   // bought with coins: "pack:…", "scene:…"
    // mini events
    public bool Events { get; set; } = true;
    public MiniEvent? Event { get; set; }
    public long SinceEventMs { get; set; }
    public long NextEventMs { get; set; } = 45 * 60000;
    public string EventDay { get; set; } = "";
    public int EventsToday { get; set; }
    public ProgressState Snapshot()
    {
        var copy = (ProgressState)MemberwiseClone(); copy.Goals = Goals.Select(g => g.Snapshot()).ToList(); copy.Counters = new(Counters); copy.Achievements = new(Achievements); copy.Unlocked = new(Unlocked); copy.Event = Event?.Snapshot(); return copy;
    }
}
// Something worth telling the user: a day checked in, a goal met, an event won, an achievement.
public sealed record ProgressNews(string Kind, string Title, string Detail, int Coins, bool Quiet = false);

// Attendance, weekly goals, mini events and achievements (progress.json). It only keeps count; coins are handed out by the caller.
public sealed class ProgressBook
{
    public const int MaxGoals = 8, EventsPerDay = 3, ManualRewardsPerWeek = 3;
    static readonly ProgressNews[] nothing = Array.Empty<ProgressNews>();
    public static readonly Achievement[] All =
    {
        new("work-1", "첫 한 시간", "함께 1시간 작업", "work", 60, 10), new("work-10", "열 시간의 동행", "함께 10시간 작업", "work", 600, 30), new("work-50", "쉰 시간의 동행", "함께 50시간 작업", "work", 3000, 60), new("work-100", "백 시간의 동행", "함께 100시간 작업", "work", 6000, 100),
        new("days-7", "일주일의 얼굴", "출석 7일", "days", 7, 15), new("days-30", "한 달의 얼굴", "출석 30일", "days", 30, 40), new("days-100", "백 일의 얼굴", "출석 100일", "days", 100, 100),
        new("streak-3", "사흘 연속", "3일 연속 출석", "streak", 3, 10), new("streak-7", "일주일 연속", "7일 연속 출석", "streak", 7, 30), new("streak-30", "한 달 연속", "30일 연속 출석", "streak", 30, 100),
        new("pats-50", "다정한 손길", "쓰다듬기 50번", "pats", 50, 15), new("pats-300", "쓰다듬기 장인", "쓰다듬기 300번", "pats", 300, 50),
        new("chats-10", "말문이 트이다", "대화 10번", "chats", 10, 10), new("chats-100", "수다 친구", "대화 100번", "chats", 100, 30), new("chats-500", "못다 한 이야기", "대화 500번", "chats", 500, 80),
        new("gifts-10", "선물 보따리", "선물 10번", "gifts", 10, 20), new("kept-10", "마음에 남은 말", "대사 10개 도감에 담기", "kept", 10, 15), new("kept-50", "대사 수집가", "대사 50개 도감에 담기", "kept", 50, 40),
        new("stories-1", "첫 이야기", "이야기책에 이야기 1편", "stories", 1, 15), new("stories-10", "이야기꾼의 책장", "이야기책에 이야기 10편", "stories", 10, 50),
        new("stage-3", "친구가 되다", "친밀도 3단계", "stage", 3, 20), new("stage-5", "단짝이 되다", "친밀도 5단계", "stage", 5, 50), new("stage-6", "소중한 사람", "친밀도 6단계", "stage", 6, 100),
        new("pair-10", "둘의 수다", "두 친구의 대화 10번", "pair", 10, 15), new("pair-100", "끝없는 수다", "두 친구의 대화 100번", "pair", 100, 50),
        new("goals-1", "첫 주간 목표", "주간 목표 1개 달성", "goals", 1, 10), new("goals-10", "계획의 달인", "주간 목표 10개 달성", "goals", 10, 40), new("goals-30", "꾸준함의 증명", "주간 목표 30개 달성", "goals", 30, 80),
        new("events-1", "반짝 도전", "미니 이벤트 1번 성공", "events", 1, 10), new("events-10", "도전을 즐기는 자", "미니 이벤트 10번 성공", "events", 10, 40), new("events-30", "이벤트 사냥꾼", "미니 이벤트 30번 성공", "events", 30, 80),
        new("level-5", "쑥쑥 자라는 중", "레벨 5", "level", 5, 20), new("level-10", "믿음직한 친구", "레벨 10", "level", 10, 50), new("level-20", "오랜 동료", "레벨 20", "level", 20, 100),
        new("todos-1", "한 줄 완료", "할 일 메모 1번 완료", "todos", 1, 5), new("todos-30", "해내는 사람", "할 일 메모 30번 완료", "todos", 30, 40),
        new("packs-1", "새 말투 장만", "대사 팩 1개 구매", "packs", 1, 10), new("scenes-1", "둘만의 한 컷", "2인 장면 1개 등록", "scenes", 1, 15), new("days-anniversary", "기억해 둘 날", "기념일 1개 등록", "anniversaries", 1, 10)
    };
    static readonly int[] streakMarks = { 3, 7, 14, 30, 50, 100, 200, 365 };

    readonly string path; bool dirty;
    public ProgressState State { get; }
    public event Action? Changed;
    public ProgressBook(string? root = null)
    {
        path = Path.Combine(root ?? Storage.Root, "progress.json");
        State = Storage.ReadJson<ProgressState>(path, 2 * 1024 * 1024) ?? new();
        State.Goals = (State.Goals ?? new()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Title)).Take(MaxGoals).ToList(); State.Counters ??= new(); State.Achievements ??= new(); State.Unlocked ??= new();
        foreach (var goal in State.Goals) Fit(goal);
        State.NextEventMs = Math.Clamp(State.NextEventMs, 20 * 60000, 120 * 60000);
    }
    void Touch() { dirty = true; Changed?.Invoke(); }
    public void Save(bool force = false) { if (!dirty && !force) return; dirty = false; OrderedStore.SaveJson(path, State.Snapshot()); }
    public long Counter(string name) => State.Counters.GetValueOrDefault(name);

    // ---- attendance ----
    public static string WeekOf(DateTime day) => day.Date.AddDays(-(((int)day.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
    public static int CheckInCoins(int streak) => 5 + 2 * Math.Min(Math.Max(streak, 1) - 1, 10);
    // A new week starts every goal from zero; the goals themselves stay.
    void RollWeek(DateTime now)
    {
        string week = WeekOf(now); if (State.Week == week) return;
        State.Week = week; State.WeekDays = 0; State.ManualRewards = 0; foreach (var goal in State.Goals) { goal.Amount = 0; goal.Done = false; }
        dirty = true;
    }
    // The first time on a day. Yesterday keeps the streak going; a missed day starts it again (nothing is taken away).
    public ProgressNews[] CheckIn(DateTime now)
    {
        string today = now.ToString("yyyy-MM-dd"); if (State.LastDay == today) return nothing;
        RollWeek(now);
        State.Streak = State.LastDay == now.AddDays(-1).ToString("yyyy-MM-dd") ? State.Streak + 1 : 1;
        State.LastDay = today; State.TotalDays++; State.WeekDays++; State.BestStreak = Math.Max(State.BestStreak, State.Streak);
        bool mark = streakMarks.Contains(State.Streak); int coins = CheckInCoins(State.Streak) + (mark ? State.Streak : 0);
        var news = new List<ProgressNews> { new("출석", mark ? State.Streak + "일 연속 출석!" : State.Streak > 1 ? State.Streak + "일 연속 출석" : "오늘도 만났어요", $"출석 {State.TotalDays}일째 · 최고 연속 {State.BestStreak}일", coins, !mark) };
        news.AddRange(Advance("days", 1)); Touch(); return news.ToArray();
    }

    // ---- weekly goals ----
    static void Fit(WeeklyGoal goal)
    {
        var kind = GoalKinds.Find(goal.Kind); goal.Kind = kind.Key; goal.Title = goal.Title.Trim(); goal.Title = goal.Title[..Math.Min(goal.Title.Length, 60)];
        goal.Target = Math.Clamp(goal.Target, kind.Low, kind.High); goal.Amount = Math.Max(0, goal.Amount);
    }
    public WeeklyGoal AddGoal(string title, string kind, int target, DateTime now)
    {
        RollWeek(now); if (State.Goals.Count >= MaxGoals) throw new InvalidOperationException("주간 목표는 " + MaxGoals + "개까지 만들 수 있어요.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("목표 이름을 적어 주세요.");
        var goal = new WeeklyGoal { Title = title, Kind = kind, Target = target }; Fit(goal); State.Goals.Add(goal); Touch(); return goal;
    }
    // Changing a goal keeps what was counted this week when the kind stays the same. A goal already rewarded stays done.
    public void EditGoal(string id, string title, string kind, int target)
    {
        var goal = State.Goals.FirstOrDefault(g => g.Id == id) ?? throw new InvalidOperationException("목표를 찾을 수 없어요.");
        if (string.IsNullOrWhiteSpace(title)) throw new InvalidOperationException("목표 이름을 적어 주세요.");
        if (GoalKinds.Find(kind).Key != goal.Kind) { goal.Amount = 0; }
        goal.Title = title; goal.Kind = kind; goal.Target = target; Fit(goal); Touch();
    }
    public void DeleteGoal(string id) { if (State.Goals.RemoveAll(g => g.Id == id) > 0) Touch(); }
    // One more for a goal the user counts by hand.
    public ProgressNews[] Tick(string id, DateTime now)
    {
        RollWeek(now); var goal = State.Goals.FirstOrDefault(g => g.Id == id && g.Kind == "manual"); if (goal == null || goal.Done) return nothing;
        goal.Amount++; var news = Finish(goal); Touch(); return news;
    }
    ProgressNews[] Finish(WeeklyGoal goal)
    {
        if (goal.Done || goal.Value < goal.Target) return nothing;
        goal.Done = true; State.Counters["goals"] = Counter("goals") + 1; int coins = GoalKinds.Find(goal.Kind).Coins;
        // Goals counted by hand are rewarded only a few times a week, so they cannot be used to print coins.
        if (goal.Kind == "manual" && ++State.ManualRewards > ManualRewardsPerWeek) coins = 0;
        return new[] { new ProgressNews("주간 목표 달성", goal.Title, $"{goal.Target}{GoalKinds.Find(goal.Kind).Unit} 달성", coins) };
    }
    ProgressNews[] Advance(string kind, long amount)
    {
        List<ProgressNews>? news = null;
        foreach (var goal in State.Goals) { if (goal.Kind != kind || goal.Done) continue; goal.Amount += amount; var done = Finish(goal); if (done.Length > 0) (news ??= new()).AddRange(done); }
        return news?.ToArray() ?? nothing;
    }

    // ---- counting ----
    public ProgressNews[] Count(string counter, DateTime now, int amount = 1)
    {
        RollWeek(now); State.Counters[counter] = Counter(counter) + amount; dirty = true;
        var news = Advance(counter, amount); if (news.Length > 0) Touch(); return news;
    }
    // Called for every stretch of typing that counts as work. Cheap when nothing happens.
    public ProgressNews[] Work(long delta, DateTimeOffset now, bool duo, Func<int, int>? roll = null)
    {
        if (delta <= 0) return nothing; delta = Math.Min(delta, 10000); RollWeek(now.LocalDateTime); dirty = true;
        List<ProgressNews>? news = null;
        var goals = Advance("work", delta); if (goals.Length > 0) (news ??= new()).AddRange(goals);
        if (State.Event is { } running)
        {
            if (now > running.Ends) { State.Event = null; (news ??= new()).Add(new("미니 이벤트", running.Title, "이번에는 시간이 조금 모자랐어요. 다음에 또 도전해요.", 0, true)); }
            else
            {
                running.ProgressMs += delta;
                if (running.ProgressMs >= running.TargetMs)
                {
                    State.Event = null; State.Counters["events"] = Counter("events") + 1;
                    (news ??= new()).Add(new("미니 이벤트 성공", running.Title, "제시간에 해냈어요!", running.Coins)); news.AddRange(Advance("events", 1));
                }
            }
        }
        else if (State.Events)
        {
            string today = now.LocalDateTime.ToString("yyyy-MM-dd"); if (State.EventDay != today) { State.EventDay = today; State.EventsToday = 0; }
            State.SinceEventMs += delta;
            if (State.SinceEventMs >= State.NextEventMs && State.EventsToday < EventsPerDay)
            {
                roll ??= Random.Shared.Next; var started = Start(roll(duo ? 3 : 2), now); State.Event = started; State.SinceEventMs = 0; State.EventsToday++; State.NextEventMs = (40 + roll(41)) * 60000L;
                (news ??= new()).Add(new("미니 이벤트 시작", started.Title, Describe(started, now), 0));
            }
        }
        if (news == null) return nothing; Touch(); return news.ToArray();
    }
    static MiniEvent Start(int pick, DateTimeOffset now) => pick switch
    {
        0 => new() { Kind = "sprint", Title = "반짝 집중", TargetMs = 10 * 60000, Ends = now.AddMinutes(20), Coins = 15 },
        1 => new() { Kind = "steady", Title = "꾸준히 한 걸음", TargetMs = 20 * 60000, Ends = now.AddMinutes(45), Coins = 30 },
        _ => new() { Kind = "duo", Title = "둘이서 응원", TargetMs = 15 * 60000, Ends = now.AddMinutes(30), Coins = 25 }
    };
    public static string Describe(MiniEvent item, DateTimeOffset now) =>
        $"{Math.Max(0, (int)Math.Ceiling((item.Ends - now).TotalMinutes))}분 안에 키보드 작업 {item.TargetMs / 60000}분 채우기 · 지금 {item.ProgressMs / 60000}분 · 성공하면 {item.Coins}코인";
    // An event whose time ran out while nobody was typing.
    public void Expire(DateTimeOffset now) { if (State.Event is { } running && now > running.Ends) { State.Event = null; Touch(); } }
    public void SetEvents(bool on) { State.Events = on; if (!on) State.Event = null; Touch(); }

    // ---- achievements ----
    // facts: how much of each thing there is right now (see Achievement.Fact). The first call counts what was earned before
    // achievements existed and reports it as one piece of news.
    public ProgressNews[] Achieve(IReadOnlyDictionary<string, long> facts, DateTime now)
    {
        List<Achievement>? reached = null;
        foreach (var item in All) if (!State.Achievements.ContainsKey(item.Id) && facts.GetValueOrDefault(item.Fact) >= item.Target) { State.Achievements[item.Id] = now.ToString("yyyy-MM-dd"); (reached ??= new()).Add(item); }
        bool first = !State.Seeded; if (first) { State.Seeded = true; dirty = true; }
        if (reached == null) return nothing; Touch();
        if (first && reached.Count > 1) return new[] { new ProgressNews("업적", "지금까지 쌓은 업적 " + reached.Count + "개", string.Join(" · ", reached.Take(6).Select(a => a.Title)) + (reached.Count > 6 ? " 외" : ""), reached.Sum(a => a.Coins)) };
        return reached.Select(a => new ProgressNews("업적 달성", a.Title, a.Description, a.Coins)).ToArray();
    }

    // ---- bought with coins ----
    public bool IsUnlocked(string key) => State.Unlocked.Contains(key);
    public void Unlock(string key) { if (State.Unlocked.Add(key)) Touch(); }
}
