using System.Drawing;
using System.Windows.Forms;

namespace Fastdown.App;

internal sealed class CompletionNotifier(Action activate) : IDisposable
{
    private NotifyIcon? _icon;
    public void Show(string fileName)
    {
        if (_icon == null)
        {
            _icon = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? (Icon)SystemIcons.Application.Clone(), Text = "Fastdown · İndirme yöneticisi" };
            _icon.DoubleClick += (_, _) => activate();
        }
        _icon.Visible = true;
        _icon.ShowBalloonTip(5000, "İndirme tamamlandı", fileName[..Math.Min(fileName.Length, 200)], ToolTipIcon.Info);
    }
    public void Dispose() { if (_icon != null) { _icon.Visible = false; var icon = _icon.Icon; _icon.Dispose(); icon?.Dispose(); } }
}
