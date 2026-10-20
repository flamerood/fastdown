namespace Fastdown.Core;

/// <summary>Session-only queue completion policy. It never invokes the operating system.</summary>
public sealed class ShutdownCountdown
{
    private readonly HashSet<Guid> _watched = [];
    private DateTimeOffset? _due;
    public bool Armed { get; private set; }
    public void Arm() { _watched.Clear(); _due = null; Armed = true; }
    public void Cancel() { Armed = false; _due = null; _watched.Clear(); }

    public (int? Seconds, bool Shutdown) Tick(IEnumerable<DownloadJob> source, DateTimeOffset now)
    {
        if (!Armed) return (null, false);
        var jobs = source.ToArray();
        foreach (var job in jobs.Where(j => j.State is DownloadState.Queued or DownloadState.Downloading)) _watched.Add(job.Id);
        var targets = jobs.Where(j => _watched.Contains(j.Id)).ToArray();
        // Paused/failed jobs cannot be mistaken for a successfully finished queue. Removing every job is not success either.
        if (targets.Length == 0 || targets.Any(j => j.State != DownloadState.Completed) || jobs.Any(j => j.State is DownloadState.Paused or DownloadState.Failed)) { _due = null; return (null, false); }
        _due ??= now.AddSeconds(60);
        var seconds = Math.Max(0, (int)Math.Ceiling((_due.Value - now).TotalSeconds));
        if (seconds > 0) return (seconds, false);
        Cancel();
        return (0, true);
    }

    public static bool CanOpenDocument(string path) => new[] { ".mp4", ".mkv", ".webm", ".mov", ".avi", ".mp3", ".m4a", ".wav", ".flac", ".ogg", ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf", ".txt" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
