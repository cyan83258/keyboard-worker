using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace KeyboardWorkerSetup
{
    // What the setup program does, without any window: find the newest release, download it, check it, unpack it, make shortcuts.
    public static class SetupCore
    {
        public const string Repository = "cyan83258/keyboard-worker", Package = "KeyboardWorker-win-x64.zip", Program = "KeyboardWorker.exe", Title = "Keyboard Worker";
        const long MaxDownload = 400L * 1024 * 1024;
        public static string DefaultFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "KeyboardWorker");
        public sealed class Release { public string Version = "", Url = "", Sha256 = ""; public long Size; }

        static HttpClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("KeyboardWorker-Setup");
            return client;
        }
        // Only GitHub's own addresses are ever downloaded from.
        public static bool Trusted(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host == "github.com" || uri.Host == "api.github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));

        // Reads GitHub's answer about the newest release. Null when it holds no package.
        public static Release ParseRelease(string json)
        {
            if (!(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.DeserializeObject(json) is Dictionary<string, object> root)) return null;
            if (!root.TryGetValue("assets", out object list) || !(list is object[] assets)) return null;
            foreach (var asset in assets.OfType<Dictionary<string, object>>())
            {
                if (!asset.TryGetValue("name", out object name) || !string.Equals(name as string, Package, StringComparison.OrdinalIgnoreCase)) continue;
                var release = new Release { Version = (root.TryGetValue("tag_name", out object tag) ? tag as string ?? "" : "").TrimStart('v', 'V'), Url = asset.TryGetValue("browser_download_url", out object url) ? url as string ?? "" : "" };
                if (asset.TryGetValue("size", out object size) && size != null) release.Size = Convert.ToInt64(size);
                if (asset.TryGetValue("digest", out object digest) && digest is string text && text.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) release.Sha256 = text.Substring(7).Trim().ToLowerInvariant();
                return Trusted(release.Url) ? release : null;
            }
            return null;
        }
        // The newest release. When GitHub's list cannot be read (it limits how often one may ask), the fixed "latest" address is used instead.
        public static async Task<Release> Find(CancellationToken ct)
        {
            try
            {
                using (var client = Client())
                using (var answer = await client.GetAsync("https://api.github.com/repos/" + Repository + "/releases/latest", ct))
                    if (answer.IsSuccessStatusCode && ParseRelease(await answer.Content.ReadAsStringAsync()) is Release found) return found;
            }
            catch (HttpRequestException) { }
            return new Release { Url = "https://github.com/" + Repository + "/releases/latest/download/" + Package };
        }
        public static async Task Download(Release release, string file, Action<long, long> progress, CancellationToken ct)
        {
            using (var client = Client())
            using (var answer = await client.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!Trusted(answer.RequestMessage.RequestUri.ToString())) throw new InvalidOperationException("내려받는 주소가 GitHub가 아닙니다.");
                if (answer.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("아직 공개된 버전이 없습니다.");
                answer.EnsureSuccessStatusCode();
                long total = answer.Content.Headers.ContentLength ?? release.Size, done = 0; var buffer = new byte[81920];
                using (var from = await answer.Content.ReadAsStreamAsync())
                using (var to = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None))
                    for (int read; (read = await from.ReadAsync(buffer, 0, buffer.Length, ct)) > 0;)
                    {
                        if ((done += read) > MaxDownload) throw new InvalidOperationException("내려받는 파일이 너무 큽니다.");
                        await to.WriteAsync(buffer, 0, read, ct); progress?.Invoke(done, total);
                    }
            }
            if (release.Sha256.Length == 0) return;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(file))
                if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != release.Sha256)
                    throw new InvalidOperationException("내려받은 파일이 손상됐습니다. 다시 시도해 주세요.");
        }
        public static bool Running(string folder)
        {
            string exe = Path.Combine(folder, Program);
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Program)))
                try { if (string.Equals(process.MainModule.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true; }
                catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception) { }
                finally { process.Dispose(); }
            return false;
        }
        // Unpacks the package over the folder. The package holds one top folder, which is left out; nothing may land outside the folder.
        public static int Unpack(string zip, string folder)
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; int files = 0;
            using (var archive = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read, false, Encoding.UTF8))
            {
                if (!archive.Entries.Any(e => e.FullName.Replace('\\', '/').EndsWith("/" + Program, StringComparison.OrdinalIgnoreCase) || e.FullName == Program))
                    throw new InvalidOperationException("내려받은 파일에 프로그램이 들어 있지 않습니다.");
                Directory.CreateDirectory(root);
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/'); int slash = name.IndexOf('/');
                    if (name.StartsWith("KeyboardWorker/", StringComparison.OrdinalIgnoreCase)) name = name.Substring(slash + 1);
                    if (name.Length == 0 || name.EndsWith("/")) continue;
                    string target = Path.GetFullPath(Path.Combine(root, name));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("내려받은 파일의 구성이 올바르지 않습니다.");
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true); files++;
                }
            }
            return files;
        }
        public static void Shortcuts(string folder, bool desktop)
        {
            var places = new List<string> { Environment.GetFolderPath(Environment.SpecialFolder.Programs) };
            if (desktop) places.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            foreach (string place in places)
            {
                dynamic link = shell.CreateShortcut(Path.Combine(place, Title + ".lnk"));
                link.TargetPath = Path.Combine(folder, Program); link.WorkingDirectory = folder; link.Description = Title; link.Save();
            }
        }
        // The whole installation. Returns the installed version ("" when GitHub did not say).
        public static async Task<string> Install(string folder, bool shortcuts, bool desktop, Action<string> status, Action<long, long> progress, CancellationToken ct)
        {
            if (Running(folder)) throw new InvalidOperationException("Keyboard Worker가 실행 중입니다. 캐릭터를 우클릭해 종료한 뒤 다시 눌러 주세요.");
            status?.Invoke("최신 버전을 찾는 중...");
            var release = await Find(ct);
            string zip = Path.Combine(Path.GetTempPath(), "KeyboardWorker-Setup-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                status?.Invoke("내려받는 중...");
                await Download(release, zip, progress, ct);
                status?.Invoke("설치하는 중...");
                await Task.Run(() => Unpack(zip, folder), ct);
                if (shortcuts) Shortcuts(folder, desktop);
            }
            finally { try { File.Delete(zip); } catch (IOException) { } }
            return release.Version;
        }
    }
}
