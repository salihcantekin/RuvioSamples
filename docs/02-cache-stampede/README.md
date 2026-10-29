# 02 · Cache stampede

**Scenario:** A product is cached in Ruvio for 10 seconds. Clients ask for it at a steady rate, and the database behind the cache takes 800 ms per query.
When the cache entry expires, every request that arrives during the database query misses at the same moment and goes to the database. That is the stampede.

**Ruvio feature:** `FILLGET` and `FILLSET`. `FILLGET` lets one caller rebuild a missing value (it gets a `Fill` token), the others get `Busy` and wait for the value.
The fill marker lives in Ruvio, so it works across every API instance. The same behavior is also available as the one-call helper `GetOrCreateAsync`.

Code: [`examples/02-cache-stampede`](../../examples/02-cache-stampede). The Ruvio calls are in [`Caching.cs`](../../examples/02-cache-stampede/CacheStampede.Api/Caching.cs).

## Run it

Works the same on macOS and Windows (Docker Desktop and the .NET 10 SDK are required).

```bash
docker compose -f infra/docker-compose.yml up -d        # Ruvio on 6379, UI on http://127.0.0.1:8080
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5011 --Demo:Instance=api-1
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5012 --Demo:Instance=api-2   # second terminal, optional
dotnet run --project examples/02-cache-stampede/CacheStampede.Load                                                    # the load console
```

`http://localhost:5011/api/stats` shows the database counter.

## The load console

It asks the same questions a k6 script would take as options:

| Question | k6 equivalent | Default |
|----------|---------------|---------|
| How many API instances? | number of targets | 1 |
| Which endpoint? | the request URL | no cache |
| Virtual users | `maxVUs`: requests in flight at the same time | 60 |
| Requests per second (total) | `rate` with `constant-arrival-rate` | 50 |
| Duration in seconds | `duration` | 30 |
| Database time per query | the fake database delay (`workMs`) | 800 ms |
| Reset first | `/api/admin/reset` before the run | yes |

Rule of thumb: every request holds a virtual user for the database time, so you need about `rate × database time` virtual users (50 × 0.8 = 40). If there are fewer, the surplus requests are **dropped**, just as k6 counts dropped iterations, and the console says so before the run.

The screen shows, live:

- **requests**: sent, served, failed, dropped, in flight, and the **database queries** (the number to watch)
- **latency**: p50, p95, p99, max
- **answered by**: `cache`, `database` or `waited` (the API says which one answered), and the answers per instance
- two bar charts for the last 20 seconds: requests sent per second, and database queries per second. A stampede is a red spike at each expiry.

When the run ends, a result table adds the cache TTL, the expiries that happened (about `duration / TTL`) and the **database queries per expiry**. 1 means no stampede.

## Video flow

Use the same console settings for every take, and press **Reset** in the console (it is on by default) before each one.

| Part | Endpoint in the console | What to show | Time |
|------|-------------------------|--------------|------|
| 1. The cost | no cache | Every request is an 800 ms query. The database query line goes up with every request | 2 min |
| 2. The cache | classic cache | Hits are instant. At each 10-second expiry the red chart spikes: many misses at once | 3-4 min |
| 3. The fix | FILLGET / FILLSET | One query per expiry. The others are `waited`, a few milliseconds more than a hit | 4 min |
| 4. Many instances | FILLGET / FILLSET, 2 instances | Still one query per expiry, whichever instance it lands on | 2 min |

## Live-coding steps

`Program.cs` only registers Ruvio and maps the endpoints. The lesson is in `Caching.cs`. The file has both implementations:

- `FillCache` is **active**: the raw `FILLGET` / `FILLSET` loop.
- `GetOrCreateCache` is **commented out**, at the bottom of `Caching.cs`, and so is `/api/get-or-create` in `Program.cs`.

To teach the other version: delete the block you do not need and type the other one.

### Step 1: no cache

`/api/no-cache` calls `SlowDatabase.LoadProductAsync` on every request. Run the console with **no cache**.

### Step 2: the classic cache

```csharp
string? cached = await ruvio.GetStringAsync(key, cancellationToken);
if (cached is not null)
    return Deserialize(cached);

Product product = await load(cancellationToken);
await ruvio.SetAsync(key, Serialize(product), ttl, cancellationToken: cancellationToken);
return product;
```

Run the console with **classic cache**. The first query after an expiry does not finish before the next requests arrive, and all of them miss.

### Step 3: FILLGET / FILLSET (the active implementation)

```csharp
FillResult reply = await ruvio.FillGetAsync(key, fillTimeout, cancellationToken: cancellationToken);

switch (reply.State)
{
    case FillState.Hit:
        return Deserialize(reply.Text!);

    case FillState.Fill:                       // this caller builds the value
        Product product = await load(cancellationToken);
        await ruvio.FillSetAsync(key, Serialize(product), ttl, reply.Token!.Value, cancellationToken: cancellationToken);
        return product;

    case FillState.Busy:                       // someone else is building it: wait and ask again
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        break;
}
```

Points to explain:

- `Fill` comes with a token. `FILLSET` stores the value only if the token is still live, so a slow builder cannot overwrite a newer value.
- `Busy` callers poll with plain `FILLGET`. The optional `WAIT` form exists, but it parks the shared connection, so the example does not use it.
- If the builder dies, the marker lapses after `FillSeconds` and the next caller becomes the builder. There is no lock to release.

Run the console with **FILLGET / FILLSET**, then with **2 instances**.

### Step 4: GetOrCreateAsync (the other implementation)

The helper wraps the whole protocol above (polling, timeout, stale window) in one call. In `Caching.cs`, uncomment `GetOrCreateCache` and in `Program.cs` uncomment `/api/get-or-create`:

```csharp
Product product = await ruvio.GetOrCreateAsync(key, ttl, async ct => await load(ct), ProductJson.Default.Product, cancellationToken: ct);
```

Then pick **GetOrCreateAsync** in the console. Its numbers should match step 3. The difference to show: `GetOrCreateAsync` returns only the value, so the console can tell `database` from `cache`, but it cannot tell `waited` from `cache`.

## Expected behavior (to verify before recording)

These are the expectations, not measured numbers yet. The console prints the real ones; replace this table after one run per endpoint on the preview server.

| Endpoint | Database queries per expiry (10 s) | Answers |
|----------|------------------------------------|---------|
| no cache | one per request (~500 per 10 s at 50/s) | all `database` |
| classic cache | many: every request that arrives during a query | `database` and `cache` |
| FILLGET / FILLSET | 1 | `database`, `waited`, `cache` |
| GetOrCreateAsync | 1 | `database`, `cache` |

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Cannot reach http://localhost:5011` | The API is not running. Start it with the `dotnet run` command above |
| `HTTP 500: is Ruvio running?` in the console | Ruvio is down: `docker compose -f infra/docker-compose.yml up -d` |
| `404: this endpoint is not on the API` | You picked GetOrCreateAsync, but its endpoint is still commented out in `Program.cs` |
| `dropped` is not zero | Too few virtual users for the rate and database time. Raise the virtual users |
| Database queries stay at zero | Reset the cache and start again; the cache may still hold the product from the last run |
