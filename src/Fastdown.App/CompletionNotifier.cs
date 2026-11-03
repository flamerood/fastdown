using System.Drawing;
using System.Windows.Forms;

namespace Fastdown.App;

internal sealed class CompletionNotifier(Action<string> openFolder) : IDisposable
{
    private readonly Dictionary<NotifyIcon, System.Windows.Forms.Timer> _notifications = [];
    private bool _disposed;

    private NotifyIcon CreateNotification(string directoryPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // Each balloon owns its destination; a later download cannot redirect an earlier click.
        var icon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone(), Text = "Fastdown · İndirme yöneticisi" };
        var expiry = new System.Windows.Forms.Timer { Interval = 120000 };
        _notifications.Add(icon, expiry);
        void Open()
        {
            if (!_notifications.ContainsKey(icon)) return;
            try { openFolder(directoryPath); }
            finally { Release(icon); }
        }
        icon.BalloonTipClicked += (_, _) => Open();
        icon.DoubleClick += (_, _) => Open();
        icon.BalloonTipClosed += (_, _) => Release(icon);
        expiry.Tick += (_, _) => Release(icon);
        expiry.Start();
        return icon;
    }

    public void Show(string fileName, string directoryPath)
    {
        var icon = CreateNotification(directoryPath);
        try
        {
            icon.Visible = true;
            icon.ShowBalloonTip(5000, "İndirme tamamlandı", fileName[..Math.Min(fileName.Length, 160)] + "\nKlasörü açmak için tıkla.", ToolTipIcon.Info);
        }
        catch { Release(icon); throw; }
    }

    private void Release(NotifyIcon icon)
    {
        if (!_notifications.Remove(icon, out var expiry)) return;
        expiry.Stop(); expiry.Dispose();
        icon.Visible = false;
        var image = icon.Icon;
        icon.Dispose(); image?.Dispose();
    }
    public void Dispose()
    {
        _disposed = true;
        foreach (var icon in _notifications.Keys.ToArray()) Release(icon);
    }
}
