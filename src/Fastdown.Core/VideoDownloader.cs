using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Fastdown.Core;

public sealed record VideoInfo(string Title, int[] Heights);

/// <summary>Resolves a page again at download time so expiring stream URLs never become saved jobs.</summary>
public sealed class VideoDownloader(string partsRoot, string? toolsDirectory = null)
{
    private readonly string _tools = Path.GetFullPath(toolsDirectory ?? Path.Combine(AppContext.BaseDirectory, "tools"));
    private readonly string _parts = Path.GetFullPath(partsRoot);

    public static bool IsYouTube(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        var host = uri.Host.ToLowerInvariant();
        return (host == "youtu.be" && uri.AbsolutePath.Trim('/').Length > 0) ||
            ((host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal) || host == "youtube-nocookie.com" || host.EndsWith(".youtube-nocookie.com", StringComparison.Ordinal)) &&
            ((uri.AbsolutePath == "/watch" && uri.Query.Contains("v=", StringComparison.Ordinal)) || new[] { "/shorts/", "/live/", "/embed/" }.Any(uri.AbsolutePath.StartsWith)));
    }

    public static void ValidateHeight(int height)
    {
        if (height != 0 && (height < 72 || height > 8640)) throw new ArgumentException("Geçersiz video kalitesi.");
    }

    public static string FormatSelector(int height)
    {
        ValidateHeight(height);
        // No lower-quality fallback: a user's selected resolution must be honored.
        var filter = height == 0 ? "" : $"[height={height}]";
        return $"bv*{filter}+ba/b{filter}";
    }

    public static string ValidateFormat(string format) => format is "mkv" or "mp4" or "mp3" or "m4a" ? format : throw new ArgumentException("Desteklenen biçimler: MP4, MKV, MP3 ve M4A.");
    public static bool AudioOnly(string format) => format is "mp3" or "m4a";

    public static string FormatSelector(int height, string format)
    {
        ValidateFormat(format);
        if (AudioOnly(format)) return "ba/b";
        if (format == "mkv") return FormatSelector(height);
        ValidateHeight(height);
        var quality = height == 0 ? "" : $"[height={height}]";
        return $"bv*{quality}[ext=mp4]+ba[ext=m4a]/b{quality}[ext=mp4]/" + FormatSelector(height);
    }

    public static VideoInfo ParseInfo(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
        var data = document.RootElement;
        if (data.TryGetProperty("_type", out var type) && type.GetString() is "playlist" or "multi_video")
            throw new InvalidDataException("Tek bir videonun sayfa bağlantısını ekle; oynatma listeleri desteklenmiyor.");
        if (data.TryGetProperty("is_live", out var live) && live.ValueKind == JsonValueKind.True)
            throw new InvalidDataException("Devam eden canlı yayınlar henüz desteklenmiyor.");
        var heights = new HashSet<int>();
        if (data.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            foreach (var format in formats.EnumerateArray())
            {
                if (format.TryGetProperty("has_drm", out var drm) && drm.ValueKind == JsonValueKind.True) continue;
                if (!format.TryGetProperty("vcodec", out var codec) || codec.GetString() is null or "none") continue;
                if (codec.GetString() == "images") continue;
                if (format.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number && h.TryGetInt32(out var height) && height is >= 72 and <= 8640) heights.Add(height);
            }
        if (heights.Count == 0) throw new InvalidDataException("İndirilebilir video kalitesi bulunamadı. Video erişime kapalı veya korumalı olabilir.");
        var title = data.TryGetProperty("title", out var titleValue) ? titleValue.GetString() : null;
        return new VideoInfo((title ?? "Video")[..Math.Min(title?.Length ?? 5, 180)], heights.OrderDescending().ToArray());
    }

    public async Task<VideoInfo> InspectAsync(string url, CancellationToken token)
    {
        DownloadEngine.ValidateUrl(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(100));
        var result = new StringBuilder();
        try
        {
            await RunAsync(["--dump-single-json", "--skip-download", "--", url], line =>
            {
                if (result.Length + line.Length > 16 * 1024 * 1024) throw new InvalidDataException("Video kaynak listesi çok büyük.");
                result.AppendLine(line);
            }, timeout.Token);
            return ParseInfo(result.ToString());
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Video bilgileri zamanında alınamadı. Bağlantıyı kontrol edip tekrar dene."); }
    }

    public async Task DownloadAsync(DownloadJob job, long speedLimit, CancellationToken token)
    {
        ValidateHeight(job.VideoHeight);
        var format = ValidateFormat(job.VideoFormat);
        var audioOnly = AudioOnly(format);
        DownloadEngine.ValidateUrl(job.Url);
        if (!File.Exists(Path.Combine(_tools, "ffmpeg.exe"))) throw MissingTools();
        var area = Path.Combine(_parts, job.Id.ToString("N"));
        Directory.CreateDirectory(area);
        job.Stage = "Video ve ses hazırlanıyor…";
        // Revalidation also excludes DRM-only formats and active live streams on retry.
        var info = await InspectAsync(job.Url, token);
        if (!audioOnly && job.VideoHeight != 0 && !info.Heights.Contains(job.VideoHeight))
            throw new InvalidDataException("Seçilen kalite artık sunulmuyor. Video bağlantısını yeniden ekleyip kalite seç.");
        var output = Path.Combine(area, "video.%(ext)s").Replace("%", "%%", StringComparison.Ordinal).Replace("%%(ext)s", "%(ext)s", StringComparison.Ordinal);
        var arguments = new List<string> {
            "--newline", "--progress", "--progress-delta", "0.4", "--progress-template", "download:AKIS_PROGRESS:%(progress)j",
            "--format", FormatSelector(job.VideoHeight, format),
            "--ffmpeg-location", _tools, "--continue", "--no-overwrites", "--output", output,
            "--print", "after_move:AKIS_DONE:%(filepath)s", "--no-simulate"
        };
        if (audioOnly) arguments.AddRange(["--extract-audio", "--audio-format", format, "--audio-quality", "0"]);
        else arguments.AddRange(["--merge-output-format", format, "--remux-video", format]);
        if (speedLimit > 0) { arguments.Add("--limit-rate"); arguments.Add(speedLimit.ToString(CultureInfo.InvariantCulture)); }
        arguments.Add("--"); arguments.Add(job.Url);
        var progress = new Dictionary<string, (long Downloaded, long? Total)>();
        string? completed = null;
        await RunAsync(arguments, line =>
        {
            if (line.StartsWith("AKIS_DONE:", StringComparison.Ordinal)) completed = line[10..];
            else if (line.StartsWith("AKIS_PROGRESS:", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(line[14..]);
                var p = document.RootElement;
                var key = p.TryGetProperty("filename", out var f) ? f.GetString() ?? "video" : "video";
                static long? Number(JsonElement p, string key) => p.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;
                var downloaded = Number(p, "downloaded_bytes") ?? 0;
                var total = Number(p, "total_bytes") ?? Number(p, "total_bytes_estimate");
                progress[key] = (downloaded, total);
                Interlocked.Exchange(ref job.DownloadedBytes, progress.Values.Sum(v => v.Downloaded));
                job.TotalBytes = progress.Values.All(v => v.Total.HasValue) ? progress.Values.Sum(v => v.Total!.Value) : null;
                job.Stage = "Video ve ses indiriliyor…";
            }
            else if (line.StartsWith("[Merger]", StringComparison.Ordinal) || line.StartsWith("[VideoRemuxer]", StringComparison.Ordinal)) job.Stage = "Ses ve görüntü birleştiriliyor…";
        }, token);
        token.ThrowIfCancellationRequested();
        var final = Path.Combine(area, "video." + format);
        if (completed == null || !string.Equals(Path.GetFullPath(completed), final, StringComparison.OrdinalIgnoreCase) || !File.Exists(final))
            throw new IOException("Video dosyası tamamlanamadı. İndirmeyi yeniden dene.");
        // Copy into a reserved temporary file on the target volume, then atomically publish without overwrite.
        Directory.CreateDirectory(job.DirectoryPath);
        var pending = Path.Combine(job.DirectoryPath, ".akis-" + job.Id.ToString("N") + ".tmp");
        try
        {
            await using (var source = new FileStream(final, FileMode.Open, FileAccess.Read, FileShare.Read, 131072, true))
            await using (var target = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
                await source.CopyToAsync(target, token);
            token.ThrowIfCancellationRequested();
            File.Move(pending, job.TargetPath, false);
        }
        finally { if (File.Exists(pending)) File.Delete(pending); }
        var length = new FileInfo(job.TargetPath).Length;
        job.TotalBytes = length; Interlocked.Exchange(ref job.DownloadedBytes, length);
        job.Stage = null;
        try { Directory.Delete(area, true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void DeleteParts(DownloadJob job)
    {
        var path = Path.Combine(_parts, job.Id.ToString("N"));
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static IOException MissingTools() => new("Video araçları eksik. Paket içindeki Video-Araclarini-Guncelle.cmd dosyasını çalıştır.");

    private async Task RunAsync(IEnumerable<string> arguments, Action<string> output, CancellationToken token)
    {
        var exe = Path.Combine(_tools, "yt-dlp.exe");
        if (!File.Exists(exe) || !File.Exists(Path.Combine(_tools, "deno.exe"))) throw MissingTools();
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = _tools };
        foreach (var value in new[] { "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--playlist-end", "1", "--no-colors", "--no-warnings", "--no-cache-dir", "--socket-timeout", "15", "--retries", "2", "--fragment-retries", "2", "--js-runtimes", "deno:" + Path.Combine(_tools, "deno.exe"), "--encoding", "utf-8" }) start.ArgumentList.Add(value);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception ex) { throw new IOException("Video aracı çalıştırılamadı. Video-Araclarini-Guncelle.cmd ile araçları yeniden kur.", ex); }
        process.StandardInput.Close();
        var error = new StringBuilder();
        using var stop = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { } });
        async Task ReadErrors()
        {
            while (await process.StandardError.ReadLineAsync(token) is { } line)
                if (error.Length < 8192) error.AppendLine(line[..Math.Min(line.Length, 1000)]);
        }
        var errors = ReadErrors();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line) output(line);
            await process.WaitForExitAsync(token);
            await errors;
            token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException(UserError(error.ToString()));
        }
        finally
        {
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            try { await errors; } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        }
    }

    public static string UserError(string error)
    {
        if (error.Contains("Sign in", StringComparison.OrdinalIgnoreCase) || error.Contains("not a bot", StringComparison.OrdinalIgnoreCase) || error.Contains("login", StringComparison.OrdinalIgnoreCase))
            return "Site bu bağlantı için oturum veya doğrulama istiyor. Herkese açık başka bir video dene; tarayıcı çerezleri aktarılmıyor.";
        if (error.Contains("DRM", StringComparison.OrdinalIgnoreCase)) return "Bu video DRM koruması kullanıyor; indirilemiyor.";
        if (error.Contains("Unsupported URL", StringComparison.OrdinalIgnoreCase)) return "Bu video sitesi henüz desteklenmiyor. Varsa doğrudan dosya bağlantısını ekle.";
        if (error.Contains("Requested format", StringComparison.OrdinalIgnoreCase)) return "Seçilen kalite sunulmuyor. Kaliteleri yeniden getirip başka birini seç.";
        var detail = error.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "Bilinmeyen video hatası.";
        return "Video indirilemedi. " + detail[..Math.Min(detail.Length, 450)] + " Gerekirse Video-Araclarini-Guncelle.cmd ile video araçlarını güncelle.";
    }
}
