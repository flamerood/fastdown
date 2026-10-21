using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using Fastdown.Core;

var root = Path.Combine(Environment.CurrentDirectory, ".test-data", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
await using var server = new FixtureServer();
var videoTools = Path.GetFullPath("Fastdown/tools");
if (args.Length >= 2 && args[0] == "--video-smoke")
{
    await using var manager = new DownloadManager(Path.Combine(root, "video-data"), videoTools);
    var info = await manager.InspectVideoAsync(args[1], CancellationToken.None);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(info));
    var height = args.Length > 2 ? int.Parse(args[2]) : info.Heights.Last();
    var outputFormat = args.Length > 3 ? args[3] : "mkv";
    var job = manager.Add(args[1], info.Title + "." + outputFormat, root, isVideoPage: true, videoHeight: height, videoFormat: outputFormat);
    var timeout = Stopwatch.StartNew();
    while (job.State is not (DownloadState.Completed or DownloadState.Failed))
    {
        if (timeout.Elapsed > TimeSpan.FromMinutes(3)) throw new TimeoutException("Video smoke timed out");
        await Task.Delay(100);
    }
    if (job.State != DownloadState.Completed) throw new IOException(job.Error);
    Console.WriteLine("VIDEO_OK " + job.TargetPath);
    return;
}
if (args.Contains("--browser-fixture"))
{
    using var fixtureInstance = new Mutex(true, "Local\\Akis.DownloadManager", out var fixtureOwnsInstance);
    if (!fixtureOwnsInstance) throw new InvalidOperationException("Close the desktop app before isolated browser tests; user downloads must not receive fixtures.");
    var downloads = Path.Combine(root, "browser-downloads");
    Directory.CreateDirectory(downloads);
    await using var manager = new DownloadManager(Path.Combine(root, "browser-data"), videoTools);
    manager.Settings.DownloadDirectory = downloads;
    manager.SaveSettings();
    await using var pipe = new BrowserPipeServer(manager.AcceptBrowserAsync);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { url = server.Url, downloads, library = Path.Combine(root, "browser-data", "library.json") }));
    await Console.In.ReadLineAsync();
    return;
}
var passed = 0;
async Task Test(string name, Func<Task> action)
{
    try { await action(); Console.WriteLine("PASS  " + name); passed++; }
    catch (Exception ex) { Console.Error.WriteLine("FAIL  " + name + "\n" + ex); Environment.ExitCode = 1; }
}
string Area(string name) { var path = Path.Combine(root, name); Directory.CreateDirectory(path); return path; }
DownloadJob Job(string endpoint, string folder, string name = "payload.bin") => new() { Url = server.Url + endpoint, FileName = name, DirectoryPath = folder };
void Assert(bool value, string message) { if (!value) throw new Exception(message); }
async Task CheckFile(DownloadJob job, byte[] expected)
{
    Assert(File.Exists(job.TargetPath), "Final file missing");
    var actual = await File.ReadAllBytesAsync(job.TargetPath);
    Assert(SHA256.HashData(actual).SequenceEqual(SHA256.HashData(expected)), "SHA256 mismatch");
}
async Task WaitUntil(Func<bool> condition, int milliseconds = 15000)
{
    var watch = Stopwatch.StartNew();
    while (!condition()) { if (watch.ElapsedMilliseconds > milliseconds) throw new TimeoutException("Condition did not become true"); await Task.Delay(30); }
}

await Test("HTTP range download: 4 parts and SHA256 integrity", async () =>
{
    var folder = Area("parallel");
    using var engine = new DownloadEngine(Path.Combine(folder, "parts"));
    var job = Job("range", folder);
    await engine.DownloadAsync(job, 4, CancellationToken.None);
    Assert(job.SegmentCount == 4 && job.SupportsRanges, "Expected 4 ranged connections");
    await CheckFile(job, server.Payload);
    Assert(server.Ranges.Any(r => r.Start > 0), "No ranged offset was requested");
});

await Test("Server ignores ranges: single-stream fallback", async () =>
{
    var folder = Area("no-range");
    using var engine = new DownloadEngine(Path.Combine(folder, "parts"));
    var job = Job("plain", folder);
    await engine.DownloadAsync(job, 8, CancellationToken.None);
    Assert(job.SegmentCount == 1 && !job.SupportsRanges, "Non-range server must use a single stream");
    await CheckFile(job, server.Payload);
});

await Test("Unknown content length and empty file", async () =>
{
    var folder = Area("unknown");
    using var engine = new DownloadEngine(Path.Combine(folder, "parts"));
    var unknown = Job("unknown", folder);
    await engine.DownloadAsync(unknown, 4, CancellationToken.None);
    await CheckFile(unknown, server.Payload);
    Assert(unknown.TotalBytes == server.Payload.Length, "Final size must be discovered");
    var empty = Job("empty", folder, "empty.bin");
    await engine.DownloadAsync(empty, 4, CancellationToken.None);
    await CheckFile(empty, []);
});

