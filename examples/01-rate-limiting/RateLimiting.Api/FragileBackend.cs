using System.Diagnostics;

namespace RateLimiting.Api;

public sealed class FragileBackend(int capacity)
{
    private int _inFlight;
    private int _peak;
    private long _served;
    private long _overloaded;
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public int Capacity { get; } = capacity;

    public async Task<bool> WorkAsync(int workMs, CancellationToken cancellationToken)
    {
        int now = Interlocked.Increment(ref _inFlight);

        try
        {
            int peak;

            while (now > (peak = Volatile.Read(ref _peak)))
            {
                if (Interlocked.CompareExchange(ref _peak, now, peak) == peak)
                    break;
            }

            if (now > Capacity * 2)
            {
                Interlocked.Increment(ref _overloaded);
                return false;
            }

            double slowdown = now > Capacity ? (double)now / Capacity : 1;
            await Task.Delay(TimeSpan.FromMilliseconds(workMs * slowdown), cancellationToken);

            Interlocked.Increment(ref _served);
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public BackendStats Stats()
    {
        return new BackendStats(
            Volatile.Read(ref _inFlight), Volatile.Read(ref _peak), Interlocked.Read(ref _served),
            Interlocked.Read(ref _overloaded), Capacity, Stopwatch.GetElapsedTime(_startedAt).TotalSeconds);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _peak, 0);
        Interlocked.Exchange(ref _served, 0);
        Interlocked.Exchange(ref _overloaded, 0);
    }
}

public sealed record BackendStats(int InFlight, int PeakInFlight, long Served, long Overloaded, int Capacity, double UptimeSeconds);
