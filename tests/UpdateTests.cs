using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// Fetching from GitHub (never the real network here), mods installed from an address, and updating the program by hand.
public static class UpdateTests
{
    // Answers requests from a table; anything not in the table is a 404. Remembers what was asked for.
    sealed class FakeWeb : HttpMessageHandler
    {
        public readonly Dictionary<string, Func<HttpResponseMessage>> Pages = new(); public readonly List<string> Asked = new();
        public void Text(string url, string body) => Pages[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
        public void Bytes(string url, byte[] body) => Pages[url] = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        public void Redirect(string url, string to) => Pages[url] = () => { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute); return response; };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            string url = request.RequestUri!.ToString(); Asked.Add(url);
            return Task.FromResult(Pages.TryGetValue(url, out var page) ? page() : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
    static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    public static int Run(string root)
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        bool Throws(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
        string home = Path.Combine(root, "update"); Directory.CreateDirectory(home); var web = new FakeWeb(); var none = CancellationToken.None;
        Downloader.TestHandler = web;
        try
        {
            // Addresses people paste
            Check(GitHubLink.Parse("https://github.com/someone/kw-mods") == new GitHubLink("someone", "kw-mods", "", "", false) && GitHubLink.Parse(" github.com/someone/kw-mods.git ") == new GitHubLink("someone", "kw-mods", "", "", false) && GitHubLink.Parse("https://www.github.com/someone/kw-mods/") == new GitHubLink("someone", "kw-mods", "", "", false), "A repository address is understood, with or without https, www or .git");
            Check(GitHubLink.Parse("https://github.com/a/b/blob/main/mods/night.kwmod.json") == new GitHubLink("a", "b", "main", "mods/night.kwmod.json", true) && GitHubLink.Parse("https://github.com/a/b/raw/dev/x.json") == new GitHubLink("a", "b", "dev", "x.json", true) && GitHubLink.Parse("https://github.com/a/b/tree/main/mods") == new GitHubLink("a", "b", "main", "mods", false) && GitHubLink.Parse("https://github.com/a/b/tree/main") == new GitHubLink("a", "b", "main", "", false), "A file, a folder and a branch are told apart");
            Check(GitHubLink.Parse("https://raw.githubusercontent.com/a/b/main/x.json") == new GitHubLink("a", "b", "main", "x.json", true) && GitHubLink.Parse("https://raw.githubusercontent.com/a/b/refs/heads/main/dir/x.json") == new GitHubLink("a", "b", "main", "dir/x.json", true), "A raw file address is understood, in its short and long form");
            Check(new[] { "", "그냥 글자", "http://github.com/a/b", "https://gitlab.com/a/b", "https://github.com.evil.com/a/b", "https://evil.com/github.com/a/b", "https://github.com/a", "https://github.com/a/b/blob/main", "https://github.com/a/b/issues/3", "https://user:pw@github.com/a/b", "https://github.com/a/b/blob/main/../../secret", "ftp://github.com/a/b", "https://github.com/a b/c", new string('a', 600) }.All(bad => GitHubLink.Parse(bad) == null), "Anything that is not a GitHub repository, folder or file is not accepted");
            var spaced = GitHubLink.Parse("https://github.com/a/b/blob/main/내 모드/밤 하늘.json")!;
            Check(spaced.Path == "내 모드/밤 하늘.json" && spaced.Raw == "https://raw.githubusercontent.com/a/b/main/" + Uri.EscapeDataString("내 모드") + "/" + Uri.EscapeDataString("밤 하늘.json") && GitHubLink.Parse("https://github.com/a/b/tree/dev/mods")!.Listing == "https://api.github.com/repos/a/b/contents/mods?ref=dev" && GitHubLink.Parse("github.com/a/b")!.Listing == "https://api.github.com/repos/a/b/contents/" && GitHubLink.Parse("github.com/a/b")!.Page == "https://github.com/a/b", "The addresses to fetch from are built from the parts, names with spaces included");
            Check(Downloader.Allowed(new Uri("https://raw.githubusercontent.com/a/b/main/x.json")) && Downloader.Allowed(new Uri("https://API.GitHub.com/repos")) && !Downloader.Allowed(new Uri("http://github.com/a")) && !Downloader.Allowed(new Uri("https://github.com:8443/a")) && !Downloader.Allowed(new Uri("https://evil.com/a")) && !Downloader.Allowed(new Uri("https://u:p@github.com/a")) && !Downloader.Allowed(null), "Only GitHub's own servers, over HTTPS on the usual port, are fetched from");

            // Fetching
            web.Text("https://raw.githubusercontent.com/a/b/main/hello.txt", "﻿안녕"); web.Redirect("https://github.com/a/b/raw/main/hello.txt", "https://raw.githubusercontent.com/a/b/main/hello.txt");
            web.Redirect("https://github.com/a/b/raw/main/away.txt", "https://evil.example.com/x"); web.Redirect("https://github.com/loop", "/loop"); web.Text("https://raw.githubusercontent.com/a/b/main/big.txt", new string('x', 5000));
            Check(Wait(Downloader.Text("https://github.com/a/b/raw/main/hello.txt", 100, none)) == "안녕" && web.Asked.Count == 2, "A redirect to another GitHub server is followed");
            int asked = web.Asked.Count; Check(Throws(() => Wait(Downloader.Text("https://github.com/a/b/raw/main/away.txt", 100, none))) && web.Asked.Count == asked + 1 && !web.Asked.Any(u => u.Contains("evil")), "A redirect that leaves GitHub is refused without asking the other server");
            Check(Throws(() => Wait(Downloader.Text("https://github.com/loop", 100, none))) && Throws(() => Wait(Downloader.Text("https://evil.example.com/x", 100, none))) && Throws(() => Wait(Downloader.Text("https://raw.githubusercontent.com/a/b/main/big.txt", 1000, none))) && Throws(() => Wait(Downloader.Text("https://raw.githubusercontent.com/a/b/main/missing.txt", 1000, none))), "Endless redirects, other hosts, files over the limit and missing files are refused");
            string saved = Path.Combine(home, "got", "big.txt"); Wait(Task.Run(async () => { await Downloader.File("https://raw.githubusercontent.com/a/b/main/big.txt", saved, 10000, null, none); return 0; }));
            Check(File.ReadAllText(saved).Length == 5000 && !File.Exists(saved + ".part"), "A file is downloaded whole and no partial file is left");
            Check(Throws(() => Wait(Task.Run(async () => { await Downloader.File("https://raw.githubusercontent.com/a/b/main/big.txt", Path.Combine(home, "got", "small.txt"), 100, null, none); return 0; }))) && Directory.GetFiles(Path.Combine(home, "got")).Length == 1, "A download over the limit leaves nothing behind");

            // Mods from an address
            var store = ModStore.Reload(Path.Combine(home, "mod-home")); var night = ModFile.Sample(); night.Id = "night"; night.Name = "밤 모드"; night.Version = "1.0"; var day = ModFile.Sample(); day.Id = "day"; day.Name = "낮 모드";
            string nightUrl = "https://raw.githubusercontent.com/a/mods/main/night.kwmod.json", dayUrl = "https://raw.githubusercontent.com/a/mods/main/day.kwmod.json";
            web.Text(nightUrl, JsonSerializer.Serialize(night, ModFile.Writing)); web.Text(dayUrl, JsonSerializer.Serialize(day, ModFile.Writing)); web.Text("https://raw.githubusercontent.com/a/mods/main/package.json", "{\"name\":\"not a mod\"}");
            string Item(string name, string url, int size = 100, string type = "file") => "{\"name\":\"" + name + "\",\"type\":\"" + type + "\",\"size\":" + size + ",\"download_url\":" + (url.Length == 0 ? "null" : "\"" + url + "\"") + "}";
            web.Text("https://api.github.com/repos/a/mods/contents/", "[" + string.Join(",", Item("README.md", "https://raw.githubusercontent.com/a/mods/main/README.md"), Item("docs", "", 0, "dir"), Item("package.json", "https://raw.githubusercontent.com/a/mods/main/package.json"), Item("night.kwmod.json", nightUrl), Item("day.kwmod.json", dayUrl), Item("huge.kwmod.json", "https://raw.githubusercontent.com/a/mods/main/huge.kwmod.json", 9999999), Item("else.kwmod.json", "https://evil.example.com/else.kwmod.json")) + "]");
            var one = Wait(ModDownloads.Install(store, "https://github.com/a/mods/blob/main/night.kwmod.json", none));
            Check(one.Installed.Single().Id == "night" && store.Mods.Single().File.Source == nightUrl && one.Problems.Count == 0, "One mod file is installed from its address, and remembers where it came from");
            var all = Wait(ModDownloads.Install(store, "github.com/a/mods", none));
            Check(all.Installed.Select(m => m.Id).OrderBy(i => i).SequenceEqual(new[] { "day", "night" }) && store.Mods.Count == 2 && all.Problems.Count == 0 && !web.Asked.Any(u => u.Contains("evil") || u.Contains("huge") || u.EndsWith("package.json")), "A repository address installs every mod file in it; other files, oversized ones and files hosted elsewhere are not fetched");
            web.Text("https://api.github.com/repos/a/plain/contents/", "[" + Item("package.json", "https://raw.githubusercontent.com/a/mods/main/package.json") + "]"); web.Text("https://api.github.com/repos/a/empty/contents/", "[]");
            Check(Throws(() => Wait(ModDownloads.Install(store, "github.com/a/plain", none))) && Throws(() => Wait(ModDownloads.Install(store, "github.com/a/empty", none))) && Throws(() => Wait(ModDownloads.Install(store, "https://example.com/mod.json", none))) && Throws(() => Wait(ModDownloads.Install(store, "github.com/a/mods/blob/main/missing.json", none))) && store.Mods.Count == 2, "An address without a mod in it installs nothing and says so");
            night.Version = "2.0"; night.Description = "새 설명"; web.Text(nightUrl, JsonSerializer.Serialize(night, ModFile.Writing));
            var updated = Wait(ModDownloads.Update(store, store.Mods.Single(m => m.File.Id == "night"), none));
            Check(updated.Version == "2.0" && store.Mods.Single(m => m.File.Id == "night").File is { Version: "2.0", Description: "새 설명" } file && file.Source == nightUrl && store.Mods.Count == 2, "A mod from GitHub can be fetched again and is replaced by the newer file");
            night.Id = "something-else"; web.Text(nightUrl, JsonSerializer.Serialize(night, ModFile.Writing));
            Check(Throws(() => Wait(ModDownloads.Update(store, store.Mods.Single(m => m.File.Id == "night"), none))) && store.Mods.Count == 2, "If the address now holds a different mod, nothing is replaced");
            string byHand = Path.Combine(home, "hand.json"); var local = ModFile.Sample(); local.Id = "hand"; local.Source = "https://evil.example.com/hand.json"; File.WriteAllText(byHand, JsonSerializer.Serialize(local, ModFile.Writing)); store.Install(byHand);
            Check(store.Mods.Single(m => m.File.Id == "hand").File.Source == "" && Throws(() => Wait(ModDownloads.Update(store, store.Mods.Single(m => m.File.Id == "hand"), none))), "A mod added from a file has no address, and one written into the file from elsewhere is dropped");

            // Versions and releases
            Check(Updater.ParseVersion("v2.1.0") == new Version(2, 1, 0, 0) && Updater.ParseVersion("2.1") == new Version(2, 1, 0, 0) && Updater.ParseVersion(" V10.0.3-beta2 ") == new Version(10, 0, 3, 0) && Updater.ParseVersion("1.2.3.4") == new Version(1, 2, 3, 4) && Updater.ParseVersion("release") == null && Updater.ParseVersion("v2") == null && Updater.ParseVersion(null) == null, "Version tags are read with or without a v and a suffix");
            byte[] package = Package(("KeyboardWorker/KeyboardWorker.exe", "new exe"), ("KeyboardWorker/KeyboardWorker.dll", "new dll"), ("KeyboardWorker/lib/extra.dll", "extra"), ("KeyboardWorker/읽어주세요.txt", "읽기"));
            string hash = Convert.ToHexString(SHA256.HashData(package)).ToLowerInvariant(), assetUrl = "https://github.com/me/kw/releases/download/v2.1.0/KeyboardWorker-win-x64.zip";
            string Release(string tag, string assets) => "{\"tag_name\":\"" + tag + "\",\"name\":\"가을 업데이트\",\"body\":\"- 새 기능\\r\\n- 고친 것\",\"html_url\":\"https://github.com/me/kw/releases/tag/" + tag + "\",\"assets\":[" + assets + "]}";
            string Asset(string name, string url, long size, string digest = "") => "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + url + "\",\"size\":" + size + (digest.Length > 0 ? ",\"digest\":\"sha256:" + digest + "\"" : "") + "}";
            var release = Updater.ParseRelease(Release("v2.1.0", string.Join(",", Asset("source-notes.txt", "https://github.com/me/kw/releases/download/v2.1.0/notes.txt", 5), Asset("KeyboardWorker-linux.zip", "https://github.com/me/kw/releases/download/v2.1.0/linux.zip", 9), Asset("KeyboardWorker-win-x64.zip", assetUrl, package.Length, hash.ToUpperInvariant()))));
            Check(release.Version == new Version(2, 1, 0, 0) && release.Tag == "v2.1.0" && release.Title == "가을 업데이트" && release.Notes == "- 새 기능\n- 고친 것" && release.AssetName == "KeyboardWorker-win-x64.zip" && release.AssetUrl == assetUrl && release.Size == package.Length && release.Sha256 == hash, "The newest release is read: its number, notes, and the package for 64-bit Windows with its checksum");
            Check(Updater.Newer(release, "2.0.0") && !Updater.Newer(release, "2.1.0") && !Updater.Newer(release, "3.0") && Updater.Newer(release, "개발판"), "A release is newer only when its number is higher (or this copy has no number)");
            Check(Updater.ParseRelease(Release("v3.0", Asset("only.zip", "https://github.com/me/kw/releases/download/v3.0/only.zip", 1))).AssetName == "only.zip", "Without a package named for Windows the only ZIP is taken");
            Check(Throws(() => Updater.ParseRelease(Release("v3.0", Asset("setup.exe", "https://github.com/me/kw/releases/download/v3.0/setup.exe", 1)))) && Throws(() => Updater.ParseRelease(Release("v3.0", Asset("a.zip", "https://evil.example.com/a.zip", 1)))) && Throws(() => Updater.ParseRelease(Release("latest", Asset("a.zip", assetUrl, 1)))) && Throws(() => Updater.ParseRelease("[]")) && Throws(() => Updater.ParseRelease(Release("v3.0", Asset("a.zip", assetUrl, 900L * 1024 * 1024)))), "A release without a ZIP, with a package hosted elsewhere, without a version number or with an oversized package is not offered");
            Check(Updater.Configured && AppInfo.UpdateRepository == "cyan83258/keyboard-worker" && Throws(() => Wait(Updater.Check(none))) && web.Asked.Last() == "https://api.github.com/repos/cyan83258/keyboard-worker/releases/latest", "The update address is the program's own repository; with no release published yet there is nothing to offer");

            // Downloading and checking the package
            string data = Path.Combine(home, "data"); Directory.CreateDirectory(Path.Combine(data, "updates")); File.WriteAllText(Path.Combine(data, "updates", "old-leftover.zip"), "x"); web.Bytes(assetUrl, package);
            string zip = Wait(Updater.Download(release, data, null, none));
            Check(File.ReadAllBytes(zip).SequenceEqual(package) && Path.GetDirectoryName(zip) == Path.Combine(data, "updates") && Directory.GetFiles(Path.Combine(data, "updates")).Length == 1, "The package is downloaded into the settings folder's updates folder, replacing what an earlier try left");
            Check(Throws(() => Wait(Updater.Download(release with { Sha256 = new string('0', 64) }, data, null, none))) && Throws(() => Wait(Updater.Download(release with { Size = package.Length + 1 }, data, null, none))), "A package whose checksum or size is not what GitHub says is refused");
            zip = Wait(Updater.Download(release with { Sha256 = "", Size = 0 }, data, null, none));

            // Unpacking
            string staged = Updater.Stage(zip);
            Check(Path.GetFileName(staged) == "KeyboardWorker" && File.ReadAllText(Path.Combine(staged, "KeyboardWorker.exe")) == "new exe" && File.Exists(Path.Combine(staged, "lib", "extra.dll")), "The package is unpacked, and the folder it wraps the program in is found");
            string flat = Path.Combine(home, "flat.zip"), noProgram = Path.Combine(home, "no-program.zip"), escape = Path.Combine(home, "escape.zip"), notZip = Path.Combine(home, "not.zip");
            File.WriteAllBytes(flat, Package(("KeyboardWorker.exe", "e"), ("KeyboardWorker.dll", "d"))); File.WriteAllBytes(noProgram, Package(("readme.txt", "hello"))); File.WriteAllBytes(escape, Package(("KeyboardWorker.exe", "e"), ("KeyboardWorker.dll", "d"), ("../outside.txt", "x"))); File.WriteAllText(notZip, "this is not a zip");
            Check(File.Exists(Path.Combine(Updater.Stage(flat), "KeyboardWorker.exe")) && Throws(() => Updater.Stage(noProgram)) && Throws(() => Updater.Stage(escape)) && !File.Exists(Path.Combine(home, "outside.txt")) && Throws(() => Updater.Stage(notZip)), "A package without the program, with a path leading outside, or that is not a ZIP is refused");

            // Replacing the program files
            string program = Path.Combine(home, "program"); Directory.CreateDirectory(program);
            File.WriteAllText(Path.Combine(program, "KeyboardWorker.exe"), "old exe"); File.WriteAllText(Path.Combine(program, "KeyboardWorker.dll"), "old dll"); File.WriteAllText(Path.Combine(program, "kept-by-user.txt"), "mine");
            using (var inUse = new FileStream(Path.Combine(program, "KeyboardWorker.dll"), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                int replaced = Updater.Apply(staged, program);
                Check(replaced == 4 && File.ReadAllText(Path.Combine(program, "KeyboardWorker.exe")) == "new exe" && File.ReadAllText(Path.Combine(program, "KeyboardWorker.dll")) == "new dll" && File.ReadAllText(Path.Combine(program, "lib", "extra.dll")) == "extra" && File.ReadAllText(Path.Combine(program, "kept-by-user.txt")) == "mine", "The new files take the place of the old ones, even of a file that is open; other files in the folder are left alone");
                Check(File.Exists(Path.Combine(program, "KeyboardWorker.dll.kw-old")) && new StreamReader(inUse).ReadToEnd() == "old dll", "The file in use was set aside, not broken");
            }
            Check(Updater.CleanUp(program, data) >= 3 && Directory.GetFiles(program, "*.kw-old", SearchOption.AllDirectories).Length == 0 && !Directory.Exists(Path.Combine(data, "updates")) && File.Exists(Path.Combine(program, "KeyboardWorker.exe")) && Updater.CleanUp(program, data) == 0 && Updater.CleanUp(Path.Combine(home, "nowhere"), Path.Combine(home, "nowhere")) == 0, "At the next start what was set aside and the downloaded package are removed");
            // A failure half way puts everything back
            string broken = Path.Combine(home, "broken-program"), source = Path.Combine(home, "broken-staged"); Directory.CreateDirectory(broken); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(broken, "a.dll"), "old a"); Directory.CreateDirectory(Path.Combine(broken, "z.dll"));   // a folder where a file must go: writing it fails
            File.WriteAllText(Path.Combine(source, "a.dll"), "new a"); File.WriteAllText(Path.Combine(source, "m.dll"), "new m"); File.WriteAllText(Path.Combine(source, "z.dll"), "new z");
            Check(Throws(() => Updater.Apply(source, broken)) && File.ReadAllText(Path.Combine(broken, "a.dll")) == "old a" && !File.Exists(Path.Combine(broken, "m.dll")) && Directory.GetFiles(broken, "*.kw-old").Length == 0 && Directory.Exists(Path.Combine(broken, "z.dll")), "When a file cannot be written, the files already replaced are put back and nothing new is left");

            // Leftovers of an update that was never finished are tidied with the rest
            string stale = Path.Combine(home, "stale"); Directory.CreateDirectory(Path.Combine(stale, "updates")); File.WriteAllText(Path.Combine(stale, "updates", "update-9.zip"), new string('x', 300)); var moment = DateTime.Now;
            Directory.SetLastWriteTimeUtc(Path.Combine(stale, "updates"), moment.ToUniversalTime().AddDays(-3));
            Check(Housekeeping.Scan(stale, moment, 5, Path.Combine(home, "no-temp")).Single() is { Folder: true, Bytes: 300 } left && left.Path.EndsWith("updates"), "An update folder left for days counts as a leftover");
        }
        finally { Downloader.TestHandler = null; ModStore.Reload(Path.Combine(root, "mods-none")); }
        return count;
    }
    static byte[] Package(params (string Name, string Text)[] files)
    {
        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true)) foreach (var (name, text) in files) { using var writer = new StreamWriter(zip.CreateEntry(name).Open()); writer.Write(text); }
        return memory.ToArray();
    }
}
