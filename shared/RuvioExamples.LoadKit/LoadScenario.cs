namespace RuvioExamples.LoadKit;

/// <summary>One group of requests inside a burst plan: <see cref="Count"/> requests at once, <see cref="AtSeconds"/> after the cycle starts.</summary>
public sealed class BurstStep
{
    public double AtSeconds { get; init; }

    public int Count { get; init; } = 1;
}

/// <summary>
/// A scripted traffic shape instead of a steady stream. Every client runs the steps in order, then waits until
/// <see cref="CycleSeconds"/> have passed since the cycle began and starts over (0 = run the steps once).
/// </summary>
public sealed class BurstPlan
{
    public IReadOnlyList<BurstStep> Steps { get; init; } = [];

    public double CycleSeconds { get; init; }
}

/// <summary>Everything the runner needs. Nothing in here knows which Ruvio feature the API under test demonstrates.</summary>
public sealed record LoadScenario
{
    /// <summary>Base addresses of the API instances. Clients pick them round-robin, like a dumb load balancer.</summary>
    public required IReadOnlyList<Uri> Targets { get; init; }

    public string Path { get; init; } = "/";

    public string Method { get; init; } = "GET";

    /// <summary>Number of virtual clients. Each one sends its own identity header unless <see cref="SharedIdentity"/> is set.</summary>
    public int Clients { get; init; } = 3;

    public double RequestsPerSecondPerClient { get; init; } = 2;

    /// <summary>0-100. How far each gap between two requests may drift (random), so traffic does not look like a metronome.</summary>
    public int JitterPercent { get; init; } = 25;

    /// <summary>Fake work the server does per request, sent as the <see cref="WorkParameter"/> query value.</summary>
    public int ServerWorkMs { get; init; }

    /// <summary>Fake wait before each client sends its first request (client i waits i * this).</summary>
    public int ClientStaggerMs { get; init; }

    /// <summary>Stop by itself after this long. Null means run until stopped.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Null: a steady stream at <see cref="RequestsPerSecondPerClient"/>.</summary>
    public BurstPlan? Bursts { get; init; }

    /// <summary>All clients send the same identity: one budget, one lock owner, one counter for everybody.</summary>
    public bool SharedIdentity { get; init; }

    public string ClientIdPrefix { get; init; } = "client";

    public string ClientIdHeader { get; init; } = "X-Client-Id";

    public string InstanceHeader { get; init; } = "X-Instance";

    /// <summary>Query parameter that carries <see cref="ServerWorkMs"/>. Null: do not send it.</summary>
    public string? WorkParameter { get; init; } = "workMs";

    public IReadOnlyList<int> RejectedStatusCodes { get; init; } = [429];

    public string ClientId(int index) => SharedIdentity ? ClientIdPrefix + "-shared" : $"{ClientIdPrefix}-{index + 1}";

    public Outcome Classify(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => Outcome.Success,
        503 => Outcome.Unavailable,
        0 => Outcome.Error,
        _ => RejectedStatusCodes.Contains(statusCode) ? Outcome.Rejected : Outcome.Error,
    };
}
