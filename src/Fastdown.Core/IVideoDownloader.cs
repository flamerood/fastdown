namespace Fastdown.Core;

public interface IVideoDownloader
{
    Task<VideoInfo> InspectAsync(string url, CancellationToken token);
    Task DownloadAsync(DownloadJob job, long speedLimit, CancellationToken token);
    void DeleteParts(DownloadJob job);
}
