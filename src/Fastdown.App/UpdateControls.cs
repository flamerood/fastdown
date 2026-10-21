using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Fastdown.Core;

namespace Fastdown.App;

public partial class MainWindow
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly HttpClient _updateHttp = new() { Timeout = TimeSpan.FromMinutes(30) };
    private bool _updateBusy;
    private DateTime _nextUpdateCheck = DateTime.UtcNow.AddSeconds(4);
    private AppUpdate? _availableUpdate;
    private string? _updatePath;
    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 7, 0);

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_updateBusy || _closing) return;
        _updateBusy = true; _nextUpdateCheck = DateTime.UtcNow.AddHours(12);
        CheckUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Yeni sürüm kontrol ediliyor…";
        try
        {
            var service = new UpdateService(_updateHttp);
            using var scan = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            scan.CancelAfter(TimeSpan.FromSeconds(30));
            var update = await service.CheckAsync(CurrentVersion, scan.Token);
            if (update == null) { UpdateStatus.Text = $"Fastdown {CurrentVersion} güncel."; if (manual) Notify(UpdateStatus.Text); return; }
            _availableUpdate = update;
            UpdateBanner.Visibility = Visibility.Visible;
            InstallUpdateButton.IsEnabled = false;
            var progress = new Progress<double>(percent => { UpdateText.Text = $"Fastdown {update.Version} indiriliyor: %{percent:0}"; UpdateStatus.Text = UpdateText.Text; });
            UpdateText.Text = "Yeni sürüm indiriliyor…";
            _updatePath = await service.DownloadAsync(update, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fastdown", "updates"), progress, _lifetime.Token);
            UpdateText.Text = $"Fastdown {update.Version} hazır. İndirmelerin duraklatılıp listen kaydedilecek.";
            UpdateStatus.Text = $"{update.Version} hazır; üstteki düğmeyle kurabilirsin.";
            InstallUpdateButton.IsEnabled = true;
        }
        catch (OperationCanceledException) { if (!_closing) UpdateStatus.Text = "Güncelleme kontrolü zaman aşımına uğradı. Tekrar deneyebilirsin."; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { UpdateStatus.Text = "Güncelleme alınamadı. Bağlantıyı kontrol edip tekrar dene."; if (manual) Notify(UpdateStatus.Text); UpdateText.Text = UpdateStatus.Text; InstallUpdateButton.IsEnabled = false; }
        finally { _updateBusy = false; CheckUpdateButton.IsEnabled = true; }
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate == null || _updatePath == null || _closing) return;
        try
        {
            InstallUpdateButton.IsEnabled = false;
            if (!await UpdateService.VerifyAsync(_availableUpdate, _updatePath, _lifetime.Token)) throw new IOException("Güncelleme dosyası değişmiş. Yeniden kontrol et.");
            var currentRoot = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            // Portable/development copies migrate to the user install folder, never become uninstall targets.
            var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Fastdown");
            using (var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Fastdown"))
                if (string.Equals(registration?.GetValue("InstallRoot") as string, currentRoot, StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(currentRoot, "unins000.exe"))) target = currentRoot;
            var start = new ProcessStartInfo(_updatePath) { UseShellExecute = false };
            foreach (var argument in new[] { "/SILENT", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS", "/FASTDOWNUPDATE=1", "/DIR=" + target }) start.ArgumentList.Add(argument);
            ((App)Application.Current).PendingInstaller = start;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify(ex.Message); InstallUpdateButton.IsEnabled = true; }
    }
}
