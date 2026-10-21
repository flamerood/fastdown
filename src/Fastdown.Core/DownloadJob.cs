using System.Text.Json.Serialization;

namespace Fastdown.Core;

public enum DownloadState { Queued, Downloading, Paused, Completed, Failed }

public sealed class DownloadJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string DirectoryPath { get; set; } = "";
    public DownloadState State { get; set; } = DownloadState.Queued;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }
    public long? TotalBytes { get; set; }
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public bool SupportsRanges { get; set; }
    public int SegmentCount { get; set; } = 1;
    public string? Error { get; set; }
    public string? Referrer { get; set; }
    public string? BrowserRequestId { get; set; }
    public bool IsVideoPage { get; set; }
    public int VideoHeight { get; set; }
    public string VideoFormat { get; set; } = "mkv";
    public long QueueOrder { get; set; }
    public long SpeedLimitBytes { get; set; }
    public long DownloadedBytes;
    [JsonIgnore] public string? Stage { get; set; }
    [JsonIgnore] public double BytesPerSecond { get; set; }
    [JsonIgnore] public string TargetPath => Path.Combine(DirectoryPath, FileName);
    [JsonIgnore] public string Category => Path.GetExtension(FileName).ToLowerInvariant() switch
    {
        ".mp4" or ".mkv" or ".mov" or ".webm" or ".avi" => "Video",
        ".mp3" or ".flac" or ".wav" or ".m4a" or ".ogg" => "Müzik",
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "Arşiv",
        ".pdf" or ".docx" or ".xlsx" or ".pptx" or ".txt" or ".csv" => "Belge",
        ".exe" or ".msi" or ".msix" or ".iso" => "Program",
        _ => "Diğer"
    };
}

public sealed class DownloadSettings
{
    public string DownloadDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Fastdown");
    public int MaxConcurrent { get; set; } = 2;
    public int ConnectionsPerFile { get; set; } = 4;
    public long SpeedLimitBytes { get; set; }
    public bool NotifyOnComplete { get; set; } = true;
    public CompletionAction CompletionAction { get; set; }
    public bool AutoUpdate { get; set; } = true;
}

public enum CompletionAction { None, OpenFolder, OpenDocument }

public sealed record RemoteInfo(long? Length, bool Ranges, string? ETag, DateTimeOffset? LastModified, string? SuggestedName, string? ContentType = null);
