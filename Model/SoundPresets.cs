using System;
using System.IO;

namespace KeyboardWorker;

public sealed record SoundPreset
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
}
public static class SoundAssets
{
    public static void Validate(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not ".mp3" and not ".wav") throw new InvalidDataException("MP3 또는 WAV 파일을 선택해 주세요.");
        var info = new FileInfo(path); if (!info.Exists) throw new FileNotFoundException("소리 파일을 찾지 못했습니다.");
        if (info.Length > 20 * 1024 * 1024 || info.Length < 3) throw new InvalidDataException("소리 파일은 20MB 이하의 정상 파일을 선택해 주세요.");
        using var stream = File.OpenRead(path); byte[] header = new byte[12]; int read = stream.Read(header, 0, header.Length);
        bool mp3 = read >= 3 && ((header[0] == 'I' && header[1] == 'D' && header[2] == '3') || (header[0] == 255 && (header[1] & 224) == 224));
        bool wav = read == 12 && System.Text.Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && System.Text.Encoding.ASCII.GetString(header, 8, 4) == "WAVE";
        if (ext == ".mp3" ? !mp3 : !wav) throw new InvalidDataException("파일 내용이 MP3/WAV 형식과 일치하지 않습니다.");
    }
    public static string Import(string path, string root)
    {
        Validate(path); string folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, "sounds")); Directory.CreateDirectory(folder);
        string full = System.IO.Path.GetFullPath(path);
        if (string.Equals(System.IO.Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase) && Guid.TryParseExact(System.IO.Path.GetFileNameWithoutExtension(full), "N", out _)) return full;
        string target = System.IO.Path.Combine(folder, Guid.NewGuid().ToString("N") + System.IO.Path.GetExtension(path).ToLowerInvariant()); File.Copy(path, target); return target;
    }
}
