using System.Diagnostics;

namespace Fastdown.Core;

internal sealed class BandwidthLimiter(Func<long> getLimit)
{
    private readonly SemaphoreSlim _gate = new(1);
    private double _next;
    private long _last;
    public async Task WaitAsync(int bytes, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            while (true)
            {
                var limit = getLimit();
                var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                if (limit != _last) { _next = now; _last = limit; }
                if (limit <= 0) return;
                var delay = _next - now;
                if (delay <= 0) { _next = now + bytes / (double)limit; return; }
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, .15)), token);
            }
        }
        finally { _gate.Release(); }
    }
}
