using System.Security.Cryptography;
using System.Text.Json;
using Fastdown.Core;
using Microsoft.Win32;

internal static class Registration
{
    private static readonly string[] Browsers = [@"Software\Google\Chrome", @"Software\Microsoft\Edge", @"Software\Chromium"];
    public static int Run(bool remove, bool checkOnly = false)
    {
        var manifestPath = Path.Combine(AppContext.BaseDirectory, BrowserProtocol.HostName + ".json");
        foreach (var browser in Browsers)
        {
            var keyName = browser + @"\NativeMessagingHosts\" + BrowserProtocol.HostName;
            if (remove)
            {
                using var existing = Registry.CurrentUser.OpenSubKey(keyName);
                if (string.Equals(existing?.GetValue(null) as string, manifestPath, StringComparison.OrdinalIgnoreCase)) Registry.CurrentUser.DeleteSubKey(keyName, false);
            }
        }
        if (remove) { Console.WriteLine("Fastdown'ın bu klasöre ait tarayıcı köprüsü kayıtları kaldırıldı."); return 0; }
        var extensionPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "extension", "manifest.json"));
        using var extension = JsonDocument.Parse(File.ReadAllText(extensionPath));
        var key = Convert.FromBase64String(extension.RootElement.GetProperty("key").GetString()!);
        var id = new string(SHA256.HashData(key).Take(16).SelectMany(b => new[] { (char)('a' + (b >> 4)), (char)('a' + (b & 15)) }).ToArray());
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "Fastdown.exe"))) throw new FileNotFoundException("Fastdown.exe köprüyle aynı klasörde olmalı.");
        if (checkOnly) { Console.WriteLine("Kurulum dosyaları hazır. Eklenti kimliği: " + id); return 0; }
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            name = BrowserProtocol.HostName,
            description = "Fastdown indirme yöneticisi tarayıcı bağlantısı",
            path = Path.Combine(AppContext.BaseDirectory, "Fastdown.BrowserHost.exe"),
            type = "stdio",
            allowed_origins = new[] { "chrome-extension://" + id + "/" }
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var browser in Browsers)
        {
            using var registry = Registry.CurrentUser.CreateSubKey(browser + @"\NativeMessagingHosts\" + BrowserProtocol.HostName);
            registry.SetValue(null, manifestPath, RegistryValueKind.String);
        }
        Console.WriteLine("Tarayıcı köprüsü kuruldu. Yönetici yetkisi gerekmez.");
        Console.WriteLine("\nChrome: chrome://extensions   Edge: edge://extensions");
        Console.WriteLine("Geliştirici modunu aç > Paketlenmemiş öğe yükle > şu klasörü seç:");
        Console.WriteLine(Path.GetDirectoryName(extensionPath));
        Console.WriteLine("\nEklenti kimliği: " + id);
        Console.WriteLine("Açık web sayfalarını yenile. Eklenti simgesinde 'Köprü hazır' görünür.");
        return 0;
    }
}
