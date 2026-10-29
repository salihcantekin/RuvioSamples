using System.Text.Json;
using Ruvio.Client;

namespace CacheStampede.Api;

public enum CacheSource
{
    Cache,
    Database,
    Waited,
}

/// <summary>
/// GET, and on a miss load and SET. Every request that arrives while the value is missing loads it again: that is the stampede.
/// </summary>
public static class ClassicCache
{
    public static async Task<(Product Product, CacheSource Source)> GetAsync(
        IRuvioClient ruvio, string key, TimeSpan ttl, Func<CancellationToken, Task<Product>> load, CancellationToken cancellationToken)
    {
        string? cached = await ruvio.GetStringAsync(key, cancellationToken);

        if (cached is not null)
            return (JsonSerializer.Deserialize(cached, ProductJson.Default.Product)!, CacheSource.Cache);

        Product product = await load(cancellationToken);
        await ruvio.SetAsync(key, JsonSerializer.Serialize(product, ProductJson.Default.Product), ttl, cancellationToken: cancellationToken);

        return (product, CacheSource.Database);
    }
}

/// <summary>
/// FILLGET / FILLSET. One caller gets <c>Fill</c> and builds the value; every other caller gets <c>Busy</c>, waits a few
/// milliseconds and asks again until the value is stored. The marker lives in Ruvio, so this holds across all instances.
/// </summary>
public static class FillCache
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(20);

    public static async Task<(Product Product, CacheSource Source)> GetAsync(
        IRuvioClient ruvio, string key, TimeSpan ttl, TimeSpan fillTimeout, Func<CancellationToken, Task<Product>> load, CancellationToken cancellationToken)
    {
        bool waited = false;

        while (true)
        {
            FillResult reply = await ruvio.FillGetAsync(key, fillTimeout, cancellationToken: cancellationToken);

            switch (reply.State)
            {
                case FillState.Hit:
                    return (JsonSerializer.Deserialize(reply.Text!, ProductJson.Default.Product)!, waited ? CacheSource.Waited : CacheSource.Cache);

                case FillState.Fill:
                    Product product = await load(cancellationToken);

                    // False when the marker already lapsed; the value is still returned to this caller.
                    await ruvio.FillSetAsync(key, JsonSerializer.Serialize(product, ProductJson.Default.Product), ttl, reply.Token!.Value, cancellationToken: cancellationToken);

                    return (product, CacheSource.Database);

                case FillState.Busy:
                    waited = true;
                    await Task.Delay(Poll, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Unexpected fill state {reply.State}. This example does not use stale-while-revalidate.");
            }
        }
    }
}