await Test("Pause, persist, restart and resume from existing ranges", async () =>
{
    var folder = Area("restart");
    var data = Path.Combine(folder, "data");
    Guid id;
    long partial;
    await using (var manager = new DownloadManager(data))
    {
        manager.Settings.DownloadDirectory = folder;
        var job = manager.Add(server.Url + "slow", "resume.bin");
        id = job.Id;
        await WaitUntil(() => Interlocked.Read(ref job.DownloadedBytes) > 200000);
        await manager.PauseAsync(job);
        partial = Interlocked.Read(ref job.DownloadedBytes);
        Assert(job.State == DownloadState.Paused && partial < server.Payload.Length, "Pause did not retain a partial download");
        Assert(!File.Exists(job.TargetPath), "Partial file must not be published");
    }
    var requestStart = server.Ranges.Count;
    await using (var manager = new DownloadManager(data))
    {
        var job = manager.Snapshot().Single(j => j.Id == id);
        Assert(job.State == DownloadState.Paused && job.DownloadedBytes == partial, "Restart did not restore state");
        manager.Resume(job);
        await WaitUntil(() => job.State is DownloadState.Completed or DownloadState.Failed);
        Assert(job.State == DownloadState.Completed, job.Error ?? "Resume failed");
        await CheckFile(job, server.Payload);
        var offsets = server.Ranges.Skip(requestStart).Where(r => r.End != 0).ToArray();
        Assert(offsets.Any(r => r.Start % (server.Payload.Length / 4) != 0), "Resume restarted every segment");
    }
});

await Test("Changed ETag discards old partial data", async () =>
{
    var folder = Area("changed");
    using var engine = new DownloadEngine(Path.Combine(folder, "parts"));
    var job = Job("mutable", folder);
    using (var cancel = new CancellationTokenSource())
    {
        var task = engine.DownloadAsync(job, 4, cancel.Token);
        await WaitUntil(() => Interlocked.Read(ref job.DownloadedBytes) > 200000);
        cancel.Cancel();
        try { await task; } catch (OperationCanceledException) { }
    }
    server.Version = 2;
    await engine.DownloadAsync(job, 4, CancellationToken.None);
    await CheckFile(job, server.PayloadV2);
    server.Version = 1;
});

await Test("Queue respects concurrency and pause/resume", async () =>
{
    var folder = Area("queue");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    manager.Settings.MaxConcurrent = 1;
    manager.Settings.DownloadDirectory = folder;
    var first = manager.Add(server.Url + "slow", "first.bin");
    var second = manager.Add(server.Url + "slow", "second.bin");
    await WaitUntil(() => first.DownloadedBytes > 200000);
    Assert(second.State == DownloadState.Queued && second.DownloadedBytes == 0, "Queue exceeded concurrency limit");
    await manager.PauseAsync(first);
    await WaitUntil(() => second.State == DownloadState.Downloading);
    manager.Resume(first);
    await WaitUntil(() => first.State == DownloadState.Completed && second.State == DownloadState.Completed, 20000);
    await CheckFile(first, server.Payload);
    await CheckFile(second, server.Payload);
});

await Test("Aggregate speed limiter applies to parallel segments", async () =>
{
    var folder = Area("throttle");
    using var engine = new DownloadEngine(Path.Combine(folder, "parts"), () => 2 * 1024 * 1024);
    var job = Job("range", folder);
    var watch = Stopwatch.StartNew();
    await engine.DownloadAsync(job, 8, CancellationToken.None);
    Assert(watch.Elapsed.TotalSeconds >= 2.7, "Aggregate rate exceeded 2 MB/s");
    await CheckFile(job, server.Payload);
});

