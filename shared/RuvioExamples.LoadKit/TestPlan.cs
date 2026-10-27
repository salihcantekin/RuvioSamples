using System.Globalization;

namespace RuvioExamples.LoadKit;

/// <summary>
/// One test, as the answers to a handful of questions: where, which endpoint, how many clients, how fast, how long.
/// The wizard, the presets and the advanced panel all produce one; <see cref="ToScenario"/> turns it into a run.
/// </summary>
public sealed record TestPlan
{
    public required IReadOnlyList<Uri> Targets { get; init; }

    public required string EndpointId { get; init; }

    public int Clients { get; init; } = 3;

    public double Rate { get; init; } = 3;

    public int JitterPercent { get; init; } = 30;

    public int WorkMs { get; init; } = 100;

    public int StaggerMs { get; init; }

    public int DurationSeconds { get; init; } = 30;

    public string? BurstId { get; init; }

    public bool SharedIdentity { get; init; }

    public LoadScenario ToScenario(ExampleManifest manifest)
    {
        var endpoint = manifest.FindEndpoint(EndpointId) ?? manifest.Endpoints[0];

        return new LoadScenario
        {
            Targets = Targets,
            Path = endpoint.Path,
            Method = endpoint.Method,
            Clients = Clients,
            RequestsPerSecondPerClient = Rate,
            JitterPercent = JitterPercent,
            ServerWorkMs = WorkMs,
            ClientStaggerMs = StaggerMs,
            Duration = DurationSeconds > 0 ? TimeSpan.FromSeconds(DurationSeconds) : null,
            Bursts = manifest.FindBurst(BurstId)?.ToPlan(),
            SharedIdentity = SharedIdentity,
            ClientIdHeader = manifest.ClientIdHeader,
            InstanceHeader = manifest.InstanceHeader,
            WorkParameter = manifest.WorkParameter,
            RejectedStatusCodes = manifest.Outcomes.Rejected.Statuses.Count > 0 ? manifest.Outcomes.Rejected.Statuses : [429],
        };
    }

    /// <summary>The whole test in plain sentences, for the last wizard screen.</summary>
    public string Describe(ExampleManifest manifest)
    {
        var endpoint = manifest.FindEndpoint(EndpointId) ?? manifest.Endpoints[0];
        var burst = manifest.FindBurst(BurstId);
        var culture = CultureInfo.InvariantCulture;

        string who = SharedIdentity ? "all sharing one identity" : Clients == 1 ? "with its own identity" : "each with its own identity";
        string clients = string.Create(culture, $"{Clients} virtual {(Clients == 1 ? "client" : "clients")}, {who},");
        string where = Targets.Count == 1
            ? "to one API instance"
            : string.Create(culture, $"spread over {Targets.Count} API instances");
        string length = DurationSeconds > 0 ? string.Create(culture, $"for {DurationSeconds} seconds") : "until you press Stop";
        string work = WorkMs > 0 ? string.Create(culture, $" The server pretends each request takes {WorkMs} ms.") : string.Empty;

        string text = burst is null
            ? string.Create(culture, $"{clients} will send {Rate:0.#} requests per second {(Clients == 1 ? string.Empty : "each ")}({Clients * Rate:0.#} per second in total) to {endpoint.Path} ({endpoint.Label}), {where}, {length}.{work}")
            : string.Create(culture, $"{clients} will fire the \"{burst.Label}\" pattern on {endpoint.Path} ({endpoint.Label}), {where}, {length}.{work}");

        return endpoint.Expect.Length > 0 ? $"{text}\n\nWhat to expect: {endpoint.Expect}" : text;
    }
}
