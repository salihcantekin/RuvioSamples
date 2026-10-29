using Ruvio.Client;

namespace CacheStampede.Api;

public enum CacheSource
{
    Cache,
    Database,
    Waited,
}

public sealed record DemoSettings(string Instance, TimeSpan CacheTtl, TimeSpan FillTimeout, string KeyPrefix)
{
    public string ProductKey => KeyPrefix + "product:42";

    public static DemoSettings From(IConfiguration configuration)
    {
        var demo = configuration.GetSection("Demo");

        return new DemoSettings(
            demo["Instance"] is { Length: > 0 } name ? name : $"api-{Environment.ProcessId}",
            TimeSpan.FromSeconds(demo.GetValue("CacheSeconds", 10)),
            TimeSpan.FromSeconds(demo.GetValue("FillSeconds", 10)),
            demo["KeyPrefix"] ?? "demo:cache:v1:");
    }
}

public static class DemoSupport
{
    public static IServiceCollection AddDemoDatabase(this IServiceCollection services)
    {
        return services.AddSingleton<SlowDatabase>();
    }

    public static WebApplication UseDemoSupport(this WebApplication app, DemoSettings demo)
    {
        app.Use((context, next) =>
        {
            context.Response.Headers["X-Instance"] = demo.Instance;
            return next(context);
        });

        app.UseExceptionHandler(errors => errors.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new { instance = demo.Instance, error = "cache unavailable" });
        }));

        return app;
    }

    public static WebApplication MapDemoEndpoints(this WebApplication app, DemoSettings demo)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/stats", (SlowDatabase database) =>
        {
            return Results.Json(new
            {
                instance = demo.Instance,
                database = database.Stats(),
                cacheSeconds = demo.CacheTtl.TotalSeconds,
            });
        });

        api.MapPost("/admin/reset", async (IRuvioClient ruvio, SlowDatabase database, CancellationToken cancellationToken) =>
        {
            await ruvio.DeleteAsync(demo.ProductKey, cancellationToken);
            database.Reset();

            return Results.Ok(new { instance = demo.Instance, reset = true });
        });

        app.MapGet("/", () =>
        {
            return Results.Text($"Ruvio cache stampede demo - {demo.Instance}");
        });

        return app;
    }

    /// <summary>How long the fake database takes for one query. The console sends it as <c>?workMs=</c>.</summary>
    public static int LoadMs(int? workMs)
    {
        return Math.Clamp(workMs ?? 800, 0, 5000);
    }

    public static IResult Served(DemoSettings demo, string strategy, CacheSource source, Product product)
    {
        return Results.Json(new
        {
            instance = demo.Instance,
            strategy,
            source = source.ToString().ToLowerInvariant(),
            product,
        });
    }
}
