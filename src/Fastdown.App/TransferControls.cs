using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Fastdown.Core;

namespace Fastdown.App;

public partial class MainWindow
{
    private Point _dragStart;
    private DownloadJob? _dragJob;
    private static ListBoxItem? FindRow(DependencyObject? element)
    {
        while (element != null) { if (element is ListBoxItem row) return row; element = VisualTreeHelper.GetParent(element); }
        return null;
    }
    private void Queue_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(DownloadList);
        _dragJob = FindRow(e.OriginalSource as DependencyObject)?.DataContext is DownloadRow row ? row.Job : null;
    }
    private void Queue_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragJob?.State != DownloadState.Queued || _filter != "queued") return;
        var position = e.GetPosition(DownloadList);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var job = _dragJob; _dragJob = null;
        DragDrop.DoDragDrop(DownloadList, new DataObject("Fastdown.Queue", job.Id.ToString()), DragDropEffects.Move);
    }
    private void Queue_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _filter == "queued" && e.Data.GetDataPresent("Fastdown.Queue") ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }
    private void Queue_Drop(object sender, DragEventArgs e)
    {
        if (_filter != "queued" || !Guid.TryParse(e.Data.GetData("Fastdown.Queue") as string, out var id)) return;
        var job = _manager.Snapshot().FirstOrDefault(j => j.Id == id);
        var target = FindRow(e.OriginalSource as DependencyObject);
        var before = target?.DataContext is DownloadRow row ? row.Job : null;
        if (target != null && before != null && e.GetPosition(target).Y >= target.ActualHeight / 2)
        {
            var queue = _manager.Snapshot().Where(j => j.State == DownloadState.Queued).OrderBy(j => j.QueueOrder).ToList();
            var index = queue.IndexOf(before);
            before = index >= 0 && index + 1 < queue.Count ? queue[index + 1] : null;
        }
        if (job != null && _manager.MoveQueued(job, before)) { Refresh(); Notify("Kuyruk sırası kaydedildi."); }
        e.Handled = true;
    }
    private void Queue_ContextOpening(object sender, ContextMenuEventArgs e)
    {
        if (FindRow(e.OriginalSource as DependencyObject)?.DataContext is DownloadRow row) DownloadList.SelectedItem = row;
        if (Selected == null) e.Handled = true;
    }
    private void Priority_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } job) return;
        var queue = _manager.Snapshot().Where(j => j.State == DownloadState.Queued).OrderBy(j => j.QueueOrder).ToList();
        var index = queue.IndexOf(job);
        if (index < 0) { Notify("Öncelik sırada bekleyen indirmeler için değiştirilebilir."); return; }
        var direction = (sender as FrameworkElement)?.Tag as string;
        var target = direction == "first" ? 0 : direction == "up" ? Math.Max(0, index - 1) : Math.Min(queue.Count, index + 2);
        if (_manager.MoveQueued(job, target < queue.Count ? queue[target] : null)) { Refresh(); Notify("Kuyruk önceliği kaydedildi. Açık indirmeler devam ediyor."); }
    }
    private void JobSpeed_Click(object sender, RoutedEventArgs e)
    {
        if (Selected == null) return;
        JobSpeedTitle.Text = Selected.FileName;
        JobSpeedInput.Text = (Selected.SpeedLimitBytes / 1024).ToString(CultureInfo.InvariantCulture);
        JobSpeedOverlay.Tag = Selected;
        JobSpeedError.Text = ""; JobSpeedOverlay.Visibility = Visibility.Visible;
        JobSpeedInput.Focus(); JobSpeedInput.SelectAll();
    }
    private void CancelJobSpeed_Click(object sender, RoutedEventArgs e) => JobSpeedOverlay.Visibility = Visibility.Collapsed;
    private async void SaveJobSpeed_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!long.TryParse(JobSpeedInput.Text.Trim(), out var kb) || kb < 0 || kb > 10_000_000) throw new ArgumentException("0 veya 16–10000000 KB/sn gir.");
            if (JobSpeedOverlay.Tag is not DownloadJob job) return;
            JobSpeedOverlay.IsEnabled = false;
            await _manager.SetJobSpeedLimitAsync(job, kb * 1024);
            JobSpeedOverlay.Visibility = Visibility.Collapsed;
            Refresh(); Notify("Dosyanın hız sınırı kaydedildi.");
        }
        catch (ArgumentException ex) { JobSpeedError.Text = ex.Message; }
        finally { JobSpeedOverlay.IsEnabled = true; }
    }
}
