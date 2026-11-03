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
                // Preserve elapsed credit after a coarse Windows timer wakes late.
                // Limit idle credit to 50 ms so waking/resuming cannot cause an unbounded burst.
                if (delay <= 0) { _next = Math.Max(_next, now - .05) + bytes / (double)limit; return; }
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay, .002, .15)), token);
            }
        }
        finally { _gate.Release(); }
    }
}
