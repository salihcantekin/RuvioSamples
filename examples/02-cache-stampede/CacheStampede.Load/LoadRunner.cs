using System.Diagnostics;
using System.Text.Json;
using Spectre.Console;

namespace CacheStampede.Load;

/// <summary>
/// Sends requests at a constant rate, like k6's constant-arrival-rate executor. A request that finds every virtual user busy is dropped.
/// A second loop reads each instance's <c>/api/stats</c> to count the database queries.
/// </summary>
public sealed class LoadRunner(Plan plan, Metrics metrics)
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        MaxConnectionsPerServer = 1024,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    private readonly Stopwatch _clock = new();
    private long _baseline;
    private long _last;

    public double Elapsed => _clock.Elapsed.TotalSeconds;

    /// <summary>Cache TTL reported by the API, used to estimate how many expiries happened during the run.</summary>
    public double CacheSeconds { get; private set; }

    public long DatabaseQueries => _last - _baseline;

    public async Task<bool> CheckAsync()
    {
        foreach (string target in plan.Targets)
        {
            try
            {
                using var response = await _http.GetAsync($"{target}/api/stats");
                response.EnsureSuccessStatusCode();
            }
            catch (Exception)
            {
                AnsiConsole.MarkupLine($"[red]Cannot reach {target}.[/] Start the API first: [cyan]./scripts/api-cache.ps1[/] (and Ruvio: [cyan]./scripts/up.ps1[/]).");
                return false;
            }
        }

        return true;
    }

    /// <summary>Returns the first failure, if any. The run goes on either way: the database counter is read as a baseline.</summary>
    public async Task<string?> ResetAsync()
    {
        foreach (string target in plan.Targets)
        {
            try
            {
                using var response = await _http.PostAsync($"{target}/api/admin/reset", null);

                if (!response.IsSuccessStatusCode)
                    return $"{target} answered {(int)response.StatusCode}";
            }
            catch (Exception)
            {
                return $"{target} did not answer";
            }
        }

        return null;
    }

    public async Task RunAsync()
    {
        _baseline = await ReadQueriesAsync(CancellationToken.None);
        _last = _baseline;

        using var pollerStop = new CancellationTokenSource();
        Task poller = PollDatabaseAsync(pollerStop.Token);
        _clock.Start();

        var virtualUsers = new SemaphoreSlim(plan.VirtualUsers);
        double interval = 1.0 / plan.RequestsPerSecond;
        long sequence = 0;

        while (Elapsed < plan.DurationSeconds)
        {
            double due = sequence * interval;

            if (due > Elapsed)
            {
                await Task.Delay(TimeSpan.FromSeconds(due - Elapsed));
                continue;
            }

            sequence++;

            if (!virtualUsers.Wait(0))
            {
                metrics.Dropped();
                continue;
            }

            string target = plan.Targets[(int)((sequence - 1) % plan.Targets.Count)];
            _ = SendAsync(target, virtualUsers);
        }

        // Every virtual user comes back only when its request has finished.
        for (int i = 0; i < plan.VirtualUsers; i++)
            await virtualUsers.WaitAsync();

        pollerStop.Cancel();
        try { await poller; } catch (OperationCanceledException) { }

        long total = await ReadQueriesAsync(CancellationToken.None);
        metrics.Queries(Math.Max(0, total - _last), Elapsed);
        _last = total;
    }

    private async Task SendAsync(string target, SemaphoreSlim virtualUsers)
    {
        double sentAt = Elapsed;
        metrics.Sent(sentAt);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await _http.GetAsync($"{target}{plan.Endpoint.Path}?workMs={plan.WorkMs}");
            double milliseconds = stopwatch.Elapsed.TotalMilliseconds;

            string instance = response.Headers.TryGetValues("X-Instance", out var values) ? values.First() : target;
            string? source = response.IsSuccessStatusCode ? await ReadSourceAsync(response) : null;

            metrics.Completed((int)response.StatusCode, milliseconds, source, instance);
        }
        catch (Exception)
        {
            metrics.Completed(0, stopwatch.Elapsed.TotalMilliseconds, null, target);
        }
        finally
        {
            virtualUsers.Release();
        }
    }

    private static async Task<string?> ReadSourceAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("source", out var source) ? source.GetString() : null;
    }

    private async Task PollDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                long total = await ReadQueriesAsync(cancellationToken);
                metrics.Queries(Math.Max(0, total - _last), Elapsed);
                _last = total;

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<long> ReadQueriesAsync(CancellationToken cancellationToken)
    {
        long total = 0;

        foreach (string target in plan.Targets)
        {
            try
            {
                using var document = JsonDocument.Parse(await _http.GetStringAsync($"{target}/api/stats", cancellationToken));
                var root = document.RootElement;

                total += root.GetProperty("database").GetProperty("queries").GetInt64();
                CacheSeconds = root.GetProperty("cacheSeconds").GetDouble();
            }
            catch (HttpRequestException)
            {
                // An instance that does not answer contributes nothing to this sample.
            }
        }

        return total;
    }
}
