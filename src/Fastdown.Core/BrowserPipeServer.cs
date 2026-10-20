using System.IO.Pipes;

namespace Fastdown.Core;

public sealed class BrowserPipeServer : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly Func<BrowserRequest, CancellationToken, Task<BrowserReply>> _handler;
    private readonly string _name;
    public BrowserPipeServer(Func<BrowserRequest, CancellationToken, Task<BrowserReply>> handler, string? pipeName = null)
    {
        _handler = handler;
        _name = pipeName ?? BrowserProtocol.PipeName;
        _loop = RunAsync();
    }
    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(110));
                BrowserReply reply;
                try
                {
                    var request = await BrowserProtocol.ReadAsync<BrowserRequest>(pipe, timeout.Token);
                    if (request == null) continue;
                    BrowserProtocol.Validate(request);
                    reply = await _handler(request, timeout.Token);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or HttpRequestException or InvalidDataException or OperationCanceledException)
                {
                    reply = new BrowserReply(false, ex is OperationCanceledException ? "Fastdown zamanında yanıt vermedi. Tekrar dene." : ex.Message);
                }
                // Even a timed-out request gets a bounded error response when the client remains connected.
                using var sendTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await BrowserProtocol.WriteAsync(pipe, reply, sendTimeout.Token);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (!_stop.IsCancellationRequested) await Task.Delay(100, _stop.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _stop.Dispose();
    }
}
