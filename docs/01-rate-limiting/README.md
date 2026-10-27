# 01 · Rate limiting

**Scenario:** There is an API with a slow, fragile dependency behind it (a database or a payment provider). Eight busy clients flood it with requests,
the dependency slows down, and then it starts rejecting requests. We will add a limit, and the budget must stay consistent even when the API has several instances.

**Ruvio feature:** the `LIMIT` command (an atomic counter on the server) and the `Ruvio.AspNetCore.RateLimiting` middleware.
Because the counter lives in Ruvio, the budget is shared however many instances the API has.

Code: [`examples/01-rate-limiting/RateLimiting.Api`](../../examples/01-rate-limiting/RateLimiting.Api). Shared UI: [00-load-console.md](../00-load-console.md).

## What do you do in the console?

When the console opens it asks questions (which instance, which endpoint, how many clients, rate, duration, fake delay) and summarizes the test in one sentence.
In the video it works well to start with the wizard and then switch to the ready-made scenarios with "Skip the questions". Details: [00-load-console.md](../00-load-console.md).
This example's endpoints, scenarios and labels are in [`console.json`](../../examples/01-rate-limiting/console.json).

## Before recording

```powershell
.\scripts\up.ps1              # Ruvio preview + UI (http://127.0.0.1:8080)
.\scripts\api.ps1             # api-1 :5001, api-2 :5002
.\scripts\console.ps1         # Load Console
```

Press **Reset limits** in the console before every take. Keep the Ruvio UI open in the browser: the keys appear with the `demo:rate:v1:...` prefix (the ids are hashed).

## What is a policy, and how do you add it to Ruvio?

There is **no policy to register** on the Ruvio side. A policy is the **name** you pass to `WithRuvioRateLimit` (or `[RuvioRateLimit]`):

```csharp
.WithRuvioRateLimit("demo-checkout", permits: 3, window: TimeSpan.FromSeconds(10))
```

When a request arrives the middleware sends a single atomic `LIMIT` command to Ruvio with the key `KeyPrefix + hash(policy name + partition key)`.
The counter is created on the first request. The consequences:

- **Same name = shared budget.** If two endpoints use the same policy name they spend one budget (`/api/shop/products` + `/api/shop/cart` = `demo-shop`).
- **Different name = separate budget.** `demo-checkout` is its own counter and is not affected by the use of other endpoints.
- **The partition key** (`PartitionKey`, `X-Client-Id` in the example) splits the budget per user: every client has its own counter for every policy.
- The name, permit count and window live in code; if a policy's settings change, the old counter does not move to the new settings. Use a new name or press **Reset limits**.

### The policies in this example

| Policy | Where | Budget |
|--------|-------|--------|
| `demo-global` | on the `app.MapGroup("/api")` group | 30 / 10 s. Every `/api/*` endpoint **without a policy of its own** (`/api/ping`, `/api/time`) |
| `demo-fixed`, `demo-sliding`, `demo-bucket` | on single endpoints | 10 / 10 s, different algorithms |
| `demo-attribute` | with `[RuvioRateLimit]` | 10 / 10 s, the same rule as an attribute |
| `demo-shop` | on the `/api/shop` group | 10 / 10 s, shared by `products` and `cart` |
| `demo-checkout` | `/api/shop/checkout` | **3** / 10 s, a stricter policy of its own |

**A global limit + a specific policy:** the definition closest to the endpoint wins; `GetMetadata` reads the last (most specific) policy. So `demo-global`, defined on the group,
**does not apply** to an endpoint that has its own policy, it is **replaced**; the two limits are not applied on top of each other. Measured: `/api/fixed` returns 429
after the 10th request (its own policy), `/api/ping` + `/api/time` together return 429 after the 30th request (global), `/api/shop/checkout` returns 429 after the 3rd request,
and `/api/naive` never got a 429 (it is not part of the group; it is mapped with `app.MapGet("/api/naive")`).
If you need to apply a global and a specific limit at the same time, this package does not do that; you would write two separate checks with two different names.

## Video flow

| Part | Console scenario | What is shown | Time |
|------|------------------|---------------|------|
| 1. The problem | `1 · No limit` | 8 clients x 8 requests/s, fake work 400 ms. Latency p95 rises, then 503s. With `/api/stats`, `peakInFlight` exceeds the capacity | 2-3 min |
| 2. The fix | `2 · Fixed window` | `AddRuvioRateLimiting` + `WithRuvioRateLimit`. 10 requests/10 s/client; from the 11th request on, 429 + `Retry-After` | 3-4 min |
| 3. Distributed | `3 · Two instances` | The same client is spread over two instances, the budget is still 10. "Served by" shows both instances; the counter is in Ruvio | 2 min |
| 4. The edge problem (preview) | `4 · Window edge` | Fixed: 20 requests got through in 1 second. Sliding and token bucket: 10. Exact `Retry-After` | 3 min |
| 5. If Ruvio goes down (preview) | `5 · Ruvio goes down` | Stop the container: `Throw` 500, `FailOpen` no limit, `FailClosed` 503, instant 503 with the circuit breaker | 3 min |

