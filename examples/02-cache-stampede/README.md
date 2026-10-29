# 02 · Cache stampede

Three parts:

| Part | Folder | What it is |
|------|--------|------------|
| API | [`CacheStampede.Api`](CacheStampede.Api) | Four endpoints for one hot product: `/api/no-cache`, `/api/classic`, `/api/fill`, and `/api/get-or-create` (commented out). A fake database that takes `workMs` per query, and `/api/stats`. |
| Load console | [`CacheStampede.Load`](CacheStampede.Load) | Terminal load generator (Spectre.Console). Asks for instances, endpoint, virtual users, rate, duration and database time, then shows the run live. |
| Old console config | [`console.json`](console.json) | Only for the shared WinForms console (`shared/RuvioExamples.Desktop`). Not needed for the terminal console. |

Video flow, live-coding steps and expected numbers: [docs/02-cache-stampede](../../docs/02-cache-stampede/README.md).

## Run it (macOS or Windows)

```bash
docker compose -f infra/docker-compose.yml up -d                                          # Ruvio on 6379
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5011 --Demo:Instance=api-1
dotnet run --project examples/02-cache-stampede/CacheStampede.Api -- --urls http://localhost:5012 --Demo:Instance=api-2   # second terminal
dotnet run --project examples/02-cache-stampede/CacheStampede.Load                        # the load console
```

The Ruvio calls are in [`Caching.cs`](CacheStampede.Api/Caching.cs):

- `FillCache`: `FILLGET` / `FILLSET`. Active.
- `GetOrCreateCache`: `GetOrCreateAsync`. Commented out, together with its `/api/get-or-create` endpoint in `Program.cs`.
