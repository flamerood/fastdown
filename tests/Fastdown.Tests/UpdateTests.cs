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
    public static async Task Resume(string area)
    {
        var bytes=new byte[512*1024];new Random(52).NextBytes(bytes);
        var update=new AppUpdate(new Version(9,1,0),new Uri("https://github.com/flamerood/fastdown/releases/download/v9.1.0/Fastdown-Setup.exe"),bytes.Length,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        using var handler=new ResumeHandler(bytes);using var http=new HttpClient(handler);
        using(var cancel=new CancellationTokenSource())
        {
            var progress=new InlineProgress(value=>{if(value>0&&value<100)cancel.Cancel();});
            try{await new UpdateService(http).DownloadAsync(update,area,progress,cancel.Token);throw new Exception("Interrupted download finished");}catch(OperationCanceledException){}
        }
        var partial=Directory.EnumerateFiles(area,"*.partial").Single();var retained=new FileInfo(partial).Length;
        Assert(retained>0&&retained<bytes.Length,"Shutdown did not retain downloaded bytes");
        var path=await new UpdateService(http).DownloadAsync(update,area,null,CancellationToken.None);
        Assert(handler.Ranges.Last()==retained,"New updater did not resume at the persisted offset");
        Assert(await UpdateService.VerifyAsync(update,path,CancellationToken.None),"Resumed installer hash differs");
        Assert(!File.Exists(partial),"Verified partial was not finalized");
    }
    public static async Task IgnoredRange(string area)
    {
        var bytes=new byte[256*1024];new Random(63).NextBytes(bytes);
        var update=new AppUpdate(new Version(9,2,0),new Uri("https://github.com/flamerood/fastdown/releases/download/v9.2.0/Fastdown-Setup.exe"),bytes.Length,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Directory.CreateDirectory(area);var partial=Path.Combine(area,$"Fastdown-Setup-{update.Version}.exe.{update.Sha256}.partial");
        await File.WriteAllBytesAsync(partial,bytes[..65536]);
        using var handler=new ResumeHandler(bytes){IgnoreRange=true};using var http=new HttpClient(handler);
        var path=await new UpdateService(http).DownloadAsync(update,area,null,CancellationToken.None);
        Assert(await UpdateService.VerifyAsync(update,path,CancellationToken.None),"Ignored Range response was appended instead of replacing the partial");
        File.Move(path,partial);var count=handler.Ranges.Count;
        path=await new UpdateService(http).DownloadAsync(update,area,null,CancellationToken.None);
        Assert(handler.Ranges.Count==count&&File.Exists(path),"Complete pending installer downloaded again after restart");
    }
    public static async Task BadResume(string area)
    {
        var bytes=new byte[256*1024];new Random(64).NextBytes(bytes);
        var update=new AppUpdate(new Version(9,3,0),new Uri("https://github.com/flamerood/fastdown/releases/download/v9.3.0/Fastdown-Setup.exe"),bytes.Length,Convert.ToHexStringLower(SHA256.HashData(bytes)));
        Directory.CreateDirectory(area);var partial=Path.Combine(area,$"Fastdown-Setup-{update.Version}.exe.{update.Sha256}.partial");
        await File.WriteAllBytesAsync(partial,bytes[..65536]);
        using var handler=new ResumeHandler(bytes){WrongOffset=true};using var http=new HttpClient(handler);
        try{await new UpdateService(http).DownloadAsync(update,area,null,CancellationToken.None);throw new Exception("Incorrect resume offset accepted");}catch(InvalidDataException){}
        Assert(!Directory.EnumerateFiles(area).Any(),"Invalid resumed installer was retained");
    }
    public static Task RetrySchedule()
    {
        var now=DateTimeOffset.UtcNow;var schedule=new UpdateCheckSchedule(now);
        schedule.Complete(false,now);Assert(schedule.NextCheck==now.AddMinutes(5),"First error postponed updates for twelve hours");
        schedule.Complete(false,now);Assert(schedule.NextCheck==now.AddMinutes(10),"Retry backoff missing");
        for(var i=0;i<10;i++)schedule.Complete(false,now);
        Assert(schedule.NextCheck<=now.AddHours(1),"Retries stopped after repeated errors");
        schedule.Complete(true,now);Assert(schedule.NextCheck==now.AddHours(12),"Successful polling does not back off");
        schedule.Complete(false,now);Assert(schedule.NextCheck==now.AddMinutes(5),"Success did not reset retry policy");
        return Task.CompletedTask;
    }
    private sealed class InlineProgress(Action<double> report) : IProgress<double> { public void Report(double value)=>report(value); }
    private sealed class ResumeHandler(byte[] bytes) : HttpMessageHandler
    {
        public List<long> Ranges {get;}=[];
        public bool IgnoreRange {get;init;}
        public bool WrongOffset {get;init;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();var offset=request.Headers.Range?.Ranges.Single().From??0;Ranges.Add(offset);
            var response=new HttpResponseMessage(offset>0&&!IgnoreRange?HttpStatusCode.PartialContent:HttpStatusCode.OK){Content=new ByteArrayContent(bytes[(int)(IgnoreRange?0:offset)..])};
            if(response.StatusCode==HttpStatusCode.PartialContent)response.Content.Headers.ContentRange=new System.Net.Http.Headers.ContentRangeHeaderValue(WrongOffset?offset+1:offset,bytes.Length-1,bytes.Length);
            return Task.FromResult(response);
        }
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
