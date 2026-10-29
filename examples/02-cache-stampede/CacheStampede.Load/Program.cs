using CacheStampede.Load;
using Spectre.Console;

AnsiConsole.Write(new FigletText("Cache Stampede").Color(Color.Orange1));
AnsiConsole.MarkupLine("[grey]Constant-rate load for the cache stampede API. Answer the questions, then watch the run.[/]");
AnsiConsole.WriteLine();

Plan plan = Scenario.Ask();
var metrics = new Metrics();
var runner = new LoadRunner(plan, metrics);

if (!await runner.CheckAsync())
    return 1;

if (plan.Reset && await runner.ResetAsync() is { } resetProblem)
    AnsiConsole.MarkupLine($"[yellow]Reset failed ({Markup.Escape(resetProblem)}). Continuing; the counters start from their current values.[/]");

Scenario.ShowPlan(plan);

if (!AnsiConsole.Confirm("Start the run?"))
    return 0;

Task run = runner.RunAsync();

await AnsiConsole.Live(Dashboard.Render(plan, metrics.Take(0), 0, false))
    .StartAsync(async context =>
    {
        while (!run.IsCompleted)
        {
            context.UpdateTarget(Dashboard.Render(plan, metrics.Take(runner.Elapsed), runner.Elapsed, false));
            await Task.Delay(250);
        }

        context.UpdateTarget(Dashboard.Render(plan, metrics.Take(runner.Elapsed), runner.Elapsed, true));
    });

await run;

Summary.Write(plan, metrics.Take(runner.Elapsed), runner);
return 0;
