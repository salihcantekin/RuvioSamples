using System.Text.Json;
using CacheStampede.Api;
using Ruvio.Client;
using Ruvio.Client.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
var demo = DemoSettings.From(builder.Configuration);

builder.Services.AddSingleton<SlowDatabase>();

#region Ruvio

builder.Services.AddRuvioClient(builder.Configuration.GetSection("Ruvio"));

#endregion

var app = builder.Build();

app.UseDemoSupport(demo);

app.MapGet("/api/no-cache", async (SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    Product product = await database.LoadProductAsync(demo.Instance,
                                                      DemoSupport.LoadMs(workMs),
                                                      cancellationToken);

    return DemoSupport.Served(demo, "no-cache", CacheSource.Database, product);
});

app.MapGet("/api/classic", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    string? cached = await ruvio.GetStringAsync(demo.ProductKey, cancellationToken);

    if (cached is not null)
    {
        return DemoSupport.Served(demo,
                                  "classic",
                                  CacheSource.Cache,
                                  JsonSerializer.Deserialize(cached, ProductJson.Default.Product)!);
    }

    Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);
    
    await ruvio.SetAsync(demo.ProductKey,
                         JsonSerializer.Serialize(product, ProductJson.Default.Product),
                         demo.CacheTtl,
                         cancellationToken: cancellationToken);

    return DemoSupport.Served(demo, "classic", CacheSource.Database, product);
});

app.MapGet("/api/fill", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    FillResult reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);
    bool waited = false;

    // Busy: another request is building the value. Ruvio answers at once without WAIT, so we ask again shortly.
    while (reply.State == FillState.Busy)
    {
        waited = true;
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);
    }

    if (reply.State == FillState.Hit)
    {
        CacheSource source = waited ? CacheSource.Waited : CacheSource.Cache;

        return DemoSupport.Served(demo, "fill", source, JsonSerializer.Deserialize(reply.Text!, ProductJson.Default.Product)!);
    }

    // Fill: this request builds the value and stores it with its token.
    Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);

    // False when the marker already lapsed; the value is still returned to this caller.
    await ruvio.FillSetAsync(demo.ProductKey, JsonSerializer.Serialize(product, ProductJson.Default.Product), demo.CacheTtl, reply.Token!.Value, cancellationToken: cancellationToken);

    return DemoSupport.Served(demo, "fill", CacheSource.Database, product);
});

app.MapGet("/api/fill-wait", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    FillResult reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);
    bool waited = false;

    // WAIT: Ruvio holds this request on the connection until the filler stores the value, the marker lapses or 5 s pass.
    // No polling loop. The cost: the parked wait sits on the same shared connection the filler uses for its FILLSET.
    if (reply.State == FillState.Busy)
    {
        waited = true;
        reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, TimeSpan.FromSeconds(5), cancellationToken);

        if (reply.State == FillState.Busy)
            return Results.Json(new { instance = demo.Instance, error = "timed out waiting for the filler" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (reply.State != FillState.Fill)
    {
        CacheSource source = waited ? CacheSource.Waited : CacheSource.Cache;

        return DemoSupport.Served(demo, "fill-wait", source, JsonSerializer.Deserialize(reply.Text!, ProductJson.Default.Product)!);
    }

    Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);
    await ruvio.FillSetAsync(demo.ProductKey, JsonSerializer.Serialize(product, ProductJson.Default.Product), demo.CacheTtl, reply.Token!.Value, cancellationToken: cancellationToken);

    return DemoSupport.Served(demo, "fill-wait", CacheSource.Database, product);
});

app.MapGet("/api/get-or-create", async (IRuvioClient ruvio, SlowDatabase database, int? workMs, CancellationToken cancellationToken) =>
{
    bool loaded = false;

    Product product = await ruvio.GetOrCreateAsync(demo.ProductKey, demo.CacheTtl, 
    async token =>
    {
        loaded = true;
        return await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), token);
    }, ProductJson.Default.Product, cancellationToken: cancellationToken);

    return DemoSupport.Served(demo, "get-or-create", loaded ? CacheSource.Database : CacheSource.Cache, product);
});

app.MapDemoEndpoints(demo);

app.Run();