await Test("Queue priority changes next job without interrupting current work and persists", async () =>
{
    var folder=Area("priority");var data=Path.Combine(folder,"data"); Guid promoted;
    await using(var manager=new DownloadManager(data))
    {
        manager.Settings.MaxConcurrent=1;
        var active=manager.Add(server.Url+"slow","active.bin",folder);
        await manager.SetJobSpeedLimitAsync(active,1024*1024);
        await WaitUntil(()=>active.DownloadedBytes>100000);
        var next=manager.Add(server.Url+"slow","second.bin",folder);
        var last=manager.Add(server.Url+"slow","promoted.bin",folder);promoted=last.Id;
        Assert(manager.MoveQueued(last,next),"Queued job could not be moved");
        Assert(!manager.MoveQueued(active,next)&&active.State==DownloadState.Downloading,"Priority interrupted active work");
        await WaitUntil(()=>last.State==DownloadState.Downloading,15000);
        Assert(next.State==DownloadState.Queued&&active.State==DownloadState.Completed,"Scheduler ignored selected priority");
        await manager.SetJobSpeedLimitAsync(last,512*1024);
    }
    await using var restored=new DownloadManager(data);
    var saved=restored.Snapshot().Single(j=>j.Id==promoted);
    Assert(saved.SpeedLimitBytes==512*1024,"Per-file limit lost after restart");
    Assert(saved.QueueOrder<restored.Snapshot().Single(j=>j.FileName=="second.bin").QueueOrder,"Priority lost after restart");
});
await Test("A per-file cap is shared by all segments and other files remain independent", async () =>
{
    var folder=Area("file-limit");using var engine=new DownloadEngine(Path.Combine(folder,"parts"));
    var slow=Job("range",folder);slow.FileName="limited.bin";slow.SpeedLimitBytes=1024*1024;
    var fast=Job("range",folder);fast.FileName="unlimited.bin";
    var watch=Stopwatch.StartNew();var limited=engine.DownloadAsync(slow,8,CancellationToken.None);
    await engine.DownloadAsync(fast,8,CancellationToken.None);
    Assert(!limited.IsCompleted,"Per-file cap multiplied by segment count or affected both files");
    await limited;Assert(watch.Elapsed.TotalSeconds>=5.5,"Per-file rate exceeded 1 MB/s");
    await CheckFile(slow,server.Payload);await CheckFile(fast,server.Payload);
});
await Test("An active HTTP download responds to a changed limit without restarting", async () =>
{
    var folder=Area("live-limit");using var engine=new DownloadEngine(Path.Combine(folder,"parts"));
    var job=Job("range",folder);job.SpeedLimitBytes=256*1024;
    var download=engine.DownloadAsync(job,8,CancellationToken.None);
    await WaitUntil(()=>job.DownloadedBytes>200000);
    var watch=Stopwatch.StartNew();job.SpeedLimitBytes=0;await download;
    Assert(watch.Elapsed.TotalSeconds<3,"Old reservations kept the download throttled");
    await CheckFile(job,server.Payload);
});
await Test("Updates reject downgrades, preview versions, foreign URLs and missing hashes", UpdateTests.Metadata);
await Test("Update download validates SHA256, reuses a verified cache and rejects later tampering", ()=>UpdateTests.Integrity(Area("updates")));
await Test("Corrupt and cancelled updates never leave an installable file", ()=>UpdateTests.Corruption(Area("bad-updates")));

await Test("404 is reported, retriable and removable", async () =>
{
    var folder = Area("error");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    var job = manager.Add(server.Url + "missing", "missing.bin", folder);
    await WaitUntil(() => job.State == DownloadState.Failed);
    Assert(job.Error?.Contains("404") == true, "Missing useful HTTP error");
    manager.Resume(job);
    await WaitUntil(() => job.State == DownloadState.Failed);
    await manager.RemoveAsync(job);
    Assert(manager.Snapshot().Length == 0, "Failed job was not removed");
});

await Test("File collision never overwrites user data", async () =>
{
    var folder = Area("collision");
    var path = Path.Combine(folder, "payload.bin");
    await File.WriteAllTextAsync(path, "keep this file");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    var job = manager.Add(server.Url + "range", "payload.bin", folder);
    Assert(job.FileName == "payload (1).bin", "Expected a unique file name");
    await WaitUntil(() => job.State == DownloadState.Completed);
    await manager.RemoveAsync(job);
    Assert(File.Exists(job.TargetPath), "Removing history deleted completed user file");
    Assert(await File.ReadAllTextAsync(path) == "keep this file", "Existing file was overwritten");
});

await Test("Unsafe URL and file names are handled", () =>
{
    foreach (var url in new[] { "file:///C:/Windows/win.ini", "ftp://example.com/a", "hello", "https://user:pass@example.com/a" })
    {
        try { DownloadEngine.ValidateUrl(url); throw new Exception("Unsafe URL accepted: " + url); } catch (ArgumentException) { }
    }
    Assert(DownloadEngine.SafeFileName("../../secret.txt") == "secret.txt", "Path traversal was not removed");
    Assert(DownloadEngine.SafeFileName("CON.exe") == "_CON.exe", "Reserved device name was not sanitized");
    Assert(!DownloadEngine.SafeFileName("bad<>\"file.exe").Contains('"'), "Invalid filename chars were accepted");
    return Task.CompletedTask;
});

