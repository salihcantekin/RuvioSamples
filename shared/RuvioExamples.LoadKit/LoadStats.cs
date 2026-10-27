namespace RuvioExamples.LoadKit;

public readonly record struct SecondBucket(int Second, int Success, int Rejected, int Unavailable, int Error)
{
    public int Total => Success + Rejected + Unavailable + Error;
}

public sealed record LaneSnapshot(int ClientIndex, string ClientId, long Success, long Rejected, long Other, byte[] Recent);

public sealed record LoadSnapshot(
    long Sent,
    long Success,
    long Rejected,
    long Unavailable,
    long Errors,
    double RequestsPerSecond,
    double P50Ms,
    double P95Ms,
    IReadOnlyList<SecondBucket> Seconds,
    IReadOnlyList<LaneSnapshot> Lanes,
    IReadOnlyDictionary<string, long> ByInstance,
    IReadOnlyList<RequestResult> Recent,
    double? LastRetryAfterSeconds);

/// <summary>Thread-safe aggregation of <see cref="RequestResult"/>s. The UI reads snapshots on a timer.</summary>
public sealed class LoadStats
{
    private const int LaneCapacity = 240;
    private const int LatencyWindow = 2000;
    private const int RecentCapacity = 40;

    private readonly object _gate = new();
    private readonly List<SecondBucket> _seconds = [];
    private readonly Dictionary<int, Lane> _lanes = [];
    private readonly Dictionary<string, long> _byInstance = [];
    private readonly Queue<RequestResult> _recent = new();
    private readonly double[] _latencies = new double[LatencyWindow];
    private readonly Queue<TimeSpan> _lastSeconds = new();
    private int _latencyCount;
    private int _latencyCursor;
    private long _sent, _allowed, _limited, _unavailable, _errors;
    private double? _lastRetryAfter;

    public void Reset()
    {
        lock (_gate)
        {
            _seconds.Clear();
            _lanes.Clear();
            _byInstance.Clear();
            _recent.Clear();
            _lastSeconds.Clear();
            _latencyCount = _latencyCursor = 0;
            _sent = _allowed = _limited = _unavailable = _errors = 0;
            _lastRetryAfter = null;
        }
    }

    public void Add(RequestResult result)
    {
        lock (_gate)
        {
            _sent++;

            int second = (int)result.SinceStart.TotalSeconds;

            while (_seconds.Count <= second)
                _seconds.Add(new SecondBucket(_seconds.Count, 0, 0, 0, 0));

            var bucket = _seconds[second];

            if (!_lanes.TryGetValue(result.ClientIndex, out var lane))
                _lanes[result.ClientIndex] = lane = new Lane(result.ClientId);

            switch (result.Outcome)
            {
                case Outcome.Success:
                    _allowed++;
                    bucket = bucket with { Success = bucket.Success + 1 };
                    lane.Success++;
                    break;
                case Outcome.Rejected:
                    _limited++;
                    bucket = bucket with { Rejected = bucket.Rejected + 1 };
                    lane.Rejected++;
                    _lastRetryAfter = result.RetryAfterSeconds ?? _lastRetryAfter;
                    break;
                case Outcome.Unavailable:
                    _unavailable++;
                    bucket = bucket with { Unavailable = bucket.Unavailable + 1 };
                    lane.Other++;
                    break;
                default:
                    _errors++;
                    bucket = bucket with { Error = bucket.Error + 1 };
                    lane.Other++;
                    break;
            }

            _seconds[second] = bucket;
            lane.Push((byte)result.Outcome);

            if (result.Instance is { } instance && result.Outcome == Outcome.Success)
                _byInstance[instance] = _byInstance.GetValueOrDefault(instance) + 1;

            // Latency percentiles describe requests the server answered, not refused connections.
            if (result.StatusCode != 0)
            {
                _latencies[_latencyCursor] = result.LatencyMs;
                _latencyCursor = (_latencyCursor + 1) % LatencyWindow;
                _latencyCount = Math.Min(_latencyCount + 1, LatencyWindow);
            }

            _lastSeconds.Enqueue(result.SinceStart);
            _recent.Enqueue(result);

            while (_recent.Count > RecentCapacity)
                _recent.Dequeue();
        }
    }

    public LoadSnapshot Snapshot(TimeSpan now)
    {
        lock (_gate)
        {
            while (_lastSeconds.Count > 0 && now - _lastSeconds.Peek() > TimeSpan.FromSeconds(2))
                _lastSeconds.Dequeue();

            double rps = _lastSeconds.Count / 2.0;

            double p50 = 0, p95 = 0;

            if (_latencyCount > 0)
            {
                var sorted = _latencies.AsSpan(0, _latencyCount).ToArray();
                Array.Sort(sorted);
                p50 = sorted[(int)(sorted.Length * 0.50)];
                p95 = sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.95))];
            }

            var lanes = _lanes
                .OrderBy(pair => pair.Key)
                .Select(pair => new LaneSnapshot(
                    pair.Key, pair.Value.ClientId, pair.Value.Success, pair.Value.Rejected, pair.Value.Other, pair.Value.Recent()))
                .ToArray();

            return new LoadSnapshot(
                _sent, _allowed, _limited, _unavailable, _errors, rps, p50, p95,
                [.. _seconds], lanes, new Dictionary<string, long>(_byInstance), [.. _recent], _lastRetryAfter);
        }
    }

    private sealed class Lane(string clientId)
    {
        private readonly byte[] _ring = new byte[LaneCapacity];
        private int _count;
        private int _cursor;

        public string ClientId { get; } = clientId;
        public long Success;
        public long Rejected;
        public long Other;

        public void Push(byte outcome)
        {
            _ring[_cursor] = outcome;
            _cursor = (_cursor + 1) % LaneCapacity;
            _count = Math.Min(_count + 1, LaneCapacity);
        }

        /// <summary>Oldest first.</summary>
        public byte[] Recent()
        {
            var copy = new byte[_count];
            int start = (_cursor - _count + LaneCapacity) % LaneCapacity;

            for (int i = 0; i < _count; i++)
                copy[i] = _ring[(start + i) % LaneCapacity];

            return copy;
        }
    }
}
