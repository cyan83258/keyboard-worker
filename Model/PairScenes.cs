using System;
using System.Linq;

namespace KeyboardWorker;

// Pictures of the two pets together, shown for a moment when something fitting happens.
public static class PairScenes
{
    public static readonly (string Key, string Title, string Help)[] Slots =
    {
        ("talk", "둘이 이야기할 때", "둘이 대사를 주고받은 뒤 가끔 나옵니다."),
        ("work", "함께 일할 때", "업무 프로그램을 1시간 넘게 썼을 때 가끔 나옵니다."),
        ("rest", "함께 쉴 때", "휴식을 시작할 때 가끔 나옵니다."),
        ("night", "늦은 밤", "밤 인사를 건넬 때 가끔 나옵니다."),
        ("celebrate", "축하·기념일", "기념일에는 꼭 나옵니다.")
    };
    // The first place is free; each of the others is opened once with coins.
    public const string Free = "talk"; public const int Price = 1200;
    public static string Unlock(string key) => "scene:" + key;
    public static string Title(string key) => Slots.FirstOrDefault(s => s.Key == key).Title ?? "둘만의 장면";
    public static bool Known(string key) => Slots.Any(s => s.Key == key);
}
