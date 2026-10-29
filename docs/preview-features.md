# Preview-specific changes

The examples were written and run against these versions (2026-10-27):

| Part | Version |
|------|---------|
| `Ruvio.AspNetCore.RateLimiting` | `0.2.1-preview.2` (stable: `0.1.2`) |
| `Ruvio.Client.AspNetCore` / `Ruvio.Client` | `0.4.1-preview.33` (stable: `0.3.4`) |
| Ruvio server (`salihcantekin/ruvio`) | `0.3.5-preview.33` |
| Ruvio UI (`salihcantekin/ruvio-ui`) | `0.1.10-preview.33` |

## In the rate limiting package (to explain in the video)

1. **Sliding window** (`RuvioRateLimitAlgorithm.SlidingWindow`). Counts the previous window in proportion to how much it still overlaps. Solves the fixed window's
   "double burst" problem at the window edge.
2. **Token bucket** (`RuvioRateLimitAlgorithm.TokenBucket`). `Permits` is the bucket size, `window` is the time an empty bucket needs to refill. Smooth refill.
3. **Exact `Retry-After`**. Sliding window and token bucket use the server's `LIMIT ... WITHRETRY` form, so `Retry-After` is the real wait.
   Fixed window is still conservative: it always returns the whole window.
4. **Outage modes** (`options.Outage.Mode`):
   - `Throw` (default): if Ruvio is unreachable the exception goes to the application (500 in our example).
   - `FailOpen`: the limit is skipped and the request runs. While Ruvio is down you have no limit.
   - `FailClosed`: `503` + (while the circuit is open) `Retry-After`, and the request does not run.
5. **Circuit breaker** (`options.Outage.CircuitBreaker = new RuvioCircuitBreakerOptions { ... }`). After `FailureThreshold` consecutive failures
   it does not contact Ruvio at all for `BreakDuration`. Measured: without the breaker the first two requests waited about 8 s in total on connection attempts;
   once the circuit opened the answers were instant 503s.

Behaviors verified in this example (by stopping the Ruvio container):

| Mode | Ruvio down | Ruvio back |
|------|------------|------------|
| `Throw` | 500 (the application's exception handler) | normal |
| `FailOpen` | 200, no limit | limits work again |
| `FailClosed` + breaker | 503; after 3 failures an instant 503 + `Retry-After: 10` | 200 once the break time is over |

## On the client side

Some of the `Ruvio.Client 0.4.1-preview` changes: `LimitSlidingAsync`, `LimitTokensAsync`, `...WithRetryAsync` and `LimitOutcome`;
`FillGetAsync` / `FillSetAsync` are used in 02 (cache stampede). The typed helpers `GetJsonAsync<T>` / `SetJsonAsync<T>` / `FillGetJsonAsync<T>` and `GetOrCreateAsync` are not used in these examples;
`RuvioClientOptions.CommandTimeout` (default **30 s**, a behavior change); failover tracking (`FollowPrimary`, `Seeds`);
cluster `ASK`/`TRYAGAIN` retries.

## Watch out

- A policy keeps the algorithm it was first used with. If the algorithm changes you need a **new policy name** (in the example: `demo-fixed`, `demo-sliding`, `demo-bucket`).
- Changing the window/permit value while a window is live does not reset the stored state. If you change a setting in the video, press **Reset limits** first.
- `PartitionKey` comes from the `X-Client-Id` header in the example (so a single console can play many users). In a real API it must be a trusted identity (user, API key, tenant).
- When updating versions, change `Directory.Packages.props` and `infra/docker-compose.yml` together; an old preview server may not know the `LIMIT` modes.
