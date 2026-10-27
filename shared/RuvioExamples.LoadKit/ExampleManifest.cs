using System.Text.Json;

namespace RuvioExamples.LoadKit;

/// <summary>
/// What the console needs to know about one example: its API addresses, endpoints, scenarios and how to name the
/// results. Lives next to the example as <c>console.json</c>; nothing here is specific to one Ruvio feature.
/// </summary>
public sealed class ExampleManifest
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Folder name of the example, set by the loader.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> Targets { get; init; } = ["http://localhost:5001"];

    public string ClientIdHeader { get; init; } = "X-Client-Id";

    public string InstanceHeader { get; init; } = "X-Instance";

    public string? WorkParameter { get; init; } = "workMs";

    public ResetDefinition? Reset { get; init; }

    public OutcomeLabels Outcomes { get; init; } = new();

    public IReadOnlyList<EndpointDefinition> Endpoints { get; init; } = [];

    public IReadOnlyList<BurstDefinition> Bursts { get; init; } = [];

    public IReadOnlyList<PresetDefinition> Presets { get; init; } = [];

    public EndpointDefinition? FindEndpoint(string? idOrPath) => idOrPath is null
        ? null
        : Endpoints.FirstOrDefault(e => e.Id.Equals(idOrPath, StringComparison.OrdinalIgnoreCase)
            || e.Path.Equals(idOrPath, StringComparison.OrdinalIgnoreCase)
            || e.Path.EndsWith('/' + idOrPath, StringComparison.OrdinalIgnoreCase));

    public BurstDefinition? FindBurst(string? id) => id is null
        ? null
        : Bursts.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static ExampleManifest Load(string file)
    {
        var manifest = JsonSerializer.Deserialize<ExampleManifest>(File.ReadAllText(file), Json)
            ?? throw new InvalidDataException($"{file} is empty.");

        manifest.Id = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(file))!).Name;

        if (manifest.Endpoints.Count == 0)
            throw new InvalidDataException($"{file} needs at least one endpoint.");

        return manifest;
    }

    /// <summary>Finds <c>examples/*/console.json</c> by walking up from <paramref name="startDirectory"/>.</summary>
    public static IReadOnlyList<ExampleManifest> Discover(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            string examples = Path.Combine(directory.FullName, "examples");

            if (!Directory.Exists(examples))
                continue;

            return [.. Directory.GetDirectories(examples)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(folder => Path.Combine(folder, "console.json"))
                .Where(File.Exists)
                .Select(Load)];
        }

        return [];
    }
}

public sealed class ResetDefinition
{
    public string Path { get; init; } = "/";

    public string Label { get; init; } = "Reset";

    public string Hint { get; init; } = string.Empty;
}

public sealed class OutcomeLabel
{
    public string Label { get; init; } = string.Empty;

    public IReadOnlyList<int> Statuses { get; init; } = [];
}

public sealed class OutcomeLabels
{
    public OutcomeLabel Success { get; init; } = new() { Label = "ok" };

    public OutcomeLabel Rejected { get; init; } = new() { Label = "rejected", Statuses = [429] };
}

public sealed class EndpointDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Path { get; init; } = "/";

    public string Method { get; init; } = "GET";

    /// <summary>One sentence for the viewer: what should happen when this endpoint is hit.</summary>
    public string Expect { get; init; } = string.Empty;
}

public sealed class BurstDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;

    public double CycleSeconds { get; init; }

    public IReadOnlyList<BurstStep> Steps { get; init; } = [];

    public BurstPlan ToPlan() => new() { Steps = Steps, CycleSeconds = CycleSeconds };
}

public sealed class PresetDefinition
{
    public string Name { get; init; } = string.Empty;

    public string Hint { get; init; } = string.Empty;

    public string Endpoint { get; init; } = string.Empty;

    public int Instances { get; init; } = 1;

    public int Clients { get; init; } = 3;

    public double Rate { get; init; } = 3;

    public int WorkMs { get; init; } = 100;

    public int Jitter { get; init; } = 30;

    public int StaggerMs { get; init; }

    public int DurationSeconds { get; init; }

    public string? Burst { get; init; }

    public bool SharedIdentity { get; init; }

    public TestPlan ToPlan(ExampleManifest manifest) => new()
    {
        Targets = [.. manifest.Targets.Take(Math.Max(1, Instances)).Select(address => new Uri(address))],
        EndpointId = manifest.FindEndpoint(Endpoint)?.Id ?? manifest.Endpoints[0].Id,
        Clients = Clients,
        Rate = Rate,
        WorkMs = WorkMs,
        JitterPercent = Jitter,
        StaggerMs = StaggerMs,
        DurationSeconds = DurationSeconds,
        BurstId = Burst,
        SharedIdentity = SharedIdentity,
    };
}
