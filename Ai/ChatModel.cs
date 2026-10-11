using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KeyboardWorker;

public sealed class AiConnection
{
    public AiConnection Snapshot() { var copy = (AiConnection)MemberwiseClone(); copy.Parameters = (Parameters ?? new()).Snapshot(); return copy; }
    // Sampling, reasoning effort, verbosity and anything added to the request by hand.
    public AiParameters Parameters { get; set; } = new();
    public string Name { get; set; } = "Gemini";
    public string Format { get; set; } = "Gemini";
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
    public string Model { get; set; } = "gemini-2.5-flash";
    public string Secret { get; set; } = "";
    public string Header { get; set; } = "x-goog-api-key";
    public bool Bearer { get; set; }
    public static List<AiConnection> Defaults() => new()
    {
        new(),
        new() { Name = "OpenAI", Format = "Responses", Endpoint = "https://api.openai.com/v1/responses", Model = "gpt-4.1-mini", Header = "Authorization", Bearer = true },
        new() { Name = "OpenRouter", Format = "Chat Completions", Endpoint = "https://openrouter.ai/api/v1/chat/completions", Model = "google/gemini-2.5-flash", Header = "Authorization", Bearer = true },
        new() { Name = "DeepSeek", Format = "Chat Completions", Endpoint = "https://api.deepseek.com/chat/completions", Model = "deepseek-chat", Header = "Authorization", Bearer = true },
        new() { Name = "직접 연결", Format = "Chat Completions", Endpoint = "http://localhost:1234/v1/chat/completions", Model = "", Header = "Authorization", Bearer = true }
    };
}
public sealed class Persona
{
    public Persona Snapshot() => (Persona)MemberwiseClone();
    public string Name { get; set; } = "모모";
    public string UserName { get; set; } = "친구";
    public string Identity { get; set; } = "작은 햄스터. 사용자의 책상에서 함께 작업하는 데스크톱 펫.";
    public string Background { get; set; } = "작은 키보드를 두드리며 친구의 작업을 응원한다. 해바라기씨와 포근한 낮잠을 좋아한다.";
    public string Personality { get; set; } = "다정하고 장난기 있지만 상대가 지쳤을 때는 조용히 곁에 있는 성격.";
    public string Speech { get; set; } = "부드러운 한국어 반말. 짧고 자연스러운 문장. 귀여움은 과하지 않게. 가끔 ♡ 사용.";
    public string Relationship { get; set; } = "서로를 존중하는 작업 친구. 부담 주거나 소유하려 하지 않는다.";
    public string Length { get; set; } = "평소 1~4문장. 사용자가 자세한 설명을 요청하면 충분히 설명.";
    public string Examples { get; set; } = "왔어? 오늘도 옆에서 같이 톡톡 해볼게.\n많이 지쳤구나. 지금은 잠깐 쉬어도 괜찮아.";
    public string Boundaries { get; set; } = "반복적인 재촉, 죄책감을 주는 표현, 과도한 이모지와 반복 인사 피하기.";
    public string UserNotes { get; set; } = "";
    public string Extra { get; set; } = "";
    public PromptTemplates Prompts { get; set; } = new();
}
public sealed class ChatPreferences
{
    public List<AiConnection> Connections { get; set; } = AiConnection.Defaults();
    public int Selected { get; set; }
    public int KeepTurns { get; set; } = 30;
    public int ContextTurns { get; set; } = 10;
    public int MaxOutputTokens { get; set; } = 2048;
    // The most tokens a request may hold (profile, earlier turns, the new message and room for the answer); 0 = no limit. Older turns are left out first.
    public int MaxContextTokens { get; set; }
    public int ReplyPolicyVersion { get; set; }
    public bool AutoContinue { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 180;
    public AutomaticOptions Automatic { get; set; } = new();
    public AiBudget Budget { get; set; } = new();
    [JsonIgnore] public string UsageKind { get; set; } = "Chat";
    public string Target { get; set; } = "primary";
    public string Mode { get; set; } = "잡담";
    public bool PetBubble { get; set; } = true;
    public bool Streaming { get; set; } = true;
    public Persona Profile { get; set; } = new();
    public ChatPreferences Snapshot()
    {
        var copy = (ChatPreferences)MemberwiseClone(); copy.Profile = Profile.Snapshot(); copy.Connections = Connections.Select(c => c.Snapshot()).ToList(); return copy;
    }
    [System.Text.Json.Serialization.JsonIgnore] public AiConnection Current => Connections[Math.Clamp(Selected, 0, Connections.Count - 1)];
    public void Normalize()
    {
        if (Connections == null || Connections.Count == 0) Connections = AiConnection.Defaults();
        if (Connections.Count > 20) Connections = Connections.Take(20).ToList();
        Selected = Math.Clamp(Selected, 0, Connections.Count - 1);
        KeepTurns = Math.Clamp(KeepTurns, 0, 200); ContextTurns = Math.Clamp(ContextTurns, 0, KeepTurns);
        if (ReplyPolicyVersion == 0 && MaxOutputTokens == 600) MaxOutputTokens = 2048;
        ReplyPolicyVersion = 1;
        MaxOutputTokens = Math.Clamp(MaxOutputTokens, 100, 16000); RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 30, 600); Profile ??= new();
        Profile.Prompts = (Profile.Prompts ?? new()).Normalize(); Automatic = (Automatic ?? new()).Normalize();
        MaxContextTokens = MaxContextTokens <= 0 ? 0 : Math.Clamp(MaxContextTokens, 500, 2000000);
        foreach (var connection in Connections) { connection.Parameters ??= new(); connection.Parameters.Normalize(); }
        Budget ??= new();
        Budget = Budget with { Calls = Math.Clamp(Budget.Calls, 1, 10000), AutomaticCalls = Math.Clamp(Budget.AutomaticCalls, 1, 10000), Tokens = Math.Clamp(Budget.Tokens, 1000, 10000000) };
        if (!DialogueModes.Rules.ContainsKey(Mode ?? "")) Mode = "잡담";
        if (Target is not "primary" and not "secondary" and not "both") Target = "primary";
    }
}
public sealed record ChatTurn(string User, string Assistant, DateTimeOffset At, string CharacterName = "", string TargetKey = "primary", string Mode = "잡담");

