using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Fastdown.Core;
using Microsoft.Win32;

namespace Fastdown.App;

public partial class MainWindow : Window
{
    private readonly DownloadManager _manager;
    private readonly BrowserPipeServer? _browser;
    private readonly ObservableCollection<DownloadRow> _rows = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly HashSet<Guid> _completed = [];
    private readonly ShutdownCountdown _shutdown = new();
    private readonly CompletionNotifier _notifier;
    private readonly bool _completionEnabled;
    private string _filter = "all";
    private string _lastSuggested = "";
    private bool _closing;
    private bool _allowClose;
    private bool _initialized;
    private DateTime _statusUntil;
    private VideoInfo? _videoInfo;
    private string? _videoUrl;
    private CancellationTokenSource? _videoScan;
    private sealed record VideoChoice(int Height, string Label);
    private sealed record OutputChoice(string Format, string Label);
    private sealed record AfterChoice(CompletionAction Value, string Label);
    private string OutputFormat => (VideoFormatInput.SelectedItem as OutputChoice)?.Format ?? "mp4";

    public MainWindow(string dataDirectory, bool enableBrowser = true)
    {
        _manager = new DownloadManager(dataDirectory);
        _completionEnabled = enableBrowser;
        _completed.UnionWith(_manager.Snapshot().Where(j => j.State == DownloadState.Completed).Select(j => j.Id));
        _notifier = new CompletionNotifier(() => { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); });
        InitializeComponent();
        VideoFormatInput.ItemsSource = new[] { new OutputChoice("mp4", "MP4 · Video + ses"), new OutputChoice("mkv", "MKV · Video + ses"), new OutputChoice("mp3", "MP3 · Sadece ses"), new OutputChoice("m4a", "M4A · Sadece ses") };
        VideoFormatInput.SelectedIndex = 0;
        CompletionActionInput.ItemsSource = new[] { new AfterChoice(CompletionAction.None, "Bir şey yapma"), new AfterChoice(CompletionAction.OpenFolder, "Dosyanın klasörünü aç"), new AfterChoice(CompletionAction.OpenDocument, "Medya / belge dosyasını aç") };
        DownloadList.ItemsSource = _rows;
        _initialized = true;
        SizeChanged += (_, _) => UpdateEmptyLayout();
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Refresh();
        if (enableBrowser) _browser = new BrowserPipeServer(ReceiveBrowserAsync);
    }

    private async Task<BrowserReply> ReceiveBrowserAsync(BrowserRequest request, CancellationToken token)
    {
        var reply = await _manager.AcceptBrowserAsync(request, token);
        if (request.Action is "download" or "video-download") await Dispatcher.InvokeAsync(() =>
        {
            Notify("Tarayıcıdan eklendi: " + reply.FileName);
            Refresh();
            if (request.Focus) { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }
        });
        return reply;
    }

    private void UpdateEmptyLayout()
    {
        var compact = ActualHeight < 790;
        EmptyGraphic.Visibility = EmptyHint.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.FontSize = compact ? 17 : 21;
    }

    internal void PreparePreview(string? screen)
    {
        if (screen == "add") { Add_Click(this, new RoutedEventArgs()); UrlInput.Text = "https://ornek.com/dosya.zip"; }
        if (screen == "video")
        {
            Add_Click(this, new RoutedEventArgs()); UrlInput.Text = "https://www.youtube.com/watch?v=jNQXAC9IVRw";
            VideoQualityInput.ItemsSource = new[] { new VideoChoice(0,"En iyi kalite + ses"), new VideoChoice(240,"240p + ses"), new VideoChoice(144,"144p + ses") };
            VideoQualityInput.SelectedIndex=1; NameInput.Text="Me at the zoo.mp4";
            FolderInput.Text="C:\\Users\\Kullanici\\Downloads\\Fastdown";
            VideoHint.Text="MP4 video + ses · Me at the zoo";
        }
        if (screen == "settings") Settings_Click(this, new RoutedEventArgs());
        if (screen == "compact") { Width = MinWidth; Height = MinHeight; }
    }

    private void Refresh()
    {
        if (!_initialized) return;
        var jobs = _manager.Snapshot();
        if (_completionEnabled)
        {
            foreach (var job in jobs.Where(j => j.State == DownloadState.Completed && !_completed.Contains(j.Id)))
            {
                _completed.Add(job.Id);
                if (_manager.Settings.NotifyOnComplete) { try { _notifier.Show(job.FileName); } catch (Exception ex) when (ex is Win32Exception or ArgumentException) { Notify("İndirme tamamlandı: " + job.FileName); } }
                if (_manager.Settings.CompletionAction == CompletionAction.OpenFolder) OpenFolder(job.DirectoryPath);
                else if (_manager.Settings.CompletionAction == CompletionAction.OpenDocument && ShutdownCountdown.CanOpenDocument(job.TargetPath)) TryShell(() => Process.Start(new ProcessStartInfo(job.TargetPath) { UseShellExecute = true }));
            }
            var power = _shutdown.Tick(jobs, DateTimeOffset.Now);
            ShutdownBanner.Visibility = power.Seconds.HasValue ? Visibility.Visible : Visibility.Collapsed;
            ShutdownText.Text = $"İndirmeler tamamlandı. Bilgisayar {power.Seconds} saniye sonra kapanacak.";
            if (power.Shutdown)
            {
                ShutdownCheck.IsChecked = false;
                if (!_closing && !_manager.Snapshot().Any(j => j.State is DownloadState.Queued or DownloadState.Downloading or DownloadState.Paused or DownloadState.Failed))
                    TryShell(() => { var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe")) { UseShellExecute = false, CreateNoWindow = true }; foreach (var arg in new[] { "/s", "/t", "0" }) start.ArgumentList.Add(arg); return Process.Start(start); });
            }
        }
        var active = jobs.Count(j => j.State == DownloadState.Downloading);
        var queued = jobs.Count(j => j.State == DownloadState.Queued);
        var completed = jobs.Where(j => j.State == DownloadState.Completed).ToArray();
        AllCount.Text = jobs.Length.ToString();
        ActiveCount.Text = active.ToString();
        CompletedCount.Text = completed.Length.ToString();
        ActiveTotal.Text = active.ToString();
        CompletedTotal.Text = completed.Length.ToString();
        TotalSpeed.Text = Format.Bytes(jobs.Sum(j => j.BytesPerSecond));
        CompletedSize.Text = Format.Bytes(completed.Sum(j => (double)(j.TotalBytes ?? 0))) + " indirildi";
        QueueLabel.Text = queued > 0 ? $"{queued} dosya sırada bekliyor" : "Kuyruk hazır";
        SpeedLimitLabel.Text = _manager.Settings.SpeedLimitBytes > 0 ? "Sınır: " + Format.Bytes(_manager.Settings.SpeedLimitBytes) + "/sn" : "Hız sınırı yok";
        ConnectionLabel.Text = $"{_manager.Settings.MaxConcurrent} eşzamanlı indirme  /  {_manager.Settings.ConnectionsPerFile} bağlantı";
        ResumeAllButton.IsEnabled = jobs.Any(j => j.State is DownloadState.Paused or DownloadState.Failed);
        PauseAllButton.IsEnabled = active + queued > 0;
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var search = SearchBox.Text.Trim();
        var visible = jobs.Where(j => Matches(j) && (j.FileName.Contains(search, StringComparison.OrdinalIgnoreCase) || j.Url.Contains(search, StringComparison.OrdinalIgnoreCase))).OrderByDescending(j => j.CreatedAt).ToArray();
        var ids = visible.Select(j => j.Id).ToHashSet();
        for (var index = _rows.Count - 1; index >= 0; index--) if (!ids.Contains(_rows[index].Job.Id)) _rows.RemoveAt(index);
        for (var index = 0; index < visible.Length; index++)
        {
            var row = _rows.FirstOrDefault(r => r.Job.Id == visible[index].Id);
            if (row == null) { row = new DownloadRow(visible[index]); _rows.Insert(index, row); }
            else if (_rows.IndexOf(row) != index) _rows.Move(_rows.IndexOf(row), index);
            row.Update();
        }
        EmptyState.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyTitle.Text = jobs.Length == 0 ? "İlk indirmene yer açtık." : "Burada henüz bir dosya yok.";
        EmptyDescription.Text = jobs.Length == 0 ? "Bir bağlantı ekle, gerisini Fastdown'a bırak." : "Aramanı veya soldaki filtreyi değiştirebilirsin.";
        ListCount.Text = search.Length == 0 && _filter == "all" ? $"{jobs.Length} dosya" : $"{visible.Length} / {jobs.Length} dosya";
        UpdateDetail();
        if (_manager.PersistenceError != null) StatusText.Text = _manager.PersistenceError;
        else if (DateTime.UtcNow >= _statusUntil) StatusText.Text = active > 0 ? $"{active} dosya indiriliyor" : "Hazır";
    }

    private bool Matches(DownloadJob job) => _filter switch
    {
        "all" => true,
        "active" => job.State == DownloadState.Downloading,
        "queued" => job.State == DownloadState.Queued,
        "paused" => job.State == DownloadState.Paused,
        "completed" => job.State == DownloadState.Completed,
        "failed" => job.State == DownloadState.Failed,
        _ => job.Category == _filter
    };

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        _filter = (string)((RadioButton)sender).Tag;
        if (!_initialized) return;
        PageTitle.Text = _filter switch { "all" => "Tüm indirmeler", "active" => "İndiriliyor", "queued" => "Sırada bekleyen", "paused" => "Duraklatılan", "completed" => "Tamamlanan", "failed" => "Hatalı indirmeler", "Video" => "Videolar", "Müzik" => "Müzikler", "Belge" => "Belgeler", "Arşiv" => "Arşivler", _ => "Tüm indirmeler" };
        Refresh();
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) => Refresh();
    private DownloadJob? Selected => (DownloadList.SelectedItem as DownloadRow)?.Job;
    private void DownloadList_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (_initialized) UpdateDetail(); }
    private void UpdateDetail()
    {
        var job = Selected;
        CopyLinkButton.Visibility = RemoveButton.Visibility = job == null ? Visibility.Collapsed : Visibility.Visible;
        DetailIcon.Visibility = job == null ? Visibility.Visible : Visibility.Collapsed;
        DetailTitle.Text = job?.FileName ?? "İndirmelerin güvende.";
        DetailDescription.Text = job == null ? "Fastdown kapandığında listen saklanır. Devam etmek için indirmeyi sürdür." : job.Error ?? (job.State == DownloadState.Downloading ? job.IsVideoPage ? job.Stage ?? "Video hazırlanıyor…" : (job.SupportsRanges ? $"{job.SegmentCount} bağlantı  ·  Sunucu kaldığı yerden devam etmeyi destekliyor." : "Tek bağlantı  ·  Sunucu devam desteği sunmuyorsa indirme baştan başlar.") : job.TargetPath);
        DetailDescription.Foreground = job?.Error == null ? (Brush)FindResource("MutedBrush") : Brushes.Salmon;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
        AddOverlay.Visibility = Visibility.Visible;
        AddError.Visibility = Visibility.Collapsed;
        NameInput.Clear();
        _videoScan?.Cancel();
        _videoInfo = null; _videoUrl = null;
        VideoPageCheck.IsChecked = false;
        _lastSuggested = "";
        UrlInput.Clear();
        FolderInput.Text = _manager.Settings.DownloadDirectory;
        try { if (Clipboard.ContainsText()) { var value = Clipboard.GetText().Trim(); if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") UrlInput.Text = value; } }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException) { }
        UrlInput.Focus();
    }
    private void UrlInput_Changed(object sender, TextChangedEventArgs e)
    {
        if (NameInput == null) return;
        _videoScan?.Cancel(); _videoInfo = null; _videoUrl = null;
        if (VideoQualityInput != null) VideoQualityInput.ItemsSource = null;
        if (VideoPageCheck != null && VideoDownloader.IsYouTube(UrlInput.Text.Trim())) VideoPageCheck.IsChecked = true;
        if (!Uri.TryCreate(UrlInput.Text.Trim(), UriKind.Absolute, out var uri)) return;
        string suggested;
        try { suggested = DownloadEngine.SafeFileName(Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? "indirme")); }
        catch (UriFormatException) { return; }
        if (string.IsNullOrWhiteSpace(NameInput.Text) || NameInput.Text == _lastSuggested) NameInput.Text = suggested;
        _lastSuggested = suggested;
    }
    private void VideoMode_Changed(object sender, RoutedEventArgs e)
    {
        if (VideoControls == null) return;
        VideoControls.Visibility = VideoPageCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (VideoPageCheck.IsChecked != true) _videoScan?.Cancel();
    }
    private async void LoadVideo_Click(object sender, RoutedEventArgs e) => await LoadVideoAsync();
    private async Task LoadVideoAsync()
    {
        _videoScan?.Cancel();
        using var scan = new CancellationTokenSource(); _videoScan = scan;
        var url = UrlInput.Text.Trim();
        LoadVideoButton.IsEnabled = false; AddError.Visibility = Visibility.Collapsed;
        VideoHint.Text = "Video bilgileri ve kaliteler alınıyor…";
        try
        {
            var info = await _manager.InspectVideoAsync(url, scan.Token);
            if (scan.IsCancellationRequested || UrlInput.Text.Trim() != url) return;
            _videoInfo = info; _videoUrl = url;
            VideoQualityInput.ItemsSource = new[] { new VideoChoice(0, "En iyi kalite + ses") }.Concat(info.Heights.Select(h => new VideoChoice(h, h + "p + ses")));
            VideoQualityInput.SelectedIndex = 0;
            NameInput.Text = DownloadEngine.SafeFileName(info.Title) + "." + OutputFormat;
            VideoHint.Text = "Biçimi ve kaliteyi seç. " + info.Title;
        }
        catch (OperationCanceledException) { VideoHint.Text = "Kaliteleri yeniden getirebilirsin."; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        { AddError.Text = ex.Message; AddError.Visibility = Visibility.Visible; VideoHint.Text = "Video bilgileri alınamadı."; }
        finally { if (ReferenceEquals(_videoScan, scan)) { _videoScan = null; LoadVideoButton.IsEnabled = true; } }
    }
    private void CancelAdd_Click(object sender, RoutedEventArgs e) { _videoScan?.Cancel(); AddOverlay.Visibility = Visibility.Collapsed; }
    private void VideoFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (VideoQualityInput == null || NameInput == null) return;
        VideoQualityInput.IsEnabled = !VideoDownloader.AudioOnly(OutputFormat);
        if (_videoInfo != null) NameInput.Text = DownloadEngine.SafeFileName(_videoInfo.Title) + "." + OutputFormat;
    }
    private async void StartDownload_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var video = VideoPageCheck.IsChecked == true || VideoDownloader.IsYouTube(UrlInput.Text.Trim());
            if (video && (_videoInfo == null || _videoUrl != UrlInput.Text.Trim())) { VideoPageCheck.IsChecked = true; await LoadVideoAsync(); return; }
            if (!Path.IsPathFullyQualified(FolderInput.Text.Trim())) throw new ArgumentException("Kaydetmek için tam bir klasör yolu seç.");
            var height = video && !VideoDownloader.AudioOnly(OutputFormat) && VideoQualityInput.SelectedValue is int selectedHeight ? selectedHeight : 0;
            var job = _manager.Add(UrlInput.Text, NameInput.Text, FolderInput.Text.Trim(), isVideoPage: video, videoHeight: height, videoFormat: OutputFormat);
            AddOverlay.Visibility = Visibility.Collapsed;
            Notify("İndirme kuyruğa eklendi: " + job.FileName);
            Refresh();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { AddError.Text = ex.Message; AddError.Visibility = Visibility.Visible; }
    }

    private static void PickFolder(TextBox target)
    {
        var dialog = new OpenFolderDialog { Title = "İndirme klasörü seç", Multiselect = false };
        if (Directory.Exists(target.Text)) dialog.InitialDirectory = target.Text;
        if (dialog.ShowDialog() == true) target.Text = dialog.FolderName;
    }
    private void AddFolder_Click(object sender, RoutedEventArgs e) => PickFolder(FolderInput);
    private void SettingsFolder_Click(object sender, RoutedEventArgs e) => PickFolder(SettingsFolder);
    private void OpenDownloads_Click(object sender, RoutedEventArgs e) => OpenFolder(_manager.Settings.DownloadDirectory);
    private void BrowserSetup_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "extension")));
        Notify("Eklenti-Kur.cmd dosyasını çalıştır; sonra Chrome / Edge uzantılar sayfasından extension klasörünü yükle.");
    }
    private void ItemFolder_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not DownloadRow row) return;
        if (File.Exists(row.Job.TargetPath))
            TryShell(() => Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + row.Job.TargetPath + "\"") { UseShellExecute = true }));
        else OpenFolder(row.Job.DirectoryPath);
    }
    private void OpenFolder(string path) => TryShell(() => { Directory.CreateDirectory(path); return Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); });
    private void TryShell(Func<Process?> action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception) { Notify("Klasör açılamadı: " + ex.Message); }
    }
    private async void ItemAction_Click(object sender, RoutedEventArgs e)
    {
        if (((Button)sender).DataContext is not DownloadRow row) return;
        if (row.Job.State == DownloadState.Completed) { ItemFolder_Click(sender, e); return; }
        await GuardAsync(async () => { if (row.Job.State is DownloadState.Downloading or DownloadState.Queued) await _manager.PauseAsync(row.Job); else _manager.Resume(row.Job); });
        Refresh();
    }
    private async void PauseAll_Click(object sender, RoutedEventArgs e)
    {
        await GuardAsync(() => Task.WhenAll(_manager.Snapshot().Where(j => j.State is DownloadState.Queued or DownloadState.Downloading).Select(j => _manager.PauseAsync(j))));
        Refresh();
    }
    private void ResumeAll_Click(object sender, RoutedEventArgs e) { foreach (var job in _manager.Snapshot().Where(j => j.State is DownloadState.Paused or DownloadState.Failed)) _manager.Resume(job); Refresh(); }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        var job = Selected;
        if (job == null) return;
        await GuardAsync(() => _manager.RemoveAsync(job));
        Notify("İndirme listeden kaldırıldı. Tamamlanan dosya varsa korundu.");
        Refresh();
    }
    private void CopyLink_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        try { Clipboard.SetText(Selected.Url); Notify("İndirme bağlantısı kopyalandı."); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or UnauthorizedAccessException) { Notify("Panoya erişilemiyor. Tekrar dene."); }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        AddOverlay.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
        SettingsFolder.Text = _manager.Settings.DownloadDirectory;
        ConcurrentSlider.Value = _manager.Settings.MaxConcurrent;
        ConnectionsSlider.Value = _manager.Settings.ConnectionsPerFile;
        SpeedInput.Text = (_manager.Settings.SpeedLimitBytes / 1024).ToString(CultureInfo.InvariantCulture);
        NotifyCheck.IsChecked = _manager.Settings.NotifyOnComplete;
        CompletionActionInput.SelectedValue = _manager.Settings.CompletionAction;
        ShutdownCheck.IsChecked = _shutdown.Armed;
        SettingsError.Text = "";
    }
    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ConcurrentValue != null && ConcurrentSlider != null) ConcurrentValue.Text = ((int)ConcurrentSlider.Value).ToString();
        if (ConnectionsValue != null && ConnectionsSlider != null) ConnectionsValue.Text = ((int)ConnectionsSlider.Value).ToString();
    }
    private void CancelSettings_Click(object sender, RoutedEventArgs e) => SettingsOverlay.Visibility = Visibility.Collapsed;
    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!Path.IsPathFullyQualified(SettingsFolder.Text.Trim())) throw new ArgumentException("Tam bir klasör yolu seç.");
            if (!long.TryParse(SpeedInput.Text.Trim(), out var kb) || kb < 0 || kb > 10_000_000 || kb is > 0 and < 64) throw new ArgumentException("Hız sınırını 0 veya 64–10000000 KB/sn aralığında gir.");
            Directory.CreateDirectory(SettingsFolder.Text.Trim());
            _manager.Settings.DownloadDirectory = Path.GetFullPath(SettingsFolder.Text.Trim());
            _manager.Settings.MaxConcurrent = (int)ConcurrentSlider.Value;
            _manager.Settings.ConnectionsPerFile = (int)ConnectionsSlider.Value;
            _manager.Settings.SpeedLimitBytes = kb * 1024;
            _manager.Settings.NotifyOnComplete = NotifyCheck.IsChecked == true;
            _manager.Settings.CompletionAction = CompletionActionInput.SelectedValue is CompletionAction action ? action : CompletionAction.None;
            if (ShutdownCheck.IsChecked == true && !_shutdown.Armed) { _shutdown.Arm(); _shutdown.Tick(_manager.Snapshot(), DateTimeOffset.Now); }
            else if (ShutdownCheck.IsChecked != true) _shutdown.Cancel();
            _manager.SaveSettings();
            SettingsOverlay.Visibility = Visibility.Collapsed;
            Notify("Ayarlar kaydedildi. Bağlantı sayısı yeni başlayan indirmelere uygulanır.");
            Refresh();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException) { SettingsError.Text = ex.Message; }
    }
    private void Notify(string text) { StatusText.Text = text; _statusUntil = DateTime.UtcNow.AddSeconds(7); }
    private void CancelShutdown_Click(object sender, RoutedEventArgs e) { _shutdown.Cancel(); ShutdownCheck.IsChecked = false; ShutdownBanner.Visibility = Visibility.Collapsed; Notify("Bilgisayarı kapatma iptal edildi."); }
    private async Task GuardAsync(Func<Task> action) { try { await action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notify(ex.Message); } }
    private void Title_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ClickCount == 2) Maximize_Click(sender, e); else if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _videoScan?.Cancel(); AddOverlay.Visibility = SettingsOverlay.Visibility = Visibility.Collapsed; }
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control) { Add_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.Enter && AddOverlay.Visibility == Visibility.Visible) { StartDownload_Click(sender, e); e.Handled = true; }
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); e.Handled = true; }
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _shutdown.Cancel();
        _notifier.Dispose();
        _videoScan?.Cancel();
        _refresh.Stop();
        IsEnabled = false;
        StatusText.Text = "İndirmeler duraklatılıyor, liste kaydediliyor…";
        try { if (_browser != null) await _browser.DisposeAsync(); await _manager.DisposeAsync(); }
        finally { _allowClose = true; _ = Dispatcher.BeginInvoke(new Action(Close)); }
    }
}

