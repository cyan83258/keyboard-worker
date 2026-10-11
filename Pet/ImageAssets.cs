using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace KeyboardWorker;

public static class ImageAssets
{
    public static readonly string[] BasicKeys = { "idle", "typing-left", "typing-right", "nudge" };
    public static readonly (string Key, string Label)[] Basic = { ("idle", "기본 · 대기"), ("typing-left", "왼손 타이핑"), ("typing-right", "오른손 타이핑"), ("nudge", "재촉할 때") };
    public static readonly (string Key, string Label)[] Music = { ("music-idle", "노래 듣기 · 기본"), ("music-left", "노래 들으며 왼손 들기"), ("music-right", "노래 들으며 오른손 들기"), ("music-nudge", "노래 들으며 재촉") };
    public static string MusicKey(string state) => state switch { "idle" => "music-idle", "typing-left" => "music-left", "typing-right" => "music-right", "nudge" => "music-nudge", _ => state };
    public static readonly (string Key, string Label)[] Advanced = { ("pat", "쓰다듬어질 때"), ("chatting", "대화 중 · 기본"), ("thinking", "답변을 생각할 때"), ("happy", "기쁨 · 신남"), ("sad", "슬픔 · 시무룩"), ("angry", "화남 · 투덜거림"), ("surprised", "놀람"), ("shy", "부끄러움"), ("sleepy", "졸림 · 피곤함"), ("caring", "위로 · 다정함") };
    public static (int Width, int Height) Validate(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg") throw new InvalidDataException("PNG 또는 JPG 파일을 선택해 주세요.");
        if (!File.Exists(path)) throw new FileNotFoundException("이미지 파일을 찾지 못했습니다.");
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("이미지는 장당 20MB 이하로 넣어 주세요.");
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder is not PngBitmapDecoder && decoder is not JpegBitmapDecoder) throw new InvalidDataException("파일 내용이 PNG/JPG 형식이 아닙니다.");
        var frame = decoder.Frames[0];
        if (frame.PixelWidth > 4096 || frame.PixelHeight > 4096) throw new InvalidDataException("가로·세로는 각각 4096px 이하로 넣어 주세요.");
        return (frame.PixelWidth, frame.PixelHeight);
    }
    public static BitmapSource Read(string path, int decodeWidth = 600)
    {
        int width, height;
        using (var metadata = File.OpenRead(path)) { var decoder = BitmapDecoder.Create(metadata, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None); width = decoder.Frames[0].PixelWidth; height = decoder.Frames[0].PixelHeight; }
        var bmp = new BitmapImage(); bmp.BeginInit(); bmp.CacheOption = BitmapCacheOption.OnLoad; bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        if (height > width) bmp.DecodePixelHeight = Math.Min(height, Math.Max(1, decodeWidth)); else bmp.DecodePixelWidth = Math.Min(width, Math.Max(1, decodeWidth));
        bmp.UriSource = new Uri(Path.GetFullPath(path)); bmp.EndInit(); bmp.Freeze(); return bmp;
    }
    public static string Import(string path, string root)
    {
        Validate(path); string folder = Path.Combine(root, "images"); Directory.CreateDirectory(folder);
        // Stored once however many slots or presets use it, and never larger than it is drawn.
        string extension = Path.GetExtension(path).ToLowerInvariant(); byte[] content = ImageDiet.Slim(path);
        if (ImageDiet.Existing(folder, extension, content, path) is string stored) return stored;
        string target = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension); File.WriteAllBytes(target, content); return target;
    }
    public static Task<Dictionary<string, string>> ImportSelectionAsync(Dictionary<string, string> selected, Dictionary<string, string> previous, HashSet<string> explicitlySelected, string root)
    {
        return Task.Run(() =>
        {
            var result = new Dictionary<string, string>();
            foreach (var pair in selected)
                result[pair.Key] = !explicitlySelected.Contains(pair.Key) && previous.TryGetValue(pair.Key, out var old) && string.Equals(old, pair.Value, StringComparison.OrdinalIgnoreCase) ? pair.Value : Import(pair.Value, root);
            return result;
        });
    }
    public static bool IsAdvanced(string key) => Advanced.Any(s => s.Key == key);
    public static string VisibleState(string state, bool advanced) => !advanced && IsAdvanced(state) ? "idle" : state;
}
