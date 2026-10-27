using Ruvio.AspNetCore.RateLimiting;
using Ruvio.Client;
using Ruvio.Client.AspNetCore;
using RateLimiting.Api;
using System.Threading.RateLimiting;

// using Microsoft.AspNetCore.RateLimiting;
// using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var demo = DemoSettings.From(builder.Configuration);

builder.Services.AddDemoBackend(demo);

#region Ruvio 

builder.Services.AddRuvioClient(builder.Configuration.GetSection("Ruvio"));

builder.Services.AddRuvioRateLimiting(options =>
{
    options.KeyPrefix = demo.KeyPrefix;
    options.PartitionKey = context => context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "anonymous";

    options.Outage.Mode = demo.Outage;

    if (demo.CircuitBreaker)
    {
        options.Outage.CircuitBreaker = new RuvioCircuitBreakerOptions
        {
            FailureThreshold = 3,
            BreakDuration = TimeSpan.FromSeconds(10),
        };
    }
});

#endregion

//builder.Services.AddRateLimiter(options =>
//{
//    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

//    options.AddPolicy("ms-fixed", context => RateLimitPartition.GetFixedWindowLimiter(
//        context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "anonymous",
//        _ => new FixedWindowRateLimiterOptions
//        {
//            PermitLimit = demo.Permits,
//            Window = demo.Window,
//            QueueLimit = 0,
//        }));
//});

var app = builder.Build();

app.UseDemoSupport(demo);

app.UseRouting();

app.UseRuvioRateLimiting();

//app.UseRateLimiter();












app.MapGet("/api/naive", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "none", workMs);
});

var api = app.MapGroup("/api")
    .WithRuvioRateLimit("demo-global", demo.GlobalPermits, demo.Window);

api.MapGet("/ping", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "global-ping", workMs);
});

api.MapGet("/time", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "global-time", workMs);
});

api.MapGet("/fixed", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "fixed", workMs);
})
.WithRuvioRateLimit("demo-fixed", demo.Permits, demo.Window);

api.MapGet("/sliding", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "sliding", workMs);
})
.WithRuvioRateLimit("demo-sliding", demo.Permits, demo.Window, RuvioRateLimitAlgorithm.SlidingWindow);

api.MapGet("/bucket", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "bucket", workMs);
})
.WithRuvioRateLimit("demo-bucket", demo.Permits, demo.Window, RuvioRateLimitAlgorithm.TokenBucket);

api.MapGet("/attribute", [RuvioRateLimit("demo-attribute", 10, 10)] async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "attribute", workMs);
});





var shop = api.MapGroup("/shop")
    .WithRuvioRateLimit("demo-shop", demo.Permits, demo.Window);

shop.MapGet("/products", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "shop-products", workMs);
});

shop.MapGet("/cart", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "shop-cart", workMs);
});

api.MapGet("/shop/checkout", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "shop-checkout", workMs);
})
.WithRuvioRateLimit("demo-checkout", 3, demo.Window);

api.MapGet("/ms-fixed", async (HttpContext http, FragileBackend backend, int? workMs) =>
{
    return await backend.ServeAsync(http, "ms-fixed", workMs);
})
.RequireRateLimiting("ms-fixed");





app.MapDemoEndpoints(demo);

app.Run();
