using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KeyboardWorker;

// The only way the program fetches files: over HTTPS, from GitHub's own servers, up to a stated size. Redirects are followed
// by hand so that every hop is checked against the same list. Nothing downloaded here is ever run by this class.
public static class Downloader
{
    public static readonly string[] Hosts = { "github.com", "api.github.com", "raw.githubusercontent.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com", "codeload.github.com" };
    public static bool Allowed(Uri? uri) => uri != null && uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 && Hosts.Contains(uri.Host.ToLowerInvariant());
    // Replaced in tests, so nothing there touches the network.
    internal static HttpMessageHandler? TestHandler { get; set; }
    static HttpClient? shared;
    static HttpClient Client => TestHandler != null ? new HttpClient(TestHandler, false) { Timeout = Timeout.InfiniteTimeSpan } : shared ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };

    static async Task<HttpResponseMessage> Open(string url, CancellationToken cancellation)
    {
        var uri = new Uri(url, UriKind.Absolute);
        for (int hop = 0; hop < 6; hop++)
        {
            if (!Allowed(uri)) throw new InvalidOperationException("GitHub 주소만 받을 수 있습니다: " + uri.Host);
            var request = new HttpRequestMessage(HttpMethod.Get, uri); request.Headers.UserAgent.ParseAdd("KeyboardWorker/" + (Version.TryParse(AppInfo.Version, out var version) ? version.ToString() : "0"));
            if (uri.Host == "api.github.com") request.Headers.Accept.ParseAdd("application/vnd.github+json");
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            if ((int)response.StatusCode is >= 301 and <= 308 && response.Headers.Location is { } next) { uri = next.IsAbsoluteUri ? next : new Uri(uri, next); response.Dispose(); continue; }
            if (!response.IsSuccessStatusCode)
            {
                int code = (int)response.StatusCode; response.Dispose();
                throw new InvalidOperationException(code == 404 ? "주소에서 파일을 찾지 못했습니다 (404). 주소가 맞는지, 저장소가 공개인지 확인해 주세요." : code is 403 or 429 ? "GitHub이 요청을 잠시 막았습니다 (요청이 많을 때 그래요). 조금 뒤에 다시 해 주세요." : "받지 못했습니다 (HTTP " + code + ").");
            }
            return response;
        }
        throw new InvalidOperationException("주소가 너무 여러 번 다른 곳으로 넘어갑니다.");
    }
    static async Task Copy(HttpResponseMessage response, Stream target, long max, Action<long, long?>? progress, CancellationToken cancellation)
    {
        long? length = response.Content.Headers.ContentLength; if (length > max) throw new InvalidOperationException("파일이 너무 큽니다.");
        await using var source = await response.Content.ReadAsStreamAsync(cancellation); var buffer = new byte[81920]; long total = 0; int read;
        while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
        {
            total += read; if (total > max) throw new InvalidOperationException("파일이 너무 큽니다.");
            await target.WriteAsync(buffer.AsMemory(0, read), cancellation); progress?.Invoke(total, length);
        }
    }
    public static async Task<string> Text(string url, long max, CancellationToken cancellation, int seconds = 30)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation); limit.CancelAfter(TimeSpan.FromSeconds(seconds));
        try { using var response = await Open(url, limit.Token); using var memory = new MemoryStream(); await Copy(response, memory, max, null, limit.Token); return System.Text.Encoding.UTF8.GetString(memory.ToArray()).TrimStart('﻿'); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { throw new InvalidOperationException("응답이 너무 늦습니다. 인터넷 연결을 확인해 주세요."); }
        catch (HttpRequestException e) { throw new InvalidOperationException("인터넷에 연결하지 못했습니다. " + e.Message); }
    }
    // Writes to "<target>.part" and renames at the end, so an interrupted download never looks like a finished one.
    public static async Task File(string url, string target, long max, Action<long, long?>? progress, CancellationToken cancellation)
    {
        string part = target + ".part"; Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using (var response = await Open(url, cancellation)) await using (var file = System.IO.File.Create(part)) await Copy(response, file, max, progress, cancellation);
            System.IO.File.Move(part, target, true);
        }
        catch (HttpRequestException e) { throw new InvalidOperationException("인터넷에 연결하지 못했습니다. " + e.Message); }
        finally { try { if (System.IO.File.Exists(part)) System.IO.File.Delete(part); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
    }
}
