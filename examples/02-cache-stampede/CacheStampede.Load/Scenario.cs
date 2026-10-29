using Spectre.Console;

namespace CacheStampede.Load;

public sealed record Endpoint(string Id, string Label, string Path);

public sealed record Plan(int Instances, Endpoint Endpoint, int VirtualUsers, int RequestsPerSecond, int DurationSeconds, int WorkMs, bool Reset)
{
    /// <summary>Same ports as <c>scripts/api-cache.ps1</c>.</summary>
    public static readonly string[] AllTargets = ["http://localhost:5011", "http://localhost:5012"];

    public IReadOnlyList<string> Targets => AllTargets.Take(Instances).ToArray();

    /// <summary>Requests in flight at the same time, if every request waits for the database.</summary>
    public int NeededVirtualUsers => (int)Math.Ceiling(RequestsPerSecond * WorkMs / 1000.0);
}

/// <summary>The questions the console asks before a run, and the summary it shows before starting.</summary>
public static class Scenario
{
    public static readonly Endpoint[] Endpoints =
    [
        new("no-cache", "no cache (every request is a database query)", "/api/no-cache"),
        new("classic", "classic cache (GET, then SET)", "/api/classic"),
        new("fill", "FILLGET / FILLSET", "/api/fill"),
        new("fill-wait", "FILLGET WAIT (server-side wait on the shared connection)", "/api/fill-wait"),
        new("get-or-create", "GetOrCreateAsync", "/api/get-or-create"),
    ];

    public static Plan Ask()
    {
        int instances = AnsiConsole.Prompt(new SelectionPrompt<int>()
            .Title("How many API [cyan]instances[/]?")
            .AddChoices(1, 2));

        Endpoint endpoint = AnsiConsole.Prompt(new SelectionPrompt<Endpoint>()
            .Title("Which [cyan]endpoint[/]?")
            .UseConverter(e => e.Label)
            .AddChoices(Endpoints));

        int virtualUsers = Number("Virtual users (max requests in flight)", 60, 1);
        int rate = Number("Requests per second (total, all instances)", 50, 1);
        int duration = Number("Duration in seconds", 30, 1);
        int workMs = Number("Database time per query (ms)", 800, 0);
        bool reset = AnsiConsole.Confirm("Reset the cache and the counters before the run?", true);

        return new Plan(instances, endpoint, virtualUsers, rate, duration, workMs, reset);
    }

    public static void ShowPlan(Plan plan)
    {
        var table = new Table().Title("[bold]Run plan[/]").AddColumn("Setting").AddColumn("Value");
        table.AddRow("Endpoint", plan.Endpoint.Path);
        table.AddRow("Instances", string.Join(", ", plan.Targets));
        table.AddRow("Virtual users", plan.VirtualUsers.ToString());
        table.AddRow("Requests per second", plan.RequestsPerSecond.ToString());
        table.AddRow("Duration", $"{plan.DurationSeconds} s (about {plan.RequestsPerSecond * plan.DurationSeconds} requests)");
        table.AddRow("Database time", $"{plan.WorkMs} ms per query");
        table.AddRow("Reset first", plan.Reset ? "yes" : "no");
        AnsiConsole.Write(table);

        if (plan.VirtualUsers < plan.NeededVirtualUsers)
        {
            AnsiConsole.MarkupLine($"[yellow]Only {plan.VirtualUsers} virtual users for about {plan.NeededVirtualUsers} requests in flight: " +
                "the rest are dropped, the same way k6 counts dropped iterations.[/]");
        }
    }

    private static int Number(string title, int fallback, int min)
    {
        return AnsiConsole.Prompt(new TextPrompt<int>(title)
            .DefaultValue(fallback)
            .Validate(value => value >= min
                ? ValidationResult.Success()
                : ValidationResult.Error($"[red]must be {min} or more[/]")));
    }
}
