using System.Collections.Concurrent;
using Fastdown.Core;

internal static class VideoBudgetTests
{
    public static async Task Run(string area, string url)
    {
        var video=new FakeVideo();await using var manager=new DownloadManager(Path.Combine(area,"data"),videoDownloader:video);
        await manager.SetTransferSettingsAsync(5000L*1024,5,4);
        var job=manager.Add("https://example.test/video","video.mp4",area,isVideoPage:true);
        await Until(()=>video.Starts.Any(s=>s.Limit==5000L*1024));
        var file=manager.Add(url,"file.bin",area);
        await Until(()=>video.Starts.Any(s=>s.Limit==2500L*1024));
        await Until(()=>file.State==DownloadState.Completed);
        await Until(()=>video.Starts.Count(s=>s.Limit==5000L*1024)>=2);
        await manager.SetJobSpeedLimitAsync(job,1000L*1024);
        await Until(()=>video.Starts.Any(s=>s.Limit==1000L*1024));
        await manager.PauseAsync(job);var count=video.Starts.Count;await Task.Delay(600);
        if(job.State!=DownloadState.Paused||video.Starts.Count!=count)throw new Exception("User pause was overwritten by automatic rate rebalancing");
    }
    private static async Task Until(Func<bool> condition)
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while(!condition())await Task.Delay(25,timeout.Token);
    }
    private sealed class FakeVideo : IVideoDownloader
    {
        public ConcurrentQueue<(Guid Job,long Limit)> Starts {get;}=[];
        public Task<VideoInfo> InspectAsync(string url,CancellationToken token)=>Task.FromResult(new VideoInfo("Test video",[720]));
        public Task DownloadAsync(DownloadJob job,long speedLimit,CancellationToken token){Starts.Enqueue((job.Id,speedLimit));return Task.Delay(Timeout.Infinite,token);}
        public void DeleteParts(DownloadJob job){}
    }
}
