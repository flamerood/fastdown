using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Fastdown.Core;

public sealed record AppUpdate(Version Version, Uri DownloadUrl, long Size, string Sha256);

public sealed class UpdateService(HttpClient http)
{
    public static readonly Uri LatestRelease = new("https://api.github.com/repos/flamerood/fastdown/releases/latest");
    public static AppUpdate? ParseRelease(string json, Version current)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 24 });
        var root = doc.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v'), out var version) || version <= current) return null;
        var assets = root.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == "Fastdown-Setup.exe").ToArray();
        if (assets.Length != 1) throw new InvalidDataException("Sürümün kurulum dosyası bulunamadı.");
        var asset = assets[0];
        var expected = $"https://github.com/flamerood/fastdown/releases/download/{tag}/Fastdown-Setup.exe";
        if (asset.GetProperty("browser_download_url").GetString() != expected || !Uri.TryCreate(expected, UriKind.Absolute, out var url)) throw new InvalidDataException("Güncelleme adresi doğrulanamadı.");
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() ?? "" : "";
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 || digest[7..].Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Güncellemenin SHA-256 kaydı bulunamadı.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > 1024L * 1024 * 1024) throw new InvalidDataException("Geçersiz kurulum dosyası boyutu.");
        return new AppUpdate(version, url, size, digest[7..].ToLowerInvariant());
    }

    public async Task<AppUpdate?> CheckAsync(Version current, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestRelease);
        request.Headers.UserAgent.ParseAdd("Fastdown/" + current);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException("Sürüm bilgisi çok büyük.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var data = new MemoryStream();
        var buffer = new byte[16384]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0) { if (data.Length + read > 1024 * 1024) throw new InvalidDataException("Sürüm bilgisi çok büyük."); await data.WriteAsync(buffer.AsMemory(0, read), token); }
        return ParseRelease(System.Text.Encoding.UTF8.GetString(data.ToArray()), current);
    }

    public static async Task<bool> VerifyAsync(AppUpdate update, string path, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != update.Size) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token)) == update.Sha256;
    }

    public async Task<string> DownloadAsync(AppUpdate update, string directory, IProgress<double>? progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"Fastdown-Setup-{update.Version}.exe");
        if (await VerifyAsync(update, path, token)) return path;
        var temporary = path + "." + update.Sha256 + ".partial";
        var retained = File.Exists(temporary) ? new FileInfo(temporary).Length : 0;
        if (retained == update.Size && await VerifyAsync(update, temporary, token)) { File.Move(temporary, path, true); return path; }
        if (retained >= update.Size) { File.Delete(temporary); retained = 0; }
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            if (retained > 0) request.Headers.Range = new RangeHeaderValue(retained, null);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            if (response.StatusCode == System.Net.HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range?.From != retained || range.To != update.Size - 1 || range.Length != update.Size) throw new InvalidDataException("Güncellemenin devam aralığı doğrulanamadı.");
            }
            else retained = 0; // A server that ignores Range must replace, never append to, the partial file.
            if (response.Content.Headers.ContentLength is long length && length != update.Size - retained) throw new InvalidDataException("Kurulum dosyası boyutu eşleşmedi.");
            progress?.Report(retained * 100.0 / update.Size);
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(temporary, retained > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            {
                var buffer = new byte[131072]; long received = retained; int read;
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
                while (true)
                {
                    idle.CancelAfter(TimeSpan.FromSeconds(60));
                    read = await input.ReadAsync(buffer, idle.Token);
                    idle.CancelAfter(Timeout.InfiniteTimeSpan);
                    if (read == 0) break;
                    received += read;
                    if (received > update.Size) throw new InvalidDataException("Kurulum dosyası beklenen boyutu aştı.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    progress?.Report(received * 100.0 / update.Size);
                }
            }
            if (new FileInfo(temporary).Length < update.Size) throw new IOException("Güncelleme indirmesi yarıda kaldı; sonraki denemede sürdürülecek.");
            if (!await VerifyAsync(update, temporary, token)) throw new InvalidDataException("Güncelleme doğrulanamadı; kurulum başlatılmadı.");
            File.Move(temporary, path, true);
            return path;
        }
        catch (InvalidDataException) { if (File.Exists(temporary)) File.Delete(temporary); throw; }
        // Cancellation, process shutdown and transient transport failures retain hash-scoped partial data.
    }
}
