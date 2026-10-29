# 02 · Cache stampede

**Scenario:** A product is cached in Ruvio for 10 seconds. Clients ask for it at a steady rate, and the database behind the cache takes 800 ms per query.
When the cache entry expires, every request that arrives during the database query misses at the same moment and goes to the database. That is the stampede.

**Ruvio feature:** `FILLGET` and `FILLSET`. `FILLGET` lets one caller rebuild a missing value (it gets a `Fill` token). The others get `BUSY`, or, with `WAIT`, are held until the value is stored.
The fill marker lives in Ruvio, so it works across every API instance.

Code: [`examples/02-cache-stampede`](../../examples/02-cache-stampede). Each endpoint is a minimal API handler in [`Program.cs`](../../examples/02-cache-stampede/CacheStampede.Api/Program.cs). The database is a separate service, `SlowDatabase`.

## Run it

Works the same on macOS and Windows (Docker Desktop and the .NET 10 SDK are required).

```bash
docker compose -f infra/docker-compose.yml up -d        # Ruvio on 6379, UI on http://127.0.0.1:8080
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5011 --Demo:Instance=api-1
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5012 --Demo:Instance=api-2   # second terminal, optional
dotnet run --project examples/02-cache-stampede/CacheStampede.Load                                                    # the load console
```

`http://localhost:5011/api/stats` shows the database counter.

## The endpoints

| Endpoint | Ruvio calls | Behavior |
|----------|-------------|----------|
| `/api/no-cache` | none | Every request is a database query. |
| `/api/classic` | `GET`, `SET` | A miss loads and stores. Requests that arrive during the query all miss: the stampede. |
| `/api/fill` | `FILLGET`, `FILLSET` | One request gets `Fill` and loads. Others get `BUSY` and ask again after 20 ms until the value is stored. No `WAIT`. |
| `/api/fill-wait` | `FILLGET`, `FILLGET ... WAIT`, `FILLSET` | A `BUSY` answer is followed by one `FILLGET` with `WAIT 5 s`: Ruvio holds the request until the value is stored. No polling loop. |
| `/api/get-or-create` | `GetOrCreateAsync` | The client helper. It wraps the protocol of `/api/fill` (polling, timeout, stale window) in one call. |

## Does FILLGET need a delay while BUSY?

Ruvio does not block other callers. Without `WAIT`, the server answers at once. From the server source (`src/engine/fill.rs`, `fill_get`):

- No marker and no value: `FILL <token>` to this caller.
- Marker held by another caller, no `WAIT`: `["BUSY", nil]` immediately.
- Marker held, `WAIT <ms>` given: the connection is parked (`Response::BlockRetry`, `src/runtime/evented/connection.rs`) and the command is retried when the filler writes, when the marker lapses (the waiter becomes the filler) or when the wait ends with `BUSY`.

So the 20 ms in `/api/fill` is not a fake delay. It is the retry interval the caller must provide, because a `BUSY` answer is final. The client helper `GetOrCreateAsync` does the same with a 2 to 25 ms backoff.

## The price of WAIT: the shared connection

`IRuvioClient` in this example is one singleton, and the client multiplexes all requests over one connection. A parked `WAIT` holds that connection. The client README says so directly: a server-side wait parks the whole shared connection, including the builder's own `FILLSET` when it uses the same client. That is why `GetOrCreateAsync` polls instead of waiting.

Expected in `/api/fill-wait` (from the code above, **not measured yet**): when a waiter parks the connection, the filler's `FILLSET` on that connection is queued behind it. The filler stores the value late, the waiters time out after 5 s and get `503`. The console shows this as `failed` answers and latency near 5 s. To fix it, the waiters need their own connection (for example one `RuvioConnection` per waiter), or the code goes back to polling.

## The load console

It asks the questions a k6 script would take as options:

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

Press **Reset** in the console (it is on by default) before each take.

