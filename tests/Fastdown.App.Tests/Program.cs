using System.Reflection;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var type = typeof(Fastdown.App.MainWindow).Assembly.GetType("Fastdown.App.CompletionNotifier", true)!;
        var opened = new List<string>();
        using var notifier = (IDisposable)Activator.CreateInstance(type, (Action<string>)opened.Add)!;
        var create = type.GetMethod("CreateNotification", BindingFlags.Instance | BindingFlags.NonPublic)!;
        NotifyIcon Create(string folder) => (NotifyIcon)create.Invoke(notifier, [folder])!;
        static void Raise(NotifyIcon icon, string name) { var method = typeof(NotifyIcon).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!; method.Invoke(icon, method.GetParameters().Length == 0 ? [] : [EventArgs.Empty]); }
        static void Assert(bool success, string message) { if (!success) throw new Exception(message); }

        // Register handlers without displaying OS notifications or opening real folders.
        var first = Create(@"C:\İndirilen dosyalar\Birinci");
        var second = Create(@"D:\Diğer klasör\İkinci");
        Assert(opened.Count == 0, "Creating a notification performed a completion action");
        Raise(first, "OnBalloonTipClicked");
        Assert(opened.SequenceEqual([@"C:\İndirilen dosyalar\Birinci"]), "Later completion redirected an earlier notification");
        Raise(first, "OnBalloonTipClicked");
        Assert(opened.Count == 1, "A closed notification opened twice");
        Raise(second, "OnDoubleClick");
        Assert(opened.Last() == @"D:\Diğer klasör\İkinci", "Tray double click did not use its own destination");
        var dismissed = Create(@"C:\Do not open");
        Raise(dismissed, "OnBalloonTipClosed");
        Raise(dismissed, "OnBalloonTipClicked");
        Assert(opened.Count == 2, "Dismissing a notification opened a folder");
        var pending = Create(@"C:\Closed app");
        notifier.Dispose();
        Raise(pending, "OnBalloonTipClicked");
        Assert(opened.Count == 2, "Clicking after app shutdown opened a folder");
        Console.WriteLine("PASS Notification clicks retain each file's folder, dismissal is silent, callbacks run once, shutdown releases pending notifications");
    }
}
