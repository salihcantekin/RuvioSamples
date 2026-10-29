using Spectre.Console;

namespace CacheStampede.Load;

/// <summary>The numbers that matter for the stampede, printed once the run is over.</summary>
public static class Summary
{
    public static void Write(Plan plan, Snapshot s, LoadRunner runner)
    {
        bool cached = plan.Endpoint.Id != "no-cache";
        double expiries = runner.CacheSeconds > 0 ? plan.DurationSeconds / runner.CacheSeconds : 0;

        var table = new Table().Title("[bold]Result[/]").AddColumn("Metric").AddColumn("Value");
        table.AddRow("Requests sent", s.Sent.ToString());
        table.AddRow("Served (2xx)", s.Served.ToString());
        table.AddRow("Failed", s.Failed.ToString());
        table.AddRow("Dropped (no free virtual user)", s.Dropped.ToString());
        table.AddRow("Latency p50 / p95 / p99", $"{s.P50:0} / {s.P95:0} / {s.P99:0} ms");
        table.AddRow("Database queries", $"[bold red]{runner.DatabaseQueries}[/]");

        if (cached)
        {
            table.AddRow("Cache TTL", $"{runner.CacheSeconds:0} s");
            table.AddRow("Expiries during the run (about)", $"{expiries:0.0}");

            if (expiries >= 1)
                table.AddRow("Database queries per expiry", $"[bold]{runner.DatabaseQueries / expiries:0.0}[/]  [grey](1 means no stampede)[/]");
        }

        AnsiConsole.Write(table);

        if (s.LastError is not null)
            AnsiConsole.MarkupLine($"[red]Last error:[/] {Markup.Escape(s.LastError)}");
    }
}