public static class Format
{
    public static string Bytes(double value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var unit = 0;
        value = Math.Max(0, value);
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.GetCultureInfo("tr-TR")) + " " + units[unit];
    }
}

public sealed class DownloadRow(DownloadJob job) : INotifyPropertyChanged
{
    public DownloadJob Job { get; } = job;
    public string Name => Job.FileName;
    public string Extension => Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext[..Math.Min(4, ext.Length)] : "FILE";
    public double Progress => Job.TotalBytes > 0 ? Math.Clamp(Interlocked.Read(ref Job.DownloadedBytes) * 100.0 / Job.TotalBytes.Value, 0, 100) : Job.State == DownloadState.Completed ? 100 : 0;
    public string PercentLabel => Job.TotalBytes.HasValue ? $"{Progress:0}%" : "";
    public string SizeLabel => Job.State == DownloadState.Completed ? Format.Bytes(Job.TotalBytes ?? 0) + "  ·  " + Job.Category : Format.Bytes(Interlocked.Read(ref Job.DownloadedBytes)) + " / " + (Job.TotalBytes.HasValue ? Format.Bytes(Job.TotalBytes.Value) : "Boyut bekleniyor");
    public string StateLabel => Job.State switch { DownloadState.Queued => "Sırada", DownloadState.Downloading => "İndiriliyor", DownloadState.Paused => "Duraklatıldı", DownloadState.Completed => "Tamamlandı", _ => "Yeniden dene" };
    public Brush StateBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Job.State switch { DownloadState.Completed or DownloadState.Downloading => "#99F0BF", DownloadState.Failed => "#EBA39E", DownloadState.Paused => "#D5BE88", _ => "#9DAABA" }));
    public Brush IconBackground => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Job.Category switch { "Video" => "#2D2841", "Müzik" => "#3C2937", "Arşiv" => "#3C3424", "Belge" => "#243440", _ => "#25392F" }));
    public Brush IconForeground => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Job.Category switch { "Video" => "#C0ACF7", "Müzik" => "#E9A6CA", "Arşiv" => "#E5C580", "Belge" => "#9CCBEB", _ => "#A5DEB9" }));
    public string SpeedLabel => Job.State == DownloadState.Downloading ? Format.Bytes(Job.BytesPerSecond) + "/sn" : "—";
    public string RemainingLabel
    {
        get
        {
            if (Job.State != DownloadState.Downloading || Job.BytesPerSecond < 1 || !Job.TotalBytes.HasValue) return "—";
            var seconds = Math.Max(0, (Job.TotalBytes.Value - Interlocked.Read(ref Job.DownloadedBytes)) / Job.BytesPerSecond);
            return seconds >= 3600 ? $"{seconds / 3600:0.0} sa" : seconds >= 60 ? $"{seconds / 60:0} dk" : $"{seconds:0} sn";
        }
    }
    public string ActionIcon => Job.State is DownloadState.Downloading or DownloadState.Queued ? "\uE769" : Job.State == DownloadState.Completed ? "\uE73E" : "\uE768";
    public string ActionTooltip => Job.State is DownloadState.Downloading or DownloadState.Queued ? "Duraklat" : Job.State == DownloadState.Completed ? "Klasörde göster" : "İndirmeyi sürdür";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Update() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