## Live-coding steps

`Program.cs` contains only the code of the lesson (the demo helpers are in `DemoSupport.cs` and `FragileBackend.cs`). The parts to delete and retype in the video:

### Step 1: the problem (code is ready)

The `/api/naive` endpoint has no limit. `FragileBackend` has a capacity of 12 parallel requests; above it the latency grows, and at 2x it returns 503.

### Step 2: add the limit

```csharp
// packages: Ruvio.Client.AspNetCore + Ruvio.AspNetCore.RateLimiting
builder.Services.AddRuvioClient(builder.Configuration.GetSection("Ruvio"));

builder.Services.AddRuvioRateLimiting(options =>
{
    options.KeyPrefix = "demo:rate:v1:";
    options.PartitionKey = context => context.Request.Headers["X-Client-Id"].FirstOrDefault() ?? "anonymous";
});

app.UseRouting();
app.UseRuvioRateLimiting();   // after UseRouting

api.MapGet("/fixed", handler)
   .WithRuvioRateLimit("demo-fixed", permits: 10, window: TimeSpan.FromSeconds(10));
```

Points to explain: the partition key (whose budget is it?), the middleware only runs on marked endpoints, a single atomic command, the raw identity is never stored (hashed key),
on a 429 the `Retry-After` is the whole window.

### Step 2b: Microsoft's own limiter (commented out)

When the commented blocks in `Program.cs` (the `using`, `AddRateLimiter`, `app.UseRateLimiter()` and the `/api/ms-fixed` endpoint) are uncommented,
the same rule (10 requests / 10 s, per client) runs with `Microsoft.AspNetCore.RateLimiting`; it was verified to compile.
The message: this limiter counts **in each instance's own memory**. When requests are spread over two instances the total budget becomes 20 (as many times as there are instances),
while with Ruvio it stays 10. Demo: uncomment, start two instances with `.\scripts\api.ps1`, pick the `Microsoft limiter, in-process` endpoint in the console, and run it with both addresses.

### Step 3: the second instance

The code does not change. Two windows with `.\scripts\api.ps1 -Count 2`. Two addresses in the console. Show that the total budget stays 10.

### Step 4: the preview algorithms

```csharp
api.MapGet("/sliding", handler)
   .WithRuvioRateLimit("demo-sliding", 10, TimeSpan.FromSeconds(10), RuvioRateLimitAlgorithm.SlidingWindow);

api.MapGet("/bucket", handler)
   .WithRuvioRateLimit("demo-bucket", 10, TimeSpan.FromSeconds(10), RuvioRateLimitAlgorithm.TokenBucket);
```

Run the same `4 · Window edge` scenario on all three endpoints. Expected (measured): fixed 20 allowed / 0 rejected, sliding 10-11 allowed / 9-10 rejected, bucket 10 allowed / 10 rejected.
With sliding and bucket `Retry-After` is the real wait (e.g. 1 s), with fixed it is 10 s.

### Step 5: if Ruvio goes down (preview)

```csharp
options.Outage.Mode = RuvioOutageMode.FailClosed;          // Throw | FailOpen | FailClosed
options.Outage.CircuitBreaker = new RuvioCircuitBreakerOptions
{
    FailureThreshold = 3,
    BreakDuration = TimeSpan.FromSeconds(10),
};
```

Restart the API per mode: `.\scripts\api.ps1 -Outage FailClosed -CircuitBreaker`. Stop Ruvio with `docker stop ruvio-examples` and bring it back with `docker start ruvio-examples`.
Present it as a decision question: should a payment API be FailClosed and a general read API FailOpen?

## Screenshot / test

```powershell
.\scripts\console.ps1 --preset 4 --endpoint sliding --autostart --seconds 14 --snapshot out\edge-sliding.png
```

## Troubleshooting

| Symptom | Cause / fix |
|---------|-------------|
| Constant 500 | The API cannot connect to Ruvio: `.\scripts\up.ps1`, `docker ps`. `Ruvio:Host/Port` is in `appsettings.json` |
| No 429 at all | Counters left from the previous take may be outside the window; or the limit/duration changed. **Reset limits** |
| The algorithm changed but the behavior is the same | A policy keeps its first algorithm: give it a new policy name or press **Reset limits** |
| Always `ERR` in the console | The API is not running or the address is wrong; `curl http://localhost:5001/api/stats` |
| Port in use | `.\scripts\down.ps1` |
