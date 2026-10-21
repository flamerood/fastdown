using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Fastdown.App;

public partial class App : Application
{
    internal System.Diagnostics.ProcessStartInfo? PendingInstaller { get; set; }
    private Mutex? _instance;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.Length >= 2 && e.Args[0] == "--preview";
        _instance = new Mutex(true, preview ? "Fastdown.Preview" : "Local\\Akis.DownloadManager", out var created);
        if (!created) { if (!e.Args.Contains("--browser")) MessageBox.Show("Fastdown zaten açık. Görev çubuğundaki Fastdown penceresini kullan.", "Fastdown"); Shutdown(); return; }
        try
        {
            var preferred = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Fastdown");
            var legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akis");
            var data = preview ? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(e.Args[1]))!, "preview-data") : !File.Exists(Path.Combine(preferred, "library.json")) && File.Exists(Path.Combine(legacy, "library.json")) ? legacy : preferred;
            var window = new MainWindow(data, enableBrowser: !preview);
            MainWindow = window;
            window.Show();
            if (preview) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    window.UpdateLayout();
                    window.PreparePreview(e.Args.ElementAtOrDefault(2));
                    window.UpdateLayout();
                    var render = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    render.Render(window);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(render));
                    using (var stream = File.Create(e.Args[1])) encoder.Save(stream);
                    window.Close();
                }
                catch (Exception ex) { File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString()); Shutdown(1); }
            }));
        }
        catch (Exception ex)
        {
            if (preview) File.WriteAllText(e.Args[1] + ".error.txt", ex.ToString());
            else MessageBox.Show("Fastdown başlatılamadı: " + ex.Message, "Fastdown");
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _instance?.Dispose();
        if (PendingInstaller != null)
        {
            try { System.Diagnostics.Process.Start(PendingInstaller); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { MessageBox.Show("Kurulum başlatılamadı: " + ex.Message, "Fastdown"); }
        }
        base.OnExit(e);
    }
}
