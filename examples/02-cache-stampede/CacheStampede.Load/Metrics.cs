namespace CacheStampede.Load;

/// <summary>Thread-safe counters and per-second series. The load runner writes, the dashboard reads snapshots.</summary>
public sealed class Metrics
{
    public const int Window = 20;

    private readonly object _gate = new();
    private readonly List<double> _latencies = [];
    private readonly Dictionary<string, int> _sources = [];
    private readonly Dictionary<string, int> _instances = [];
    private readonly Dictionary<int, int> _sentPerSecond = [];
    private readonly Dictionary<int, int> _queriesPerSecond = [];
    private int _sent;
    private int _served;
    private int _failed;
    private int _dropped;
    private int _inFlight;
    private long _queries;
    private string? _lastError;

    public void Sent(double at)
    {
        lock (_gate)
        {
            _sent++;
            _inFlight++;
            Add(_sentPerSecond, Second(at), 1);
        }
    }

    public void Dropped()
    {
        lock (_gate) _dropped++;
    }

    /// <summary>Status 0 means the request never got an answer.</summary>
    public void Completed(int status, double milliseconds, string? source, string instance)
    {
        lock (_gate)
        {
            _inFlight--;
            _latencies.Add(milliseconds);

            if (status is >= 200 and < 300) _served++;
            else _failed++;

            _lastError = status switch
            {
                0 => "no answer from the API: is it running?",
                404 => "404: this endpoint is not on the API (is it uncommented in Program.cs?)",
                >= 500 => $"HTTP {status}: is Ruvio running?",
                _ => _lastError,
            };

            if (source is not null) Add(_sources, source, 1);
            Add(_instances, instance, 1);
        }
    }

    public void Queries(long delta, double at)
    {
        lock (_gate)
        {
            _queries += delta;
            Add(_queriesPerSecond, Second(at), (int)delta);
        }
    }

    public Snapshot Take(double elapsed)
    {
        lock (_gate)
        {
            double[] sorted = [.. _latencies.Order()];
            int now = Second(elapsed);

            return new Snapshot(
                _sent, _served, _failed, _dropped, _inFlight, _queries,
                Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99), sorted.Length == 0 ? 0 : sorted[^1],
                new Dictionary<string, int>(_sources),
                new Dictionary<string, int>(_instances),
                Series(_sentPerSecond, now),
                Series(_queriesPerSecond, now),
                _lastError);
        }
    }

    private static int Second(double at) => (int)at;

    private static void Add<TKey>(Dictionary<TKey, int> counts, TKey key, int amount) where TKey : notnull
    {
        counts[key] = counts.GetValueOrDefault(key) + amount;
    }

    private static int[] Series(Dictionary<int, int> counts, int now)
    {
        return Enumerable.Range(now - Window + 1, Window).Select(second => counts.GetValueOrDefault(second)).ToArray();
    }

    private static double Percentile(double[] sorted, double p)
    {
        return sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
    }
}

public sealed record Snapshot(
    int Sent,
    int Served,
    int Failed,
    int Dropped,
    int InFlight,
    long Queries,
    double P50,
    double P95,
    double P99,
    double Max,
    IReadOnlyDictionary<string, int> Sources,
    IReadOnlyDictionary<string, int> Instances,
    int[] SentPerSecond,
    int[] QueriesPerSecond,
    string? LastError);
