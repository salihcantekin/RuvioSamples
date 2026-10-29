using System.Diagnostics;

namespace CacheStampede.Api;

/// <summary>Stands in for the database behind the cache. Every call is one query and takes <c>workMs</c>.</summary>
public sealed class SlowDatabase
{
    private int _inFlight;
    private int _peak;
    private long _queries;
    private readonly long _startedAt = Stopwatch.GetTimestamp();

    public async Task<Product> LoadProductAsync(string builtBy, int workMs, CancellationToken cancellationToken)
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

            Interlocked.Increment(ref _queries);
            await Task.Delay(workMs, cancellationToken);

            return new Product(42, "Ruvio mug", 19.90m, builtBy);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }

    public DatabaseStats Stats()
    {
        return new DatabaseStats(
            Volatile.Read(ref _inFlight), Volatile.Read(ref _peak), Interlocked.Read(ref _queries),
            Stopwatch.GetElapsedTime(_startedAt).TotalSeconds);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _peak, 0);
        Interlocked.Exchange(ref _queries, 0);
    }
}

public sealed record DatabaseStats(int InFlight, int PeakInFlight, long Queries, double UptimeSeconds);
