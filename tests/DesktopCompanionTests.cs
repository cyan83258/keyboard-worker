using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyboardWorker;

public static class DesktopCompanionTests
{
    sealed class Handler : HttpMessageHandler
    {
        public int Calls; public string Request = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Request = await request.Content!.ReadAsStringAsync(token);
            string text = "{\"first\":\"오늘 같이 톡톡 해볼까?\",\"second\":\"좋아, 내가 옆에 있을게!\"}";
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { candidates = new[] { new { content = new { parts = new[] { new { text } } }, finishReason = "STOP" } } }), Encoding.UTF8, "application/json") };
        }
    }
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var stroke = new StrokeGesture(); stroke.Move(new Point(0, 0), 0);
        Check(!stroke.Move(new Point(50, 0), 50) && !stroke.Move(new Point(100, 0), 100), "Single mouse pass does not trigger petting");
        stroke.Reset(); stroke.Move(new Point(50, 20), 1000); stroke.Move(new Point(90, 20), 1050); stroke.Move(new Point(40, 20), 1100);
        Check(stroke.Move(new Point(85, 20), 1150), "Back and forth strokes trigger automatic petting");
        stroke.Move(new Point(40, 20), 1200); stroke.Move(new Point(90, 20), 1250); stroke.Move(new Point(40, 20), 1300);
        Check(!stroke.Move(new Point(90, 20), 1350), "Petting gesture cooldown prevents repeated dialogue flooding");
        var stationary = new StrokeGesture(); bool triggered = false; for (int i = 0; i < 30; i++) triggered |= stationary.Move(new Point(30, 30), i * 20);
        Check(!triggered, "Stationary hover does not award petting");
        var room = new Rect(0, 0, 1280, 900); var petSize = new Size(240, 200);
        var atLeft = CompanionPlacement.Place(petSize, new Rect(0, 100, 240, 200), room, new Point(0, 100));
        Check(atLeft.X == 264 && atLeft.Y == 100, "Secondary at left edge restores beside primary instead of clamping on top of it");
        var atRight = CompanionPlacement.Place(petSize, new Rect(1040, 100, 240, 200), room, new Point(1040, 100));
        Check(atRight.X == 776 && atRight.Y == 100, "Fully overlapping saved secondary position restores to free left side");
        var independent = CompanionPlacement.Place(petSize, new Rect(500, 300, 240, 200), room, new Point(100, 500));
        Check(independent == new Point(100, 500), "Valid independent secondary placement is preserved");
        var outside = CompanionPlacement.Place(petSize, new Rect(500, 300, 240, 200), room, new Point(9000, -10000));
        Check(room.Contains(new Rect(outside, petSize)), "Offscreen secondary position is brought inside current screen");
        using (var trail = new HeartTrail())
        {
            trail.Measure(new Size(120, 120)); trail.Arrange(new Rect(0, 0, 120, 120)); trail.Emit(new Point(60, 60)); trail.UpdateLayout();
            var frame = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32); frame.Render(trail); byte[] art = new byte[120 * 120 * 4]; frame.CopyPixels(art, 480, 0);
            bool painted = false; for (int i = 3; i < art.Length; i += 4) painted |= art[i] > 0; Check(painted, "Pastel heart trail draws visible particles on transparent overlay");
        }
        var pet = new Rect(500, 400, 180, 180); var area = new Rect(0, 0, 1280, 900); var size = new Size(300, 100);
        var placed = FloatingPlacement.Bubble(size, pet, area, "위", 4, 0, 0);
        Check(placed.Y == 296 && placed.X == 440, "Bubble sits four pixels from actual character bounds");
        var shifted = FloatingPlacement.Bubble(size, pet, area, "위", 4, 30, 60); var overlap = Rect.Intersect(new Rect(shifted, size), pet);
        Check(overlap.IsEmpty || overlap.Width == 0 || overlap.Height == 0, "Offsets never place bubble over character");
        var edge = FloatingPlacement.Bubble(size, new Rect(500, 20, 180, 180), area, "위", 4, 0, 0);
        Check(edge.Y == 204, "Bubble moves below pet near top screen edge");
        var firstBubble = new Rect(440, 296, 300, 100); var secondPet = new Rect(720, 400, 180, 180);
        var secondBubble = new Rect(FloatingPlacement.Bubble(size, secondPet, area, "위", 4, -200, 0, pet, firstBubble), size);
        Check(!secondBubble.IntersectsWith(pet) && !secondBubble.IntersectsWith(firstBubble), "Two-character bubble placement avoids other pet and reply");
        byte[] pixels = new byte[20 * 20 * 4]; for (int y = 5; y < 15; y++) for (int x = 4; x < 16; x++) pixels[(y * 20 + x) * 4 + 3] = 255;
        var bitmap = BitmapSource.Create(20, 20, 96, 96, PixelFormats.Bgra32, null, pixels, 80); var image = new Image { Source = bitmap, Width = 200, Height = 200, Stretch = Stretch.Uniform };
        image.Measure(new Size(200, 200)); image.Arrange(new Rect(0, 0, 200, 200)); var visible = PetImageLayout.ImageBounds(image);
        Check(visible == new Rect(40, 50, 120, 100), "Transparent image padding is excluded from bubble anchor");
        var normal = new Window { Width = 20, Height = 20, Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None }; normal.Show();
        try
        {
            IntPtr handle = new WindowInteropHelper(normal).Handle; IntPtr foreground = Native.ForegroundHandle; Native.Style(handle, false); Native.RaisePet(handle);
            Check(Native.IsTopmost(handle) && Native.NoActivate(handle) && Native.ForegroundHandle == foreground, "Topmost recovery does not steal keyboard focus");
        }
        finally { normal.Close(); }
        var prefs = new Preferences { Topmost = false, HideFullscreen = true, Duo = new() { Enabled = true, PresetId = "second", Left = 700, Top = 500 } }; prefs.Normalize();
        Check(prefs.Topmost && !prefs.HideFullscreen && prefs.Duo.Left == 700, "Previous visibility defaults migrate while second pet position is retained");
        prefs.HideFullscreen = true; prefs.Normalize(); Check(prefs.HideFullscreen, "User can explicitly enable full-screen hiding after migration");
        var copy = prefs.Snapshot(); copy.Duo = copy.Duo with { Left = 300 };
        Check(prefs.Duo.Left == 700 && copy.Left == prefs.Left, "Second pet placement is independent and snapshots do not mutate first pet");
        string file = Path.Combine(root, "duo-settings.json"); OrderedStore.SaveJsonAsync(file, prefs).GetAwaiter().GetResult();
        Check(JsonSerializer.Deserialize<Preferences>(File.ReadAllText(file))!.Duo.PresetId == "second", "Two-character selection and position survive reload");
        Check(DuoInteraction.Parse("```json\n{\"first\":\"A\",\"second\":\"B\"}\n```") == new DuoReply("A", "B"), "Pair dialogue parses separate speakers and fenced JSON");
        bool rejected = false;
        try { DuoInteraction.Parse("{\"first\":\"A\"}"); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Incomplete interaction is rejected without invented second dialogue");
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var chat = new ChatPreferences { Profile = new Persona { Name = "대표친구", Speech = "대표 말투" }, MaxOutputTokens = 4000 };
        var result = DuoInteraction.Generate(chat, new Persona { Name = "둘째친구", Speech = "둘째 말투" }, new DuoOptions(), "지난 대화", Array.Empty<ChatTurn>(), CancellationToken.None, new AiClient(http)).GetAwaiter().GetResult();
        using (var json = JsonDocument.Parse(handler.Request))
        {
            string profile = json.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString()!;
            Check(handler.Calls == 1 && result.First.Contains("톡톡") && result.Second.Contains("옆에"), "One AI request produces both interaction lines");
            Check(profile.Contains("대표 말투") && profile.Contains("둘째 말투"), "Pair request includes both independent character profiles");
        }
        Check(chat.MaxOutputTokens == 4000 && chat.Profile.Name == "대표친구" && chat.AutoContinue, "Interaction does not modify primary chat profile or output policy");
        string tallPath = Path.Combine(root, "tall-image.png"); var tall = BitmapSource.Create(8, 800, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 800 * 4], 32);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(tall)); using (var stream = File.Create(tallPath)) png.Save(stream);
        var decoded = ImageAssets.Read(tallPath, 128); Check(decoded.PixelWidth <= 128 && decoded.PixelHeight <= 128, "Tall images decode within a bounded rectangle before alpha scanning");
        return count;
    }
}
