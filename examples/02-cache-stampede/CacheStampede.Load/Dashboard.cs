using Spectre.Console;
using Spectre.Console.Rendering;

namespace CacheStampede.Load;

/// <summary>The live screen: counters, latency, where answers came from, and one bar per second.</summary>
public static class Dashboard
{
    public static IRenderable Render(Plan plan, Snapshot s, double elapsed, bool done)
    {
        string state = done ? "[green]done[/]" : "[yellow]running[/]";

        var header = new Panel(new Markup(
                $"[bold]{plan.Endpoint.Path}[/]   instances [cyan]{plan.Instances}[/]   VUs [cyan]{plan.VirtualUsers}[/]   " +
                $"target [cyan]{plan.RequestsPerSecond}/s[/]   database [cyan]{plan.WorkMs} ms[/]   " +
                $"[bold]{Math.Min(elapsed, plan.DurationSeconds):0.0}[/]/{plan.DurationSeconds} s   {state}"))
            .Header("[bold]Cache stampede[/]")
            .Expand();

        var counts = KeyValues(
            ("sent", s.Sent.ToString()),
            ("served (2xx)", $"[green]{s.Served}[/]"),
            ("failed", s.Failed > 0 ? $"[red]{s.Failed}[/]" : "0"),
            ("dropped", s.Dropped > 0 ? $"[yellow]{s.Dropped}[/]" : "0"),
            ("in flight", s.InFlight.ToString()),
            ("database queries", $"[bold red]{s.Queries}[/]"));

        var latency = KeyValues(
            ("p50", $"{s.P50:0} ms"),
            ("p95", $"{s.P95:0} ms"),
            ("p99", $"{s.P99:0} ms"),
            ("max", $"{s.Max:0} ms"));

        var sources = new Rows(
            Bar("cache", s.Sources.GetValueOrDefault("cache"), Color.Green),
            Bar("database", s.Sources.GetValueOrDefault("database"), Color.Red),
            Bar("waited", s.Sources.GetValueOrDefault("waited"), Color.Yellow));

        var instances = KeyValues(s.Instances.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value.ToString())).ToArray());

        var numbers = new Columns(
            new Panel(counts).Header("[bold]requests[/]").Expand(),
            new Panel(latency).Header("[bold]latency[/]").Expand(),
            new Panel(new Rows(sources, new Text(""), instances)).Header("[bold]answered by[/]").Expand());

        var charts = new Columns(
            Series("requests sent per second", s.SentPerSecond, Color.Blue, elapsed),
            Series("database queries per second", s.QueriesPerSecond, Color.Red, elapsed));

        var items = new List<IRenderable> { header, numbers, charts };

        if (s.LastError is not null)
            items.Add(new Markup($"[red]{Markup.Escape(s.LastError)}[/]"));

        return new Rows(items);
    }

    private static Table KeyValues(params (string Key, string Value)[] rows)
    {
        var table = new Table().Border(TableBorder.None).HideHeaders()
            .AddColumn(new TableColumn("key"))
            .AddColumn(new TableColumn("value").RightAligned());

        foreach (var (key, value) in rows)
            table.AddRow($"[grey]{Markup.Escape(key)}[/]", value);

        return table;
    }

    private static Markup Bar(string name, int count, Color color)
    {
        return new Markup($"{name,-9} [{color.ToMarkup()}]{new string('█', Math.Min(count, 30))}[/] {count}");
    }

    private static BarChart Series(string title, int[] values, Color color, double elapsed)
    {
        int now = (int)elapsed;
        var chart = new BarChart().Width(56).Label($"[bold]{title}[/] [grey](last {values.Length} s)[/]");

        for (int i = 0; i < values.Length; i++)
        {
            int second = now - values.Length + 1 + i;
            chart.AddItem(second % 5 == 0 ? $"{second}s" : " ", values[i], color);
        }

        return chart;
    }
}
