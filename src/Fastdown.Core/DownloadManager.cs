using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fastdown.Core;

public sealed class DownloadManager : IAsyncDisposable
{
    private sealed class Library
    {
        public DownloadSettings Settings { get; set; } = new();
        public List<DownloadJob> Jobs { get; set; } = [];
    }
    private sealed record Running(CancellationTokenSource Cancellation, Task Task);
    private readonly object _gate = new();
    private readonly string _libraryPath;
    private readonly Dictionary<Guid, Running> _running = [];
    private readonly List<DownloadJob> _jobs;
    private readonly Timer _timer;
    private readonly DownloadEngine _engine;
    private readonly VideoDownloader _video;
    private readonly bool _schedulerEnabled;
    private bool _closing;
    private bool _reconfiguring;
    private DateTime _lastSave = DateTime.UtcNow;
    private readonly Dictionary<Guid, (long Bytes, DateTime Time)> _samples = [];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IncludeFields = true, Converters = { new JsonStringEnumConverter() } };
    public DownloadSettings Settings { get; }
    public string? PersistenceError { get; private set; }

    public DownloadManager(string dataDirectory, string? videoToolsDirectory = null, bool schedulerEnabled = true)
    {
        _schedulerEnabled = schedulerEnabled;
        Directory.CreateDirectory(dataDirectory);
        _libraryPath = Path.Combine(dataDirectory, "library.json");
        Library library;
        try { library = File.Exists(_libraryPath) ? JsonSerializer.Deserialize<Library>(File.ReadAllText(_libraryPath), JsonOptions) ?? new() : new(); }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Preserve the user's damaged library for recovery instead of overwriting it silently.
            var backup = _libraryPath + ".recovery-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            if (File.Exists(_libraryPath)) File.Copy(_libraryPath, backup, false);
            PersistenceError = "Önceki indirme listesi okunamadı; kurtarma kopyası saklandı.";
            library = new();
        }
        Settings = library.Settings;
        Settings.MaxConcurrent = Math.Clamp(Settings.MaxConcurrent, 1, 5);
        Settings.ConnectionsPerFile = Math.Clamp(Settings.ConnectionsPerFile, 1, 8);
        Settings.SpeedLimitBytes = Math.Clamp(Settings.SpeedLimitBytes, 0, 10_000_000L * 1024);
        if (!Enum.IsDefined(Settings.CompletionAction)) Settings.CompletionAction = CompletionAction.None;
        _jobs = library.Jobs;
        if (_jobs.All(j => j.QueueOrder == 0)) for (var i = 0; i < _jobs.Count; i++) _jobs[i].QueueOrder = i + 1;
        foreach (var job in _jobs)
        {
            job.SpeedLimitBytes = Math.Clamp(job.SpeedLimitBytes, 0, 10_000_000L * 1024);
            if (job.State is DownloadState.Downloading or DownloadState.Queued) job.State = DownloadState.Paused;
            job.BytesPerSecond = 0;
        }
        _engine = new DownloadEngine(Path.Combine(dataDirectory, "parts"), () =>
        {
            var limit = Settings.SpeedLimitBytes;
            if (limit <= 0) return 0;
            lock (_gate)
            {
                var reserved = _jobs.Where(j => j.IsVideoPage && _running.ContainsKey(j.Id)).Sum(EffectiveVideoLimit);
                return Math.Max(1, limit - reserved);
            }
        });
        _video = new VideoDownloader(Path.Combine(dataDirectory, "video-parts"), videoToolsDirectory);
        _timer = new Timer(Tick, null, 250, 250);
    }

    public DownloadJob[] Snapshot() { lock (_gate) return _jobs.ToArray(); }

    public DownloadJob Add(string url, string? name = null, string? directory = null, bool startNow = true, string? referrer = null, string? browserRequestId = null, bool isVideoPage = false, int videoHeight = 0, string videoFormat = "mkv")
    {
        var uri = DownloadEngine.ValidateUrl(url);
        VideoDownloader.ValidateHeight(videoHeight);
        VideoDownloader.ValidateFormat(videoFormat);
        if (VideoDownloader.AudioOnly(videoFormat)) videoHeight = 0;
        isVideoPage |= VideoDownloader.IsYouTube(url);
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? "indirme"); }
            catch (UriFormatException) { name = "indirme"; }
        }
        var safeName = DownloadEngine.SafeFileName(name);
        if (isVideoPage) safeName = Path.GetFileNameWithoutExtension(safeName) + "." + videoFormat;
        var folder = Path.GetFullPath(directory ?? Settings.DownloadDirectory);
        Directory.CreateDirectory(folder);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            var candidate = safeName;
            var suffix = 1;
            while (File.Exists(Path.Combine(folder, candidate)) || _jobs.Any(j => string.Equals(j.TargetPath, Path.Combine(folder, candidate), StringComparison.OrdinalIgnoreCase)))
                candidate = Path.GetFileNameWithoutExtension(safeName) + $" ({suffix++})" + Path.GetExtension(safeName);
            var job = new DownloadJob { Url = uri.AbsoluteUri, FileName = candidate, DirectoryPath = folder, State = startNow ? DownloadState.Queued : DownloadState.Paused, Referrer = referrer, BrowserRequestId = browserRequestId, IsVideoPage = isVideoPage, VideoHeight = videoHeight, VideoFormat = videoFormat };
            _jobs.Add(job);
            job.QueueOrder = _jobs.Max(j => j.QueueOrder) + 1;
            SaveLocked();
            return job;
        }
    }

    public async Task<BrowserReply> AcceptBrowserAsync(BrowserRequest request, CancellationToken token)
    {
        BrowserProtocol.Validate(request);
        if (request.Action == "ping") return new BrowserReply(true, AppRunning: true);
        if (request.Action == "inspect-video") return new BrowserReply(true, AppRunning: true, Video: await _video.InspectAsync(request.Url!, token));
        lock (_gate)
        {
            var existing = _jobs.FirstOrDefault(j => j.BrowserRequestId == request.Id);
            if (existing != null) return new BrowserReply(true, FileName: existing.FileName, JobId: existing.Id, AppRunning: true);
        }
        if (request.Action == "video-download" || VideoDownloader.IsYouTube(request.Url!))
        {
            var info = await _video.InspectAsync(request.Url!, token);
            if (!VideoDownloader.AudioOnly(request.VideoFormat) && request.VideoHeight != 0 && !info.Heights.Contains(request.VideoHeight)) throw new InvalidDataException("Seçilen video kalitesi sunulmuyor.");
            lock (_gate)
            {
                var video = _jobs.FirstOrDefault(j => j.BrowserRequestId == request.Id) ?? Add(request.Url!, info.Title + "." + request.VideoFormat, browserRequestId: request.Id, isVideoPage: true, videoHeight: request.VideoHeight, videoFormat: request.VideoFormat);
                if (PersistenceError != null) { video.State = DownloadState.Paused; throw new IOException(PersistenceError); }
                return new BrowserReply(true, FileName: video.FileName, JobId: video.Id, AppRunning: true);
            }
        }
        var remote = await _engine.ProbeAsync(request.Url!, token, request.Referrer);
        if (remote.ContentType is "text/html" or "application/xhtml+xml")
            throw new InvalidDataException("Bağlantı bir web sayfası döndürüyor. Doğrudan dosya bağlantısı veya tarayıcı oturumu gerekiyor.");
        var mime = remote.ContentType ?? "";
        if (mime.Contains("mpegurl", StringComparison.OrdinalIgnoreCase) || mime.Contains("dash+xml", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bu bağlantı parçalı yayın kullanıyor. Video sayfası seçeneğiyle ekleyip çözümlemeyi dene.");
        // onCreated may contain Chrome's provisional URL-derived name, before Content-Disposition is applied.
        var suggestedName = string.IsNullOrWhiteSpace(remote.SuggestedName) ? request.FileName : remote.SuggestedName;
        if (string.IsNullOrWhiteSpace(suggestedName))
        {
            suggestedName = DownloadEngine.SafeFileName(Uri.UnescapeDataString(new Uri(request.Url!).Segments.LastOrDefault() ?? "medya"));
            if (string.IsNullOrEmpty(Path.GetExtension(suggestedName))) suggestedName += mime.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg", "image/png" => ".png", "image/webp" => ".webp", "image/gif" => ".gif", "image/avif" => ".avif", "video/mp4" => ".mp4", "video/webm" => ".webm", "audio/mpeg" => ".mp3", "audio/mp4" => ".m4a", _ => ""
            };
        }
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.BrowserRequestId == request.Id) ?? Add(request.Url!, suggestedName, referrer: request.Referrer, browserRequestId: request.Id);
            if (PersistenceError != null)
            {
                // Never acknowledge a transfer that has not been committed to the library.
                job.State = DownloadState.Paused;
                throw new IOException(PersistenceError);
            }
            return new BrowserReply(true, FileName: job.FileName, JobId: job.Id, AppRunning: true);
        }
    }

    public Task<VideoInfo> InspectVideoAsync(string url, CancellationToken token) => _video.InspectAsync(url, token);

    // Moving queued work never interrupts an active transfer.
    public bool MoveQueued(DownloadJob job, DownloadJob? before)
    {
        lock (_gate)
        {
            if (_closing || job.State != DownloadState.Queued || !_jobs.Contains(job) || before == job || before != null && (before.State != DownloadState.Queued || !_jobs.Contains(before))) return false;
            var queue = _jobs.Where(j => j.State == DownloadState.Queued).OrderBy(j => j.QueueOrder).ToList();
            queue.Remove(job);
            queue.Insert(before == null ? queue.Count : queue.IndexOf(before), job);
            for (var i = 0; i < queue.Count; i++) queue[i].QueueOrder = i + 1;
            SaveLocked(); return true;
        }
    }

    public static void ValidateSpeedLimit(long bytes)
    {
        if (bytes < 0 || bytes > 10_000_000L * 1024 || bytes is > 0 and < 16 * 1024) throw new ArgumentException("Hız sınırını 0 veya 16–10000000 KB/sn aralığında gir.");
    }

    private long EffectiveVideoLimit(DownloadJob job)
    {
        var share = Settings.SpeedLimitBytes > 0 ? Math.Max(1, Settings.SpeedLimitBytes / Settings.MaxConcurrent) : 0;
        return job.SpeedLimitBytes > 0 ? share > 0 ? Math.Min(share, job.SpeedLimitBytes) : job.SpeedLimitBytes : share;
    }

    public async Task SetJobSpeedLimitAsync(DownloadJob job, long bytes)
    {
        ValidateSpeedLimit(bytes);
        bool restart;
        lock (_gate) { if (_closing || !_jobs.Contains(job)) return; restart = job.IsVideoPage && _running.ContainsKey(job.Id); job.SpeedLimitBytes = bytes; }
        if (restart) { await PauseAsync(job); Resume(job); }
        lock (_gate) SaveLocked();
    }

    public async Task SetTransferSettingsAsync(long bytes, int concurrent, int connections)
    {
        ValidateSpeedLimit(bytes);
        DownloadJob[] restart;
        lock (_gate)
        {
            if (_closing) return;
            _reconfiguring = true;
            restart = bytes != Settings.SpeedLimitBytes || concurrent != Settings.MaxConcurrent ? _jobs.Where(j => j.IsVideoPage && _running.ContainsKey(j.Id)).ToArray() : [];
        }
        try
        {
            foreach (var job in restart) await PauseAsync(job);
            lock (_gate) { Settings.SpeedLimitBytes = bytes; Settings.MaxConcurrent = Math.Clamp(concurrent, 1, 5); Settings.ConnectionsPerFile = Math.Clamp(connections, 1, 8); }
            foreach (var job in restart) Resume(job);
        }
        finally { lock (_gate) { _reconfiguring = false; SaveLocked(); } }
    }

    public void Resume(DownloadJob job)
    {
        lock (_gate)
        {
            if (_closing || !_jobs.Contains(job) || _running.ContainsKey(job.Id) || job.State == DownloadState.Completed) return;
            job.Error = null;
            job.State = DownloadState.Queued;
            SaveLocked();
        }
    }

    public async Task PauseAsync(DownloadJob job)
    {
        Task? pending = null;
        lock (_gate)
        {
            if (job.State is DownloadState.Completed or DownloadState.Failed) return;
            job.State = DownloadState.Paused;
            if (_running.TryGetValue(job.Id, out var running)) { running.Cancellation.Cancel(); pending = running.Task; }
        }
        if (pending != null) await pending;
        lock (_gate) SaveLocked();
    }

    public async Task RemoveAsync(DownloadJob job)
    {
        await PauseAsync(job);
        lock (_gate)
        {
            _engine.DeleteParts(job);
            _video.DeleteParts(job);
            _jobs.Remove(job);
            _samples.Remove(job.Id);
            SaveLocked();
        }
    }

    public void SaveSettings() { lock (_gate) SaveLocked(); }

    private void Tick(object? state)
    {
        lock (_gate)
        {
            if (_closing) return;
            var now = DateTime.UtcNow;
            foreach (var job in _jobs)
            {
                var bytes = Interlocked.Read(ref job.DownloadedBytes);
                if (_samples.TryGetValue(job.Id, out var previous) && job.State == DownloadState.Downloading)
                {
                    var instant = Math.Max(0, bytes - previous.Bytes) / Math.Max(0.01, (now - previous.Time).TotalSeconds);
                    job.BytesPerSecond = job.BytesPerSecond * 0.65 + instant * 0.35;
                }
                else job.BytesPerSecond = 0;
                _samples[job.Id] = (bytes, now);
            }
            foreach (var job in _jobs.Where(j => j.State == DownloadState.Queued).OrderBy(j => j.QueueOrder).ToArray())
            {
                if (_reconfiguring || !_schedulerEnabled) break;
                if (_running.Count >= Settings.MaxConcurrent) break;
                if (_running.ContainsKey(job.Id)) continue;
                job.State = DownloadState.Downloading;
                var cancellation = new CancellationTokenSource();
                var task = Task.Run(() => RunAsync(job, cancellation.Token));
                _running.Add(job.Id, new Running(cancellation, task));
            }
            if ((now - _lastSave).TotalSeconds >= 2) SaveLocked();
        }
    }

    private async Task RunAsync(DownloadJob job, CancellationToken token)
    {
        try
        {
            if (job.IsVideoPage) await _video.DownloadAsync(job, EffectiveVideoLimit(job), token);
            else await _engine.DownloadAsync(job, Settings.ConnectionsPerFile, token);
            lock (_gate) { job.State = DownloadState.Completed; job.CompletedAt = DateTimeOffset.Now; job.Error = null; }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { lock (_gate) job.State = DownloadState.Paused; }
        catch (Exception ex)
        {
            lock (_gate)
            {
                job.State = DownloadState.Failed;
                job.Error = ex is HttpRequestException { StatusCode: { } code } ? $"Sunucu yanıtı: {(int)code} ({code}). Bağlantının doğrudan dosyaya ait olduğundan emin ol." : ex.Message;
            }
        }
        finally
        {
            lock (_gate)
            {
                job.BytesPerSecond = 0;
                job.Stage = null;
                if (_running.Remove(job.Id, out var running)) running.Cancellation.Dispose();
                SaveLocked();
            }
        }
    }

    private void SaveLocked()
    {
        try
        {
            var temp = _libraryPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Library { Settings = Settings, Jobs = _jobs }, JsonOptions));
            File.Move(temp, _libraryPath, true);
            _lastSave = DateTime.UtcNow;
            PersistenceError = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { PersistenceError = "İndirme listesi kaydedilemedi: " + ex.Message; }
    }

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            if (_closing) return;
            _closing = true;
            _timer.Dispose();
            foreach (var running in _running.Values) running.Cancellation.Cancel();
            pending = _running.Values.Select(r => r.Task).ToArray();
        }
        await Task.WhenAll(pending);
        lock (_gate) SaveLocked();
        _engine.Dispose();
    }
}