await Test("Browser handoff probes, persists, deduplicates and downloads", async () =>
{
    var folder = Area("browser");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    manager.Settings.DownloadDirectory = folder;
    var request = new BrowserRequest("download", Guid.NewGuid().ToString(), server.Url + "attachment", "tarayıcı.bin");
    var reply = await manager.AcceptBrowserAsync(request, CancellationToken.None);
    var duplicate = await manager.AcceptBrowserAsync(request, CancellationToken.None);
    Assert(reply.Ok && duplicate.JobId == reply.JobId && manager.Snapshot().Length == 1, "Browser request was duplicated");
    var job = manager.Snapshot().Single();
    Assert(job.FileName == "automatic.bin", "Server Content-Disposition must override the browser's provisional file name");
    await WaitUntil(() => job.State == DownloadState.Completed);
    await CheckFile(job, server.Payload);
});

await Test("Browser referrer is forwarded to probe and all download segments", async () =>
{
    var folder = Area("browser-referrer");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    manager.Settings.DownloadDirectory = folder;
    var request = new BrowserRequest("download", Guid.NewGuid().ToString(), server.Url + "referer", "referred.bin", server.Url + "watch");
    var reply = await manager.AcceptBrowserAsync(request, CancellationToken.None);
    Assert(reply.Ok, "Referrer download was rejected");
    var job = manager.Snapshot().Single();
    await WaitUntil(() => job.State is DownloadState.Completed or DownloadState.Failed);
    Assert(job.State == DownloadState.Completed, job.Error ?? "Referrer download failed");
    await CheckFile(job, server.Payload);
});

await Test("Browser preflight rejects 404 and login HTML without accepting transfer", async () =>
{
    var folder = Area("browser-errors");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    manager.Settings.DownloadDirectory = folder;
    foreach (var endpoint in new[] { "missing", "html" })
    {
        try { await manager.AcceptBrowserAsync(new BrowserRequest("download", Guid.NewGuid().ToString(), server.Url + endpoint), CancellationToken.None); throw new Exception("Invalid preflight was accepted"); }
        catch (Exception ex) when (ex is HttpRequestException or InvalidDataException) { }
    }
    Assert(manager.Snapshot().Length == 0, "Rejected transfer left a queued job");
});

await Test("Native messaging framing handles UTF8 and rejects oversized messages", async () =>
{
    using var stream = new MemoryStream();
    var request = new BrowserRequest("download", Guid.NewGuid().ToString(), "https://example.com/resim.jpg", "Türkçe-şğü.jpg");
    await BrowserProtocol.WriteAsync(stream, request, CancellationToken.None);
    stream.Position = 0;
    var copy = await BrowserProtocol.ReadAsync<BrowserRequest>(stream, CancellationToken.None);
    Assert(copy == request, "UTF8 native frame roundtrip failed");
    using var oversized = new MemoryStream(BitConverter.GetBytes(BrowserProtocol.MaxMessageBytes + 1));
    try { await BrowserProtocol.ReadAsync<BrowserRequest>(oversized, CancellationToken.None); throw new Exception("Oversized frame accepted"); } catch (InvalidDataException) { }
    using var partial = new MemoryStream([12, 0, 0]);
    try { await BrowserProtocol.ReadAsync<BrowserRequest>(partial, CancellationToken.None); throw new Exception("Truncated frame accepted"); } catch (EndOfStreamException) { }
});

await Test("Named pipe rejects a bad request then accepts a real browser download", async () =>
{
    var folder = Area("pipe");
    await using var manager = new DownloadManager(Path.Combine(folder, "data"));
    manager.Settings.DownloadDirectory = folder;
    var pipeName = "Fastdown.Test." + Guid.NewGuid().ToString("N");
    await using var bridge = new BrowserPipeServer(manager.AcceptBrowserAsync, pipeName);
    async Task<BrowserReply> Send(BrowserRequest request)
    {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(token.Token);
        await BrowserProtocol.WriteAsync(pipe, request, token.Token);
        return await BrowserProtocol.ReadAsync<BrowserReply>(pipe, token.Token) ?? throw new Exception("Empty pipe reply");
    }
    var invalid = await Send(new BrowserRequest("download", "bad", "file:///C:/secret"));
    Assert(!invalid.Ok, "Invalid browser request accepted");
    var ping = await Send(new BrowserRequest());
    Assert(ping.Ok && ping.AppRunning, "Bridge did not recover after invalid request");
    var valid = await Send(new BrowserRequest("download", Guid.NewGuid().ToString(), server.Url + "range", "pipe.bin"));
    Assert(valid.Ok, valid.Error ?? "Pipe transfer failed");
    var job = manager.Snapshot().Single();
    await WaitUntil(() => job.State == DownloadState.Completed);
    await CheckFile(job, server.Payload);
});

