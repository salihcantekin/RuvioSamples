using Ruvio.Client;

namespace RateLimiting.Api;

public sealed record DemoSettings(
    string Instance,
    int Permits,
    TimeSpan Window,
    int Capacity,
    string KeyPrefix,
    RuvioOutageMode Outage,
    bool CircuitBreaker,
    int GlobalPermits)
{
    public static DemoSettings From(IConfiguration configuration)
    {
        var demo = configuration.GetSection("Demo");

        return new DemoSettings(
            demo["Instance"] is { Length: > 0 } name ? name : $"api-{Environment.ProcessId}",
            demo.GetValue("Permits", 10),
            TimeSpan.FromSeconds(demo.GetValue("WindowSeconds", 10)),
            demo.GetValue("Capacity", 12),
            demo["KeyPrefix"] ?? "demo:rate:v1:",
            Enum.Parse<RuvioOutageMode>(demo["Outage"] ?? nameof(RuvioOutageMode.Throw), ignoreCase: true),
            demo.GetValue("CircuitBreaker", false),
            demo.GetValue("GlobalPermits", 30));
    }
}

public static class DemoSupport
{
    public static IServiceCollection AddDemoBackend(this IServiceCollection services, DemoSettings demo)
    {
        return services.AddSingleton(new FragileBackend(demo.Capacity));
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
            await context.Response.WriteAsJsonAsync(new { instance = demo.Instance, error = "rate limiter storage unavailable" });
        }));

        return app;
    }

    public static WebApplication MapDemoEndpoints(this WebApplication app, DemoSettings demo)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/stats", (FragileBackend backend) =>
        {
            return Results.Json(new
            {
                instance = demo.Instance,
                backend = backend.Stats(),
                limit = new { permits = demo.Permits, windowSeconds = demo.Window.TotalSeconds },
            });
        });

        api.MapPost("/admin/reset", async (IRuvioClient ruvio, FragileBackend backend, CancellationToken cancellationToken) =>
        {
            await ruvio.FlushDatabaseAsync(cancellationToken);
            backend.Reset();

            return Results.Ok(new { instance = demo.Instance, reset = true });
        });

        app.MapGet("/", () =>
        {
            return Results.Text($"Ruvio rate limiting demo - {demo.Instance}");
        });

        return app;
    }

    public static async Task<IResult> ServeAsync(this FragileBackend backend, HttpContext http, string policy, int? workMs)
    {
        bool served = await backend.WorkAsync(Math.Clamp(workMs ?? 100, 0, 5000), http.RequestAborted);

        if (!served)
        {
            http.Response.Headers["X-Overload"] = "true";
            return Results.Json(new { error = "backend overloaded" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Json(new { instance = http.Response.Headers["X-Instance"].ToString(), policy, ok = true });
    }
}
