using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Fastdown.Core;

public sealed record BrowserRequest(string Action = "ping", string? Id = null, string? Url = null, string? FileName = null, string? Referrer = null, bool Focus = false, int VideoHeight = 0, string VideoFormat = "mkv");
public sealed record BrowserReply(bool Ok, string? Error = null, string? FileName = null, Guid? JobId = null, bool AppRunning = false, string Version = "1.7", VideoInfo? Video = null);

public static class BrowserProtocol
{
    // Keep the existing transport identity so upgrading the unpacked extension preserves its permissions and settings.
    public const string HostName = "com.akis.download_manager";
    public const int MaxMessageBytes = 128 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static string PipeName => "Akis.Browser.V1." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..20];

    public static void Validate(BrowserRequest request)
    {
        if (request.Action == "ping") return;
        if (request.Action is not ("download" or "inspect-video" or "video-download")) throw new ArgumentException("Desteklenmeyen tarayıcı komutu.");
        if (request.Action != "inspect-video" && !Guid.TryParse(request.Id, out _)) throw new ArgumentException("Geçerli bir istek kimliği gerekiyor.");
        if (request.Url == null || request.Url.Length > 16384) throw new ArgumentException("İndirme bağlantısı eksik veya çok uzun.");
        var uri = DownloadEngine.ValidateUrl(request.Url);
        VideoDownloader.ValidateHeight(request.VideoHeight);
        VideoDownloader.ValidateFormat(request.VideoFormat);
        if (request.Action == "download" && new[] { ".m3u8", ".mpd", ".m4s" }.Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Parçalı yayın bağlantısını video sayfası çözümleme seçeneğiyle ekle.");
        if (request.FileName?.Length > 1024) throw new ArgumentException("Dosya adı çok uzun.");
        if (request.Referrer != null)
        {
            if (request.Referrer.Length > 16384) throw new ArgumentException("Kaynak sayfa bağlantısı çok uzun.");
            DownloadEngine.ValidateUrl(request.Referrer);
        }
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), token);
        if (first == 0) return default;
        await stream.ReadExactlyAsync(header.AsMemory(1, 3), token);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxMessageBytes) throw new InvalidDataException("Geçersiz tarayıcı mesaj boyutu.");
        var buffer = new byte[length];
        await stream.ReadExactlyAsync(buffer, token);
        return JsonSerializer.Deserialize<T>(buffer, JsonOptions) ?? throw new InvalidDataException("Boş tarayıcı mesajı.");
    }

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("Tarayıcı mesajı çok uzun.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
}
