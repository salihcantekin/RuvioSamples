using System.Diagnostics;
using System.Globalization;

namespace RuvioExamples.LoadKit;

/// <summary>
/// Drives <see cref="LoadScenario.Clients"/> virtual clients against one or more API instances and reports every
/// response through <see cref="ResultReceived"/>. Open loop: a slow response does not slow the next request down,
/// like real users.
/// </summary>
public sealed class LoadRunner : IDisposable
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 512,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        UseProxy = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    public event Action<RequestResult>? ResultReceived;

    public async Task RunAsync(LoadScenario scenario, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        if (scenario.Targets.Count == 0)
            throw new ArgumentException("At least one target is required.", nameof(scenario));

        long startedAt = Stopwatch.GetTimestamp();
        using var duration = scenario.Duration is { } limit ? new CancellationTokenSource(limit) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, duration.Token);
        var pending = new PendingRequests();

        var clients = Enumerable.Range(0, scenario.Clients)
            .Select(index => RunClientAsync(scenario, index, startedAt, pending, linked.Token))
            .ToArray();

        await Task.WhenAll(clients);

        // Let requests that are already on the wire report back, so counters add up.
        await pending.DrainAsync(TimeSpan.FromSeconds(5));
    }

    private async Task RunClientAsync(
        LoadScenario scenario, int index, long startedAt, PendingRequests pending, CancellationToken token)
    {
        var random = new Random(HashCode.Combine(index, Environment.TickCount));
        string clientId = scenario.ClientId(index);
        int targetCursor = index;

        void Send()
        {
            var target = scenario.Targets[targetCursor++ % scenario.Targets.Count];
            pending.Add(SendAsync(scenario, index, clientId, target, startedAt));
        }

        try
        {
            if (scenario.ClientStaggerMs > 0)
                await Task.Delay(scenario.ClientStaggerMs * index, token);

            if (scenario.Bursts is { } plan)
            {
                await RunBurstsAsync(plan, Send, token);
                return;
            }

            double interval = 1000.0 / Math.Max(0.05, scenario.RequestsPerSecondPerClient);
            double jitter = Math.Clamp(scenario.JitterPercent, 0, 100) / 100.0;

            // Start each client at a random point inside its first gap so they do not all fire on the same tick.
            await Task.Delay(TimeSpan.FromMilliseconds(random.NextDouble() * interval), token);

            long next = Stopwatch.GetTimestamp();

            while (!token.IsCancellationRequested)
            {
                Send();

                double gap = interval * (1 + (((random.NextDouble() * 2) - 1) * jitter));
                next += (long)(gap * Stopwatch.Frequency / 1000.0);
                var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), next);

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop pressed or the duration ran out: expected.
        }
    }

    private static async Task RunBurstsAsync(BurstPlan plan, Action send, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            long cycleStart = Stopwatch.GetTimestamp();

            foreach (var step in plan.Steps)
            {
                await DelayUntilAsync(cycleStart, step.AtSeconds, token);

                for (int i = 0; i < step.Count; i++)
                    send();
            }

            if (plan.CycleSeconds <= 0)
            {
                await Task.Delay(Timeout.Infinite, token);
                return;
            }

            await DelayUntilAsync(cycleStart, plan.CycleSeconds, token);
        }
    }

    private static async Task DelayUntilAsync(long cycleStart, double seconds, CancellationToken token)
    {
        var wait = TimeSpan.FromSeconds(seconds) - Stopwatch.GetElapsedTime(cycleStart);

        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, token);
    }

    private async Task SendAsync(LoadScenario scenario, int index, string clientId, Uri target, long startedAt)
    {
        string path = scenario.WorkParameter is { Length: > 0 } parameter
            ? string.Create(CultureInfo.InvariantCulture, $"{scenario.Path}{(scenario.Path.Contains('?') ? '&' : '?')}{parameter}={scenario.ServerWorkMs}")
            : scenario.Path;

        using var request = new HttpRequestMessage(new HttpMethod(scenario.Method), new Uri(target, path));
        request.Headers.TryAddWithoutValidation(scenario.ClientIdHeader, clientId);

        long sent = Stopwatch.GetTimestamp();
        RequestResult result;

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            double latency = Stopwatch.GetElapsedTime(sent).TotalMilliseconds;
            int status = (int)response.StatusCode;

            string? instance = response.Headers.TryGetValues(scenario.InstanceHeader, out var values) ? values.FirstOrDefault() : null;
            double? retry = response.Headers.RetryAfter?.Delta is { } delta ? delta.TotalSeconds : null;

            result = new RequestResult(
                index, clientId, Stopwatch.GetElapsedTime(startedAt), status, scenario.Classify(status), latency, instance, retry, null);
        }
        catch (Exception error)
        {
            result = new RequestResult(
                index, clientId, Stopwatch.GetElapsedTime(startedAt), 0, Outcome.Error,
                Stopwatch.GetElapsedTime(sent).TotalMilliseconds, null, null, error.GetBaseException().Message);
        }

        ResultReceived?.Invoke(result);
    }

    public void Dispose() => _http.Dispose();

    private sealed class PendingRequests
    {
        private readonly object _gate = new();
        private readonly List<Task> _tasks = [];

        public void Add(Task task)
        {
            lock (_gate)
            {
                _tasks.RemoveAll(static t => t.IsCompleted);
                _tasks.Add(task);
            }
        }

        public async Task DrainAsync(TimeSpan timeout)
        {
            Task[] snapshot;

            lock (_gate)
                snapshot = [.. _tasks];

            await Task.WhenAny(Task.WhenAll(snapshot), Task.Delay(timeout));
        }
    }
}
