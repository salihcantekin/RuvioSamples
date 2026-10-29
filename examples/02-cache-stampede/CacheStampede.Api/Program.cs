using CacheStampede.Api;
using Ruvio.Client;
using Ruvio.Client.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var demo = DemoSettings.From(builder.Configuration);

builder.Services.AddDemoDatabase();

#region Ruvio

builder.Services.AddRuvioClient(builder.Configuration.GetSection("Ruvio"));

#endregion

var app = builder.Build();

app.UseDemoSupport(demo);

app.MapGet("/api/no-cache", async (SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);

    return DemoSupport.Served(demo, "no-cache", CacheSource.Database, product);
});

app.MapGet("/api/classic", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    var (product, source) = await ClassicCache.GetAsync(ruvio, demo.ProductKey, demo.CacheTtl,
        token => database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), token), cancellationToken);

    return DemoSupport.Served(demo, "classic", source, product);
});

app.MapGet("/api/fill", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    var (product, source) = await FillCache.GetAsync(ruvio, demo.ProductKey, demo.CacheTtl, demo.FillTimeout,
        token => database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), token), cancellationToken);

    return DemoSupport.Served(demo, "fill", source, product);
});

// ---- GetOrCreateAsync version (commented out: uncomment together with GetOrCreateCache in Caching.cs) ----
//
// app.MapGet("/api/get-or-create", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
// {
//     var (product, source) = await GetOrCreateCache.GetAsync(ruvio, demo.ProductKey, demo.CacheTtl,
//         token => database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), token), cancellationToken);
//
//     return DemoSupport.Served(demo, "get-or-create", source, product);
// });

app.MapDemoEndpoints(demo);

app.Run();