| Part | Endpoint | What to show | Time |
|------|----------|--------------|------|
| 1. The cost | no cache | Every request is an 800 ms query | 2 min |
| 2. The cache | classic | At each expiry the red chart spikes: many misses at once | 3-4 min |
| 3. The fix | fill | One query per expiry. The others are `waited`, a few milliseconds more than a hit | 4 min |
| 4. Many instances | fill, 2 instances | Still one query per expiry | 2 min |
| 5. WAIT | fill-wait | The same single query per expiry, with the shared-connection risk | 3 min |
| 6. The helper | get-or-create | The same result as fill, in one call | 1 min |

## Live-coding steps

### Step 1: no cache

```csharp
Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);
```

### Step 2: the classic cache

```csharp
string? cached = await ruvio.GetStringAsync(demo.ProductKey, cancellationToken);

if (cached is not null)
    return Served(demo, "classic", CacheSource.Cache, Deserialize(cached));

Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);
await ruvio.SetAsync(demo.ProductKey, Serialize(product), demo.CacheTtl, cancellationToken: cancellationToken);
```

The first query after an expiry does not finish before the next requests arrive, and all of them miss.

### Step 3: FILLGET / FILLSET

```csharp
FillResult reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);

while (reply.State == FillState.Busy)
{
    await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
    reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);
}

if (reply.State != FillState.Fill)
    return Served(demo, "fill", CacheSource.Waited, Deserialize(reply.Text!));

Product product = await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), cancellationToken);
await ruvio.FillSetAsync(demo.ProductKey, Serialize(product), demo.CacheTtl, reply.Token!.Value, cancellationToken: cancellationToken);
```

Points to explain:

- `Fill` comes with a token. `FILLSET` stores the value only if the token is still live, so a slow builder cannot overwrite a newer value.
- `BUSY` is final for a call without `WAIT`, so the caller has to ask again. The 20 ms is that retry interval.
- If the builder dies, the marker lapses after `FillSeconds` and the next caller becomes the builder.

### Step 4: FILLGET WAIT

```csharp
FillResult reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, cancellationToken: cancellationToken);

if (reply.State == FillState.Busy)
    reply = await ruvio.FillGetAsync(demo.ProductKey, demo.FillTimeout, TimeSpan.FromSeconds(5), cancellationToken);
```

Run `fill-wait` with the console, then compare with `fill`. Ask the audience why the database still gets one query per expiry, and what happens to the builder's `FILLSET` when the waiters hold the shared connection. Then show the fix options from the section above.

### Step 5: GetOrCreateAsync

```csharp
Product product = await ruvio.GetOrCreateAsync(demo.ProductKey, demo.CacheTtl, async token =>
    await database.LoadProductAsync(demo.Instance, DemoSupport.LoadMs(workMs), token),
    ProductJson.Default.Product, cancellationToken: cancellationToken);
```

The helper does the polling for you. It never uses `WAIT`, for the reason above. It returns only the value, so the console can tell `database` from `cache`, but not `waited` from `cache`.

## Expected behavior (to verify before recording)

These are expectations, not measured numbers. The console prints the real ones; replace this table after one run per endpoint on the preview server.

| Endpoint | Database queries per expiry (10 s) | Answers |
|----------|------------------------------------|---------|
| no cache | one per request (~500 per 10 s at 50/s) | all `database` |
| classic | many: every request that arrives during a query | `database` and `cache` |
| fill | 1 | `database`, `waited`, `cache` |
| fill-wait | 1, if the connection is not blocked | `database`, `waited`, `cache`; `503` and latency near 5 s if the filler's `FILLSET` is blocked |
| get-or-create | 1 | `database`, `cache` |

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| `Cannot reach http://localhost:5011` | The API is not running. Start it with the `dotnet run` command above |
| `HTTP 500: is Ruvio running?` in the console | Ruvio is down: `docker compose -f infra/docker-compose.yml up -d` |
| `503` on fill-wait, latency near 5 s | Expected when the waiters block the shared connection (see above) |
| `dropped` is not zero | Too few virtual users for the rate and database time. Raise the virtual users |
| Database queries stay at zero | Reset the cache and start again; the cache may still hold the product from the last run |
