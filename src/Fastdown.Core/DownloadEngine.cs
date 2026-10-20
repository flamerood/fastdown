using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

namespace Fastdown.Core;

public sealed class DownloadEngine : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _partsRoot;
    private readonly Func<long> _speedLimit;
    private readonly object _throttleGate = new();
    private double _nextReadTime;
    private long _lastLimit;

    public DownloadEngine(string partsRoot, Func<long>? speedLimit = null)
    {
        _partsRoot = Path.GetFullPath(partsRoot);
        _speedLimit = speedLimit ?? (() => 0);
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 32
        }) { Timeout = Timeout.InfiniteTimeSpan };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Fastdown/1.0");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity");
    }

    public static Uri ValidateUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Geçerli bir http:// veya https:// indirme bağlantısı gir.");
        return uri;
    }

    public static string SafeFileName(string? suggested)
    {
        var name = (suggested ?? "indirme").Replace('\\', '/').Split('/').Last();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim().TrimEnd('.');
        if (name.Length > 180) name = name[..160] + Path.GetExtension(name)[..Math.Min(Path.GetExtension(name).Length, 20)];
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") name = "indirme";
        var stem = name.Split('.')[0];
        if (new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase)) name = "_" + name;
        return name;
    }

    public async Task<RemoteInfo> ProbeAsync(string url, CancellationToken token, string? referrer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ValidateUrl(url));
        if (referrer != null) request.Headers.Referrer = ValidateUrl(referrer);
        request.Headers.Range = new RangeHeaderValue(0, 0);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        // A zero-length resource correctly rejects bytes=0-0 with an unsatisfied range.
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && response.Content.Headers.ContentRange?.Length == 0)
            return new RemoteInfo(0, false, response.Headers.ETag?.ToString(), response.Content.Headers.LastModified, null);
        response.EnsureSuccessStatusCode();
        var range = response.Content.Headers.ContentRange;
        var ranged = response.StatusCode == HttpStatusCode.PartialContent && range?.From == 0 && range.To == 0 && range.Length > 0;
        var length = ranged ? range!.Length : response.Content.Headers.ContentLength;
        if (response.StatusCode == HttpStatusCode.PartialContent && !ranged)
            throw new IOException("Sunucu geçersiz bir dosya aralığı döndürdü.");
        var etag = response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null;
        var disposition = response.Content.Headers.ContentDisposition;
        var name = disposition?.FileNameStar ?? disposition?.FileName?.Trim('"');
        return new RemoteInfo(length, ranged, etag, response.Content.Headers.LastModified, name, response.Content.Headers.ContentType?.MediaType);
    }

    private string PartsDirectory(DownloadJob job) => Path.Combine(_partsRoot, job.Id.ToString("N"));
    private string PartPath(DownloadJob job, int index) => Path.Combine(PartsDirectory(job), index + ".part");

    public void DeleteParts(DownloadJob job)
    {
        var path = PartsDirectory(job);
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    public async Task DownloadAsync(DownloadJob job, int connections, CancellationToken token)
    {
        var remote = await ProbeAsync(job.Url, token, job.Referrer);
        var validatorMatches = job.ETag != null ? remote.ETag == job.ETag : remote.LastModified != null && remote.LastModified == job.LastModified;
        var canResume = remote.Ranges && job.SupportsRanges && remote.Length == job.TotalBytes && validatorMatches;
        if (!canResume) DeleteParts(job);
        job.TotalBytes = remote.Length;
        job.SupportsRanges = remote.Ranges;
        job.ETag = remote.ETag;
        job.LastModified = remote.LastModified;
        if (!canResume) job.SegmentCount = remote.Ranges && remote.Length >= 2 * 1024 * 1024 ? Math.Clamp(connections, 1, 8) : 1;
        Directory.CreateDirectory(PartsDirectory(job));
        Directory.CreateDirectory(job.DirectoryPath);
        if (File.Exists(job.TargetPath)) throw new IOException("Aynı isimde bir dosya zaten var. Dosyayı taşı veya indirmeyi farklı bir isimle ekle.");
        Interlocked.Exchange(ref job.DownloadedBytes, 0);
        for (var index = 0; index < job.SegmentCount; index++)
        {
            var length = File.Exists(PartPath(job, index)) ? new FileInfo(PartPath(job, index)).Length : 0;
            Interlocked.Add(ref job.DownloadedBytes, length);
        }
        using var group = CancellationTokenSource.CreateLinkedTokenSource(token);
        var parts = Enumerable.Range(0, job.SegmentCount).Select(async index =>
        {
            try { await DownloadPartAsync(job, index, group.Token); }
            catch { await group.CancelAsync(); throw; }
        }).ToArray();
        try { await Task.WhenAll(parts); }
        catch
        {
            // Prefer the actual HTTP/disk error over sibling cancellation errors.
            var fault = parts.FirstOrDefault(p => p.IsFaulted)?.Exception?.InnerException;
            if (fault != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fault).Throw();
            throw;
        }
        token.ThrowIfCancellationRequested();
        var assemblyPath = Path.Combine(job.DirectoryPath, ".akis-" + job.Id.ToString("N") + ".tmp");
        try
        {
            await using (var output = new FileStream(assemblyPath, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                for (var i = 0; i < job.SegmentCount; i++)
                {
                    await using var input = new FileStream(PartPath(job, i), FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true);
                    await input.CopyToAsync(output, token);
                }
                if (job.TotalBytes.HasValue && output.Length != job.TotalBytes.Value) throw new IOException("Dosya boyutu doğrulanamadı. İndirmeyi yeniden dene.");
                await output.FlushAsync(token);
            }
            token.ThrowIfCancellationRequested();
            File.Move(assemblyPath, job.TargetPath, false);
            job.TotalBytes = new FileInfo(job.TargetPath).Length;
            Interlocked.Exchange(ref job.DownloadedBytes, job.TotalBytes.Value);
        }
        finally { if (File.Exists(assemblyPath)) File.Delete(assemblyPath); }
        // A cleanup failure must not turn a successfully committed download into a failed job.
        try { DeleteParts(job); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private async Task DownloadPartAsync(DownloadJob job, int index, CancellationToken token)
    {
        var path = PartPath(job, index);
        var ranged = job.SupportsRanges && job.TotalBytes > 0;
        long start = ranged ? job.TotalBytes!.Value * index / job.SegmentCount : 0;
        long? end = ranged ? job.TotalBytes!.Value * (index + 1) / job.SegmentCount - 1 : null;
        var expected = ranged ? end - start + 1 : job.TotalBytes;
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var existing = File.Exists(path) ? new FileInfo(path).Length : 0;
            if (expected.HasValue && existing > expected.Value) throw new IOException("Geçici dosyanın boyutu beklenen aralığı aşıyor.");
            if (expected.HasValue && existing == expected.Value)
            {
                if (!File.Exists(path)) await File.WriteAllBytesAsync(path, [], token);
                return;
            }
            if (!ranged && existing > 0)
            {
                Interlocked.Add(ref job.DownloadedBytes, -existing);
                File.Delete(path);
                existing = 0;
            }
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, job.Url);
                if (job.Referrer != null) request.Headers.Referrer = ValidateUrl(job.Referrer);
                if (ranged)
                {
                    request.Headers.Range = new RangeHeaderValue(start + existing, end);
                    if (job.ETag != null) request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(job.ETag));
                    else if (job.LastModified.HasValue) request.Headers.IfRange = new RangeConditionHeaderValue(job.LastModified.Value);
                }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                response.EnsureSuccessStatusCode();
                if (ranged)
                {
                    var range = response.Content.Headers.ContentRange;
                    if (response.StatusCode != HttpStatusCode.PartialContent || range?.From != start + existing || range.To != end || range.Length != job.TotalBytes)
                        throw new InvalidDataException("Sunucu dosyayı veya devam desteğini değiştirdi. Yeniden dene.");
                    if (job.ETag != null && response.Headers.ETag is { } actualTag && actualTag.ToString() != job.ETag)
                        throw new InvalidDataException("İndirme sırasında dosya değişti. Yeniden dene.");
                }
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 65536, true);
                var buffer = new byte[65536];
                var written = existing;
                while (true)
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(30));
                    var read = await input.ReadAsync(buffer, timeout.Token);
                    timeout.CancelAfter(Timeout.InfiniteTimeSpan);
                    if (read == 0) break;
                    if (expected.HasValue && written + read > expected) throw new InvalidDataException("Sunucu beklenenden fazla veri gönderdi.");
                    await ThrottleAsync(read, token);
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    written += read;
                    Interlocked.Add(ref job.DownloadedBytes, read);
                }
                if (expected.HasValue && written != expected.Value) throw new IOException("Bağlantı dosya tamamlanmadan kapandı.");
                return;
            }
            catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), token);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException http => http.StatusCode == null || (int)http.StatusCode >= 500 || http.StatusCode == HttpStatusCode.TooManyRequests,
        OperationCanceledException => true,
        IOException => true,
        _ => false
    };

    private async Task ThrottleAsync(int bytes, CancellationToken token)
    {
        var limit = _speedLimit();
        if (limit <= 0) return;
        double delay;
        lock (_throttleGate)
        {
            var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
            if (_lastLimit != limit) { _nextReadTime = now; _lastLimit = limit; }
            _nextReadTime = Math.Max(now, _nextReadTime);
            delay = _nextReadTime - now;
            _nextReadTime += bytes / (double)limit;
        }
        if (delay > 0) await Task.Delay(TimeSpan.FromSeconds(delay), token);
    }

    public void Dispose() => _http.Dispose();
}
