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
    private readonly UpdateCheckSchedule _updateSchedule = new(DateTimeOffset.UtcNow);
    private AppUpdate? _availableUpdate;
    private string? _updatePath;
    private static string UpdateDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fastdown", "updates");
    private static void RecordUpdate(string stage, string message, Version? version = null)
    {
        try
        {
            Directory.CreateDirectory(UpdateDirectory);
            var file = Path.Combine(UpdateDirectory, "last-update.json");
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(new { time = DateTimeOffset.UtcNow, stage, version = version?.ToString(), message }));
            File.Move(file + ".tmp", file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private static Version CurrentVersion => typeof(MainWindow).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(1, 7, 5);

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(true);
    private async Task CheckUpdatesAsync(bool manual)
    {
        if (_updateBusy || _closing) return;
        _updateBusy = true;
        var succeeded = false;
        CheckUpdateButton.IsEnabled = false;
        RetryUpdateButton.Visibility = Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Yeni sürüm kontrol ediliyor…";
        RecordUpdate("checking", UpdateStatus.Text);
        try
        {
            var service = new UpdateService(_updateHttp);
            using var scan = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            scan.CancelAfter(TimeSpan.FromSeconds(30));
            var update = await service.CheckAsync(CurrentVersion, scan.Token);
            if (update == null)
            {
                succeeded = true; _availableUpdate = null; _updatePath = null;
                UpdateBanner.Visibility = Visibility.Collapsed;
                UpdateStatus.Text = $"Fastdown {CurrentVersion} güncel.";
                RecordUpdate("current", UpdateStatus.Text, CurrentVersion);
                if (manual) Notify(UpdateStatus.Text); return;
            }
            _availableUpdate = update;
            UpdateBanner.Visibility = Visibility.Visible;
            InstallUpdateButton.IsEnabled = false;
            var progress = new Progress<double>(percent => { UpdateText.Text = $"Fastdown {update.Version} indiriliyor: %{percent:0}"; UpdateStatus.Text = UpdateText.Text; });
            UpdateText.Text = "Yeni sürüm indiriliyor…";
            RecordUpdate("downloading", UpdateText.Text, update.Version);
            _updatePath = await service.DownloadAsync(update, UpdateDirectory, progress, _lifetime.Token);
            UpdateText.Text = $"Fastdown {update.Version} hazır. İndirmelerin duraklatılıp listen kaydedilecek.";
            UpdateStatus.Text = $"{update.Version} hazır; üstteki düğmeyle kurabilirsin.";
            InstallUpdateButton.IsEnabled = true;
            succeeded = true;
            RecordUpdate("ready", UpdateText.Text, update.Version);
        }
        catch (OperationCanceledException)
        {
            RecordUpdate(_closing ? "interrupted" : "timeout", "Yarım kalan indirme sonraki denemede doğrulanarak sürdürülecek.", _availableUpdate?.Version);
            if (!_closing) ShowUpdateFailure("Güncelleme zaman aşımına uğradı.", manual);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { RecordUpdate(_closing ? "interrupted" : "failed", ex.GetType().Name + ": " + ex.Message, _availableUpdate?.Version); if (!_closing) ShowUpdateFailure("Güncelleme alınamadı.", manual); }
        finally { _updateSchedule.Complete(succeeded, DateTimeOffset.UtcNow); _updateBusy = false; CheckUpdateButton.IsEnabled = true; }
    }

    private void ShowUpdateFailure(string message, bool manual)
    {
        UpdateStatus.Text = message + (_manager.Settings.AutoUpdate ? " Otomatik kontrol kısa süre sonra tekrar deneyecek." : " Tekrar dene düğmesini kullanabilirsin.");
        UpdateText.Text = UpdateStatus.Text;
        UpdateBanner.Visibility = Visibility.Visible;
        RetryUpdateButton.Visibility = Visibility.Visible;
        InstallUpdateButton.IsEnabled = false;
        if (manual) Notify(UpdateStatus.Text);
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
            RecordUpdate("installing", "Liste kaydedilip kurulum başlatılıyor.", _availableUpdate.Version);
            Close();
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify(ex.Message); InstallUpdateButton.IsEnabled = true; }
    }
}
