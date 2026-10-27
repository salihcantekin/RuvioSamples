namespace RuvioExamples.LoadKit;

public enum Outcome : byte
{
    /// <summary>2xx: the request was served.</summary>
    Success = 1,

    /// <summary>The API refused it on purpose (429 by default; an example can name other status codes).</summary>
    Rejected = 2,

    /// <summary>503: the API said it cannot serve right now.</summary>
    Unavailable = 3,

    /// <summary>Anything else: other status codes, timeouts, refused connections.</summary>
    Error = 4,
}

public readonly record struct RequestResult(
    int ClientIndex,
    string ClientId,
    TimeSpan SinceStart,
    int StatusCode,
    Outcome Outcome,
    double LatencyMs,
    string? Instance,
    double? RetryAfterSeconds,
    string? Error);