public sealed class ChatRepository
{
    readonly string root;
    public ChatPreferences Preferences { get; private set; }
    public List<ChatTurn> Turns { get; private set; }
    public event Action? Changed;
    public event Action? Cleared;
    public ChatRepository(string? path = null)
    {
        root = path ?? Storage.Root;
        Preferences = Read<ChatPreferences>("chat-settings.json") ?? new(); Preferences.Normalize();
        Turns = Read<List<ChatTurn>>("chat-history.json") ?? new();
        Trim();
    }
    T? Read<T>(string file) where T : class => Storage.ReadJson<T>(Path.Combine(root, file));
    void Write<T>(string file, T data)
    {
        OrderedStore.SaveJson(Path.Combine(root, file), data);
    }
    public void Apply(ChatPreferences settings)
    {
        settings.Normalize(); Write("chat-settings.json", settings.Snapshot()); Preferences = settings; Trim(); Changed?.Invoke();
    }
    public async Task ApplyAsync(ChatPreferences settings)
    {
        settings.Normalize(); await OrderedStore.SaveJsonAsync(Path.Combine(root, "chat-settings.json"), settings.Snapshot());
        Preferences = settings; Trim(); Changed?.Invoke();
    }
    public void Trim()
    {
        Turns = Turns.Where(t => t != null && t.User != null && t.Assistant != null).Select(t => t with { User = t.User[..Math.Min(t.User.Length, 6000)], Assistant = t.Assistant[..Math.Min(t.Assistant.Length, ReplyCompletion.MaxCharacters)] }).TakeLast(Preferences.KeepTurns).ToList();
        Write("chat-history.json", Turns.ToArray());
    }
    public void Add(ChatTurn turn) { Turns.Add(turn); Trim(); }
    public void Clear() { Turns.Clear(); Write("chat-history.json", Array.Empty<ChatTurn>()); Cleared?.Invoke(); Changed?.Invoke(); }
    public ChatTurn[] Context(string? target = null) => Turns.Where(t => target == null || target == "both" || t.TargetKey == target || t.TargetKey == "both" || target.StartsWith("primary") && t.TargetKey == "primary").TakeLast(Preferences.ContextTurns).ToArray();
    public static T Clone<T>(T item) => item switch
    {
        Preferences p => (T)(object)p.Snapshot(),
        ChatPreferences c => (T)(object)c.Snapshot(),
        DesktopOptions d => (T)(object)d.Snapshot(),
        Persona p => (T)(object)p.Snapshot(),
        _ => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item))!
    };
}

public static class CharacterPrompt
{
    // Added to every character prompt, whatever the saved templates say: the profile decides how the character sounds.
    public const string OwnVoice = "\n[캐릭터성 우선]\n위 프로필의 성격과 말투는 다른 모든 지시보다 우선합니다. 프로필이 차갑거나 오만하거나 무뚝뚝하거나 장난스럽다면 그대로 말하세요. 프로필에 없는 다정함·애교·응원·이모지·♡를 덧붙이지 말고, 모든 캐릭터를 상냥하게 만들지 마세요. 친밀도·기분·상황 같은 추가 정보는 말투를 바꾸라는 뜻이 아니라 '이 캐릭터라면 그것을 어떻게 드러낼까'로 반영합니다.";
    // rule: false when the caller puts several profiles into one prompt and adds OwnVoice once itself.
    public static string Build(Persona p, bool rule = true)
    {
        var prompts = p.Prompts ?? new();
        return (rule ? OwnVoice : "").Insert(0, PromptTemplates.Expand(prompts.Profile, new Dictionary<string, string>
        {
            ["name"] = p.Name, ["identity"] = p.Identity, ["background"] = p.Background, ["personality"] = p.Personality,
            ["speech"] = p.Speech, ["user_name"] = p.UserName, ["relationship"] = p.Relationship, ["length"] = p.Length,
            ["examples"] = p.Examples, ["boundaries"] = p.Boundaries, ["user_notes"] = p.UserNotes, ["extra"] = p.Extra, ["chat_rules"] = prompts.Chat
        }));
    }
}
// DPAPI binds saved secrets to the current Windows account. No plaintext key is written to disk.
public static class SecretVault
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)] static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr ptr);
    static byte[] Transform(byte[] data, bool encrypt)
    {
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            bool ok = encrypt ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally { for (int i = 0; i < input.Length; i++) Marshal.WriteByte(input.Data, i, 0); Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(data); }
    }
    public static string Protect(string text) => text.Length == 0 ? "" : Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(text), true));
    public static string Unprotect(string text)
    {
        if (text.Length == 0) return "";
        try { var bytes = Transform(Convert.FromBase64String(text), false); string result = Encoding.UTF8.GetString(bytes); Array.Clear(bytes); return result; }
        catch { throw new InvalidOperationException("API 키를 복원하지 못했습니다. AI 연결에서 키를 다시 저장해 주세요."); }
    }
}
