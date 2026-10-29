# 02 · Cache stampede

**Scenario:** A product page is cached in Ruvio for 10 seconds. Ten busy clients ask for it five times a second each, and the database behind the cache takes 800 ms per query.
When the cache entry expires, every request that arrives during the database query misses at the same moment, so the database gets a burst of identical queries. That is the stampede.

**Ruvio feature:** `FILLGET` and `FILLSET`. `FILLGET` lets exactly one caller rebuild a missing value (it gets a `Fill` token); the others get `Busy` and wait for the value.
The fill marker lives in Ruvio, so it works across every API instance.

Code: [`examples/02-cache-stampede/CacheStampede.Api`](../../examples/02-cache-stampede/CacheStampede.Api). The Ruvio calls are in [`Caching.cs`](../../examples/02-cache-stampede/CacheStampede.Api/Caching.cs). Shared UI: [00-load-console.md](../00-load-console.md).

## Before recording

```powershell
.\scripts\up.ps1              # Ruvio preview + UI (http://127.0.0.1:8080)
.\scripts\api-cache.ps1       # api-1 :5011, api-2 :5012
.\scripts\console.ps1         # Load Console, pick "Cache stampede"
```

Press **Reset cache** in the console before every take. Keep `http://localhost:5011/api/stats` open in a browser tab: the `database.queries` counter is the number to show.

## Video flow

| Part | Console scenario | What is shown | Time |
|------|------------------|---------------|------|
| 1. The cost | `1 · No cache` | Every request is an 800 ms query. `database.queries` grows with the requests | 2 min |
| 2. The cache | `2 · Classic cache` | Hits are instant. At each 10-second expiry the counter jumps by many queries at once: the stampede | 3-4 min |
| 3. The fix | `3 · FILLGET / FILLSET` | One query per expiry. The others get `Busy` and wait a few milliseconds for the value | 4 min |
| 4. Many instances | `4 · Two instances` | Two API instances, still one query per expiry | 2 min |

## Live-coding steps

`Program.cs` only registers Ruvio and maps the endpoints. The lesson is in `Caching.cs`.

### Step 1: no cache

`/api/no-cache` calls `SlowDatabase.LoadProductAsync` on every request. Nothing to type; run scenario 1.

### Step 2: the classic cache

```csharp
string? cached = await ruvio.GetStringAsync(key, cancellationToken);
if (cached is not null)
    return Deserialize(cached);

Product product = await load(cancellationToken);
await ruvio.SetAsync(key, Serialize(product), ttl, cancellationToken: cancellationToken);
return product;
```

Run scenario 2. Point out that the first query after an expiry does not finish before the next ten requests arrive, and all of them miss.

### Step 3: FILLGET / FILLSET

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

- `Fill` comes with a token. `FILLSET` only stores the value if the token is still live, so a slow builder cannot overwrite a newer value.
- `Busy` callers poll with plain `FILLGET`. The optional `WAIT` form exists, but it parks the shared connection, so this example does not use it.
- If the builder dies, the marker lapses after `FillSeconds` and the next caller becomes the builder. There is no lock to release.
- Run scenario 3, then scenario 4.

### Step 4: in production, one line

`RuvioClientHelpers.GetOrCreateAsync` wraps this protocol (polling, timeout, stale window):

```csharp
Product product = await ruvio.GetOrCreateAsync(key, ttl, async ct => await database.LoadProductAsync(instance, workMs, ct), ProductJson.Default.Product);
```

Show it on a slide after step 3. The video uses the raw commands so the protocol is visible.

## Expected behavior (to verify before recording)

These are the expectations written in `console.json`. They are not yet measured on the preview server; run each scenario once and replace the numbers here.

| Scenario | Database queries per expiry (10 s) | Waiting requests |
|----------|------------------------------------|------------------|
| 1 · No cache | one per request (~500 per 10 s) | none |
| 2 · Classic cache | many: every request that arrives during a query | none |
| 3 · FILLGET / FILLSET | 1 | `source: "waited"`, latency up to ~800 ms |
| 4 · Two instances | 1 | same as 3 |

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Constant 500 on `classic` and `fill` | The API cannot reach Ruvio: `.\scripts\up.ps1`, `docker ps`. `Ruvio:Host/Port` is in `appsettings.json` |
| Counter does not move after reset | The product is cached again by the next request; that is expected. Reset, then start the scenario |
| Every request on `fill` is a query | The fill marker is not being honored: check the server version in `infra/docker-compose.yml` |
| Port in use | `.\scripts\down.ps1` |
