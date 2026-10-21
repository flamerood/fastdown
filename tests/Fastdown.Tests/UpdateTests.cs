using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fastdown.Core;

internal static class UpdateTests
{
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("MZ verified installer fixture, never executed");
    private static string Release(string? url = null, string? digest = null, string tag = "v9.0.0", bool preview = false) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft = false, prerelease = preview,
        assets = new[] { new { name = "Fastdown-Setup.exe", browser_download_url = url ?? $"https://github.com/flamerood/fastdown/releases/download/{tag}/Fastdown-Setup.exe", size = Payload.Length, digest = digest ?? "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Payload)) } }
    });
    public static Task Metadata()
    {
        Assert(UpdateService.ParseRelease(Release(), new Version(1, 7, 0))?.Version == new Version(9, 0, 0), "New stable version not found");
        Assert(UpdateService.ParseRelease(Release(tag:"v1.6.0"), new Version(1, 7, 0)) == null, "Downgrade offered");
        Assert(UpdateService.ParseRelease(Release(preview:true), new Version(1, 7, 0)) == null, "Preview auto-installed");
        foreach (var bad in new[] { Release(url:"https://evil.example/Fastdown-Setup.exe"), Release(url:"https://github.com/other/repo/releases/download/v9.0.0/Fastdown-Setup.exe"), Release(digest:"sha256:bad") })
        { try { UpdateService.ParseRelease(bad,new Version(1,7,0)); throw new Exception("Untrusted update accepted"); } catch (InvalidDataException) { } }
        return Task.CompletedTask;
    }
    public static async Task Integrity(string area)
    {
        using var handler = new FakeHandler(Payload);
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var update = await service.CheckAsync(new Version(1, 7, 0), CancellationToken.None) ?? throw new Exception("Update missing");
        var path = await service.DownloadAsync(update, area, null, CancellationToken.None);
        Assert(await UpdateService.VerifyAsync(update,path,CancellationToken.None),"Published update not verified");
        var count=handler.Downloads;
        await service.DownloadAsync(update,area,null,CancellationToken.None);
        Assert(handler.Downloads==count,"Verified cache downloaded twice");
        await File.WriteAllTextAsync(path,"tampered");
        Assert(!await UpdateService.VerifyAsync(update,path,CancellationToken.None),"Tampered update passed install-time check");
        await service.DownloadAsync(update,area,null,CancellationToken.None);
        Assert(handler.Downloads==count+1&&await UpdateService.VerifyAsync(update,path,CancellationToken.None),"Tampered cache not replaced");
    }
    public static async Task Corruption(string area)
    {
        using var handler = new FakeHandler(Enumerable.Repeat((byte)'X',Payload.Length).ToArray());
        using var http = new HttpClient(handler);
        var service = new UpdateService(http);
        var update=UpdateService.ParseRelease(Release(),new Version(1,7,0))!;
        try { await service.DownloadAsync(update,area,null,CancellationToken.None); throw new Exception("Corrupt download published"); } catch (InvalidDataException) { }
        Assert(!Directory.EnumerateFiles(area).Any(),"Failed update left an installable or partial file");
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        try { await service.DownloadAsync(update,area,null,cancel.Token); throw new Exception("Cancelled update published"); } catch (OperationCanceledException) { }
        Assert(!Directory.EnumerateFiles(area).Any(),"Cancelled update left a partial file");
    }
    private sealed class FakeHandler(byte[] bytes) : HttpMessageHandler
    {
        public int Downloads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (request.RequestUri == UpdateService.LatestRelease)
            {
                Assert(request.Headers.UserAgent.Any(),"API User-Agent missing");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release(),Encoding.UTF8,"application/json") });
            }
            Assert(request.RequestUri!.AbsoluteUri=="https://github.com/flamerood/fastdown/releases/download/v9.0.0/Fastdown-Setup.exe","Unexpected update request");
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}
