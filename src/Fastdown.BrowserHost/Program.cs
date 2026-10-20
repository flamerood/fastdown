using System.Diagnostics;
using System.IO.Pipes;
using Fastdown.Core;

if (args.Length == 1 && args[0] is "--register" or "--unregister" or "--check-install")
{
    try { Environment.ExitCode = Registration.Run(args[0] == "--unregister", args[0] == "--check-install"); }
    catch (Exception ex) { Console.Error.WriteLine("Köprü kurulamadı: " + ex.Message); Environment.ExitCode = 1; }
    return;
}

// stdout is exclusively the browser's binary framing channel.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(125));
var output = Console.OpenStandardOutput();
try
{
    var manifestPath = Path.Combine(AppContext.BaseDirectory, "com.akis.download_manager.json");
    using var manifest = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, timeout.Token));
    var allowed = manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(x => x.GetString()).ToArray();
    var origin = args.FirstOrDefault();
    if (origin == null || !allowed.Contains(origin.EndsWith('/') ? origin : origin + "/", StringComparer.Ordinal))
        throw new UnauthorizedAccessException("Bu eklentinin Fastdown'a erişim izni yok.");
    var request = await BrowserProtocol.ReadAsync<BrowserRequest>(Console.OpenStandardInput(), timeout.Token) ?? throw new InvalidDataException("Tarayıcı mesajı alınamadı.");
    BrowserProtocol.Validate(request);
    using var pipe = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    try { await pipe.ConnectAsync(600, timeout.Token); }
    catch (TimeoutException)
    {
        if (request.Action == "ping")
        {
            await BrowserProtocol.WriteAsync(output, new BrowserReply(true, AppRunning: false), timeout.Token);
            return;
        }
        var app = Path.Combine(AppContext.BaseDirectory, "Fastdown.exe");
        if (!File.Exists(app)) throw new FileNotFoundException("Fastdown.exe köprü ile aynı klasörde bulunmalı.");
        Process.Start(new ProcessStartInfo(app, "--browser") { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        await pipe.ConnectAsync(10000, timeout.Token);
    }
    await BrowserProtocol.WriteAsync(pipe, request, timeout.Token);
    var reply = await BrowserProtocol.ReadAsync<BrowserReply>(pipe, timeout.Token) ?? new BrowserReply(false, "Fastdown bağlantıyı kapattı.");
    await BrowserProtocol.WriteAsync(output, reply, timeout.Token);
}
catch (Exception ex)
{
    try { await BrowserProtocol.WriteAsync(output, new BrowserReply(false, ex is OperationCanceledException ? "Fastdown'a bağlanılamadı. Uygulamayı yeniden açıp dene." : ex.Message), CancellationToken.None); }
    catch (IOException) { }
}
