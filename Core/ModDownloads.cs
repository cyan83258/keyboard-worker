using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// Mods from a GitHub address: one mod file, or every mod file in a folder or repository. What arrives is read as a mod
// (data, checked like a file from disk) and nothing else is done with it.
public static class ModDownloads
{
    public sealed record Result(IReadOnlyList<ModFile> Installed, IReadOnlyList<string> Problems);
    public static async Task<Result> Install(ModStore store, string address, CancellationToken cancellation)
    {
        var link = GitHubLink.Parse(address) ?? throw new InvalidOperationException("GitHub 주소가 아닙니다. 저장소 주소(github.com/이름/저장소)나 모드 파일 주소를 붙여 넣어 주세요.");
        var installed = new List<ModFile>(); var problems = new List<string>();
        (string Name, string Url)[] files;
        if (link.IsFile) files = new[] { (System.IO.Path.GetFileName(link.Path), link.Raw) };
        else
        {
            try { files = GitHubLink.JsonFiles(await Downloader.Text(link.Listing, 1024 * 1024, cancellation)); }
            catch (System.Text.Json.JsonException) { throw new InvalidOperationException("저장소의 파일 목록을 읽지 못했습니다."); }
            if (files.Length == 0) throw new InvalidOperationException("그 주소에 모드 파일(.json)이 없습니다. 모드 파일이 들어 있는 폴더나 파일의 주소를 넣어 주세요.");
        }
        foreach (var (name, url) in files)
        {
            try { installed.Add(store.InstallText(await Downloader.Text(url, ModFile.MaxBytes, cancellation), url)); }
            catch (InvalidOperationException e) { problems.Add(name + ": " + e.Message); }
        }
        // A repository usually holds other JSON files too; they are only worth mentioning when nothing at all was a mod.
        if (installed.Count == 0) throw new InvalidOperationException(problems.Count == 1 ? problems[0] : "모드로 읽을 수 있는 파일이 없었습니다. (" + string.Join(" / ", problems.Take(3)) + ")");
        return new(installed, link.IsFile ? problems : Array.Empty<string>());
    }
    // Fetches a mod again from where it was installed from. Returns the new version's mod.
    public static async Task<ModFile> Update(ModStore store, InstalledMod mod, CancellationToken cancellation)
    {
        if (mod.File.Source.Length == 0 || !Uri.TryCreate(mod.File.Source, UriKind.Absolute, out var uri) || !Downloader.Allowed(uri)) throw new InvalidOperationException("이 모드는 파일로 직접 넣은 것이라 받아 올 주소가 없습니다.");
        var fresh = ModFile.Parse(await Downloader.Text(mod.File.Source, ModFile.MaxBytes, cancellation));
        if (fresh.Id != mod.File.Id) throw new InvalidOperationException("그 주소의 모드가 다른 모드로 바뀌었습니다 (id가 다릅니다). 지우고 새로 설치해 주세요.");
        return store.InstallText(System.Text.Json.JsonSerializer.Serialize(fresh, ModFile.Writing), mod.File.Source);
    }
}