await Test("Browser protocol rejects blob, manifests, unknown actions and unsafe referrers", () =>
{
    BrowserRequest[] requests = [new("erase"), new("download", "bad", server.Url), new("download", Guid.NewGuid().ToString(), "blob:https://example.com/abc"), new("download", Guid.NewGuid().ToString(), "https://example.com/master.m3u8"), new("download", Guid.NewGuid().ToString(), server.Url, Referrer: "file:///C:/secret")];
    foreach (var request in requests)
    {
        try { BrowserProtocol.Validate(request); throw new Exception("Unsafe browser request accepted"); } catch (ArgumentException) { }
    }
    return Task.CompletedTask;
});

await Test("Video metadata excludes audio, storyboards and DRM formats", () =>
{
    var info = VideoDownloader.ParseInfo("""{"title":"Örnek video","formats":[{"vcodec":"avc1","height":1080},{"vcodec":"vp9","height":1080},{"vcodec":"h264","height":720},{"vcodec":"none","height":null},{"vcodec":"images","height":2160},{"vcodec":"h264","height":4320,"has_drm":true}]}""");
    Assert(info.Title == "Örnek video" && info.Heights.SequenceEqual(new[] {1080,720}), "Invalid quality list");
    foreach (var json in new[] { """{"formats":[]}""", """{"_type":"playlist"}""", """{"is_live":true}""" })
        try { VideoDownloader.ParseInfo(json); throw new Exception("Invalid video accepted"); } catch (InvalidDataException) { }
    Assert(VideoDownloader.FormatSelector(1080) == "bv*[height=1080]+ba/b[height=1080]", "Selected resolution may not silently fall back");
    try { VideoDownloader.FormatSelector(-1); throw new Exception("Invalid height accepted"); } catch (ArgumentException) { }
    return Task.CompletedTask;
});
await Test("Video jobs preserve page URL and quality across restart without overwriting", async () =>
{
    var folder = Area("video-persist");
    Guid id;
    await using (var manager = new DownloadManager(Path.Combine(folder,"data")))
    {
        await File.WriteAllTextAsync(Path.Combine(folder,"title.mkv"), "keep");
        var job = manager.Add("https://www.youtube.com/watch?v=jNQXAC9IVRw", "title.mkv", folder, false, videoHeight:240);
        Assert(job.IsVideoPage && job.FileName == "title (1).mkv", "Video routing or collision failed");
        id = job.Id;
    }
    await using var restored = new DownloadManager(Path.Combine(folder,"data"));
    var saved = restored.Snapshot().Single(j=>j.Id == id);
    Assert(saved.IsVideoPage && saved.VideoHeight == 240 && saved.State == DownloadState.Paused && saved.Url.Contains("youtube.com/watch"), "Page or selected quality was not persisted");
});
await Test("Video protocol rejects invalid quality, URLs and shell commands", () =>
{
    BrowserProtocol.Validate(new BrowserRequest("inspect-video", Url: "https://www.youtube.com/watch?v=jNQXAC9IVRw"));
    foreach (var request in new[] {
        new BrowserRequest("video-download", Guid.NewGuid().ToString(), "https://example.com/video", VideoHeight:-4),
        new BrowserRequest("inspect-video", Url:"file:///C:/test"),
        new BrowserRequest("execute", Url:"https://example.com"),
        new BrowserRequest("video-download", "not-a-guid", "https://example.com/video") })
        try { BrowserProtocol.Validate(request); throw new Exception("Invalid video command accepted"); } catch (ArgumentException) { }
    Assert(VideoDownloader.UserError("Sign in to confirm you're not a bot").Contains("doğrulama"), "Site challenge message missing");
    return Task.CompletedTask;
});
await Test("Shutdown countdown is session-only, waits for success and executes once", () =>
{
    var policy = new ShutdownCountdown(); var now=DateTimeOffset.UtcNow;
    var old = new DownloadJob {State=DownloadState.Completed};
    Assert(!policy.Tick([old],now).Shutdown && !policy.Armed,"Shutdown armed itself on startup");
    policy.Arm();Assert(policy.Tick([old],now).Seconds==null,"Old history started shutdown");
    var job = new DownloadJob {State=DownloadState.Downloading};policy.Tick([old,job],now);
    job.State=DownloadState.Paused;Assert(policy.Tick([job],now.AddMinutes(2)).Seconds==null,"Paused queue triggered shutdown");
    job.State=DownloadState.Failed;Assert(!policy.Tick([job],now.AddMinutes(3)).Shutdown,"Failed queue triggered shutdown");
    job.State=DownloadState.Completed;Assert(policy.Tick([job],now.AddMinutes(4)).Seconds==60,"Missing cancellation window");
    Assert(policy.Tick([job],now.AddMinutes(4).AddSeconds(59)).Seconds==1,"Countdown wrong");
    Assert(policy.Tick([job],now.AddMinutes(5)).Shutdown,"Finished queue did not trigger");
    Assert(!policy.Tick([job],now.AddMinutes(6)).Shutdown && !policy.Armed,"Shutdown fired twice");
    return Task.CompletedTask;
});
await Test("New work resets shutdown countdown and cancel/removing all jobs never powers off", () =>
{
    var policy=new ShutdownCountdown();var now=DateTimeOffset.UtcNow;
    var job=new DownloadJob {State=DownloadState.Queued};policy.Arm();policy.Tick([job],now);
    job.State=DownloadState.Completed;policy.Tick([job],now);
    var next=new DownloadJob {State=DownloadState.Downloading};Assert(policy.Tick([job,next],now.AddSeconds(59)).Seconds==null,"New job did not reset countdown");
    next.State=DownloadState.Completed;Assert(policy.Tick([job,next],now.AddSeconds(61)).Seconds==60,"Countdown did not restart");
    policy.Cancel();Assert(!policy.Tick([job,next],now.AddMinutes(5)).Shutdown,"Cancel ignored");
    policy.Arm();job.State=DownloadState.Queued;policy.Tick([job],now);job.State=DownloadState.Completed;
    var alreadyPaused=new DownloadJob {State=DownloadState.Paused};
    Assert(policy.Tick([job,alreadyPaused],now.AddHours(1)).Seconds==null,"Pre-existing paused job did not block shutdown");
    alreadyPaused.State=DownloadState.Failed;Assert(policy.Tick([job,alreadyPaused],now.AddHours(2)).Seconds==null,"Pre-existing failed job did not block shutdown");
    policy.Arm();job.State=DownloadState.Queued;policy.Tick([job],now);Assert(policy.Tick([],now.AddHours(1)).Seconds==null,"Removing everything caused shutdown");
    foreach(var file in new[]{"setup.exe","commands.cmd","run.ps1","test.bat","page.html","code.js","setup.msi"})Assert(!ShutdownCountdown.CanOpenDocument(file),"Automatic executable launch allowed");
    Assert(ShutdownCountdown.CanOpenDocument("song.MP3")&&ShutdownCountdown.CanOpenDocument("video.mp4"),"Media open not available");
    return Task.CompletedTask;
});
await Test("Audio output ignores video resolution and output/notification settings persist", async () =>
{
    var folder=Area("audio-persist");
    await using(var manager=new DownloadManager(Path.Combine(folder,"data")))
    {
        var job=manager.Add("https://www.youtube.com/watch?v=jNQXAC9IVRw","song.mkv",folder,false,isVideoPage:true,videoHeight:1080,videoFormat:"mp3");
        Assert(job.VideoHeight==0&&job.VideoFormat=="mp3"&&job.FileName=="song.mp3","Audio job kept video selection");
        manager.Settings.NotifyOnComplete=false;manager.Settings.CompletionAction=CompletionAction.OpenFolder;manager.SaveSettings();
    }
    await using var restored=new DownloadManager(Path.Combine(folder,"data"));
    Assert(restored.Snapshot().Single().VideoFormat=="mp3"&&!restored.Settings.NotifyOnComplete&&restored.Settings.CompletionAction==CompletionAction.OpenFolder,"Output settings not preserved");
    try{BrowserProtocol.Validate(new BrowserRequest("video-download",Guid.NewGuid().ToString(),"https://example.com/page",VideoFormat:"mp3 --exec calc.exe"));throw new Exception("Invalid format accepted");}catch(ArgumentException){}
});
if (args.Contains("--video-suite"))
{
    foreach(var outputFormat in new[]{"mp4","mp3","m4a"})
    await Test("Real download produces "+outputFormat+" with the expected streams", async () =>
    {
        var folder=Area("output-"+outputFormat);
        await using var manager=new DownloadManager(Path.Combine(folder,"data"),videoTools);
        manager.Settings.DownloadDirectory=folder;
        var reply=await manager.AcceptBrowserAsync(new BrowserRequest("video-download",Guid.NewGuid().ToString(),server.Url+"media/manifest.mpd",VideoHeight:720,VideoFormat:outputFormat),CancellationToken.None);
        Assert(reply.Ok,"Output handoff failed");var job=manager.Snapshot().Single();
        await WaitUntil(()=>job.State is DownloadState.Completed or DownloadState.Failed,60000);
        Assert(job.State==DownloadState.Completed,job.Error??"Output failed");
        Assert(Path.GetExtension(job.TargetPath)=="."+outputFormat,"Wrong output extension");
        var start=new ProcessStartInfo(Path.Combine(videoTools,"ffprobe.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
        foreach(var arg in new[]{"-v","error","-show_streams","-show_format","-of","json",job.TargetPath})start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var json=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
        using var document=System.Text.Json.JsonDocument.Parse(json);
        var streams=document.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert(streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"),"Missing audio");
        Assert(streams.Any(s=>s.GetProperty("codec_type").GetString()=="video")== (outputFormat=="mp4"),"Audio-only output contains video or MP4 lost video");
        if(outputFormat=="mp4")Assert(streams.Single(s=>s.GetProperty("codec_type").GetString()=="video").GetProperty("height").GetInt32()==720,"Wrong MP4 resolution");
        if(outputFormat=="mp3")Assert(streams.Single().GetProperty("codec_name").GetString()=="mp3","Wrong MP3 codec");
        if(outputFormat=="m4a")Assert(streams.Single().GetProperty("codec_name").GetString()=="aac","Wrong M4A codec");
    });
    await Test("Real yt-dlp + FFmpeg download and merge selected 720p with audio", async () =>
    {
        var folder = Area("video-merge");
        await using var manager = new DownloadManager(Path.Combine(folder,"data"), videoTools);
        manager.Settings.DownloadDirectory = folder;
        var url = server.Url + "media/manifest.mpd";
        var inspected = await manager.AcceptBrowserAsync(new BrowserRequest("inspect-video", Url:url), CancellationToken.None);
        Assert(inspected.Video!.Heights.SequenceEqual(new[]{1080,720}), "Unplayed DASH alternatives were not discovered");
        var request = new BrowserRequest("video-download", Guid.NewGuid().ToString(), url, VideoHeight:720);
        var reply = await manager.AcceptBrowserAsync(request, CancellationToken.None);
        var duplicate = await manager.AcceptBrowserAsync(request, CancellationToken.None);
        Assert(reply.Ok && duplicate.JobId == reply.JobId && manager.Snapshot().Length == 1, "Video handoff was duplicated");
        var job = manager.Snapshot().Single();
        await WaitUntil(()=>job.State is DownloadState.Completed or DownloadState.Failed,60000);
        Assert(job.State == DownloadState.Completed, job.Error ?? "Video merge failed");
        var probe = new ProcessStartInfo(Path.Combine(videoTools,"ffprobe.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true};
        foreach(var a in new[]{"-v","error","-show_streams","-of","json",job.TargetPath})probe.ArgumentList.Add(a);
        using var process=Process.Start(probe)!;
        var output=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
        using var doc=System.Text.Json.JsonDocument.Parse(output);
        var streams=doc.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert(streams.Any(s=>s.GetProperty("codec_type").GetString()=="video" && s.GetProperty("height").GetInt32()==720),"Wrong video quality");
        Assert(streams.Any(s=>s.GetProperty("codec_type").GetString()=="audio"),"Merged file has no audio");
        Assert(job.TotalBytes==new FileInfo(job.TargetPath).Length && job.DownloadedBytes==job.TotalBytes,"Final size incorrect");
        await manager.RemoveAsync(job);
        Assert(File.Exists(job.TargetPath),"Removing history deleted final video");
    });
    await Test("Video cancellation stops the process and leaves no published partial file", async () =>
    {
        var folder = Area("video-cancel");
        var engine = new VideoDownloader(Path.Combine(folder,"parts"),videoTools);
        var job = new DownloadJob {Url=server.Url+"media/manifest.mpd",FileName="cancel.mkv",DirectoryPath=folder,IsVideoPage=true,VideoHeight=720};
        using var cancel=new CancellationTokenSource();
        var task=engine.DownloadAsync(job,1024,cancel.Token);
        await Task.Delay(1500);cancel.Cancel();
        try {await task;throw new Exception("Cancellation did not stop yt-dlp");}catch(OperationCanceledException){}
        Assert(!File.Exists(job.TargetPath),"Partial video published");
        engine.DeleteParts(job);
    });
}
Console.WriteLine($"\n{passed} integration tests passed. Fixtures: {root}");

sealed class FixtureServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _serve;
    public byte[] Payload { get; } = new byte[6 * 1024 * 1024];
    public byte[] PayloadV2 { get; } = new byte[6 * 1024 * 1024];
    public int Version { get; set; } = 1;
    public ConcurrentQueue<(long Start, long End)> Ranges { get; } = new();
    public string Url { get; }
    public FixtureServer()
    {
        new Random(42).NextBytes(Payload);
        new Random(84).NextBytes(PayloadV2);
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}/";
        _serve = ServeAsync();
    }
    private async Task ServeAsync()
    {
        try { while (!_cancel.IsCancellationRequested) { var client = await _listener.AcceptTcpClientAsync(_cancel.Token); _ = HandleAsync(client); } }
        catch (OperationCanceledException) { }
    }
    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        try
        {
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            var first = await reader.ReadLineAsync();
            if (first == null) return;
            var route = first.Split(' ')[1].Trim('/');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadLineAsync() is { Length: > 0 } line) { var split = line.IndexOf(':'); if (split > 0) headers[line[..split]] = line[(split + 1)..].Trim(); }
            if (route == "media/manifest.mpd")
            {
                var xml = """<MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT2S" minBufferTime="PT1S"><Period><AdaptationSet mimeType="video/mp4" contentType="video"><Representation id="720" width="1280" height="720" codecs="avc1.42C01F" bandwidth="100000"><BaseURL>split-video-720.mp4</BaseURL></Representation><Representation id="1080" width="1920" height="1080" codecs="avc1.42C028" bandwidth="200000"><BaseURL>split-video-1080.mp4</BaseURL></Representation></AdaptationSet><AdaptationSet mimeType="audio/mp4" contentType="audio"><Representation id="audio" codecs="mp4a.40.2" bandwidth="64000" audioSamplingRate="44100"><BaseURL>split-audio.m4a</BaseURL></Representation></AdaptationSet></Period></MPD>""";
                var bytes=Encoding.UTF8.GetBytes(xml);
                await WriteHeader(stream,"200 OK",$"Content-Type: application/dash+xml\r\nContent-Length: {bytes.Length}\r\n");
                await stream.WriteAsync(bytes);return;
            }
            if (route.StartsWith("media/split-",StringComparison.Ordinal))
            {
                var file=Path.GetFileName(route);
                if (file is not ("split-video-720.mp4" or "split-video-1080.mp4" or "split-audio.m4a")) {await WriteHeader(stream,"404 Not Found","Content-Length: 0\r\n");return;}
                var bytes=await File.ReadAllBytesAsync(Path.Combine("tests","browser","fixtures",file));
                await WriteHeader(stream,"200 OK",$"Content-Type: {(file.EndsWith("m4a") ? "audio/mp4" : "video/mp4")}\r\nContent-Length: {bytes.Length}\r\n");
                await stream.WriteAsync(bytes);return;
            }
            var payload = route == "mutable" && Version == 2 ? PayloadV2 : Payload;
            var etag = route == "mutable" ? $"\"version-{Version}\"" : "\"fixture-v1\"";
            if (route == "missing") { await WriteHeader(stream, "404 Not Found", "Content-Length: 0\r\n"); return; }
            if (route == "referer" && (!headers.TryGetValue("Referer", out var referer) || referer != Url + "watch")) { await WriteHeader(stream, "403 Forbidden", "Content-Length: 0\r\n"); return; }
            if (route == "html") { await WriteHeader(stream, "200 OK", "Content-Type: text/html\r\nContent-Length: 5\r\n"); await stream.WriteAsync(Encoding.UTF8.GetBytes("login")); return; }
            if (route == "empty") { await WriteHeader(stream, "416 Range Not Satisfiable", "Content-Range: bytes */0\r\nContent-Length: 0\r\n"); return; }
            long from = 0, to = payload.Length - 1;
            var ranged = route is not "plain" and not "unknown" && headers.TryGetValue("Range", out _);
            if (ranged)
            {
                var range = headers["Range"].Replace("bytes=", "").Split('-');
                from = long.Parse(range[0]); to = string.IsNullOrEmpty(range[1]) ? payload.Length - 1 : long.Parse(range[1]);
                Ranges.Enqueue((from, to));
            }
            var length = to - from + 1;
            var meta = $"ETag: {etag}\r\nContent-Type: application/octet-stream\r\n";
            if (route == "attachment") meta += "Content-Disposition: attachment; filename=\"automatic.bin\"\r\n";
            if (ranged) meta += $"Content-Range: bytes {from}-{to}/{payload.Length}\r\n";
            if (route != "unknown") meta += $"Content-Length: {length}\r\n";
            await WriteHeader(stream, ranged ? "206 Partial Content" : "200 OK", meta);
            for (var offset = from; offset <= to;)
            {
                var count = (int)Math.Min(16384, to - offset + 1);
                await stream.WriteAsync(payload.AsMemory((int)offset, count), _cancel.Token);
                offset += count;
                if ((route is "slow" or "mutable" or "attachment") && length > 1) await Task.Delay(8, _cancel.Token);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or SocketException) { }
    }
    private static Task WriteHeader(NetworkStream stream, string status, string headers) => stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nConnection: close\r\n{headers}\r\n")).AsTask();
    public async ValueTask DisposeAsync() { await _cancel.CancelAsync(); _listener.Stop(); await _serve; _cancel.Dispose(); }
}
