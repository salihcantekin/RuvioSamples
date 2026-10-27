# Ruvio Load Console

The shared test UI for all examples (`shared/RuvioExamples.Desktop`, WinForms). Virtual clients, fake delays, several API instances.
It is not a performance benchmarking tool like k6: the goal is to answer "what is happening right now?" in a video by **showing every request one by one**.

## On start: a wizard that asks questions

When the console opens it asks, in order, the following. At the end it summarizes the test in one sentence and offers "Start the test":

1. Which API instances? (one or both)
2. Which endpoint? (what to expect is shown under the selection)
3. Traffic shape (only if the example defines burst patterns): steady flow or a pattern
4. How many virtual clients? (+ whether they all share one identity)
5. How many requests per second per client? (for steady flow)
6. How long should it run?
7. Should the server be fake-slow (ms)?

"Skip the questions" opens the detailed panel; "Ask me questions instead" in the panel goes back to the wizard.

## Detailed panel

- **Counters on top**: sent, successful (2xx), rejected (429 in the example), 503/errors, current requests/s, latency p50/p95.
- **Chart**: the last 60 seconds, stacked bars per second.
- **Clients**: one row per virtual client; each request is a block, newest on the right.
- **Served by**: how many requests each API instance answered.
- **Last responses**: the latest responses (status, client, latency, instance, `Retry-After`).
- On the left: ready-made scenarios, endpoint, instance addresses, traffic, client count, rate, fake work, jitter, client start delay, duration.
- Reset button (if the example defines one): calls the example API's reset endpoint on every instance.

## Generic or example-specific?

`LoadKit` and the console know nothing about any Ruvio feature. Everything specific to an example lives in the **`console.json`** in the example's folder:

```
examples/01-rate-limiting/console.json
```

| Field | Meaning |
|-------|---------|
| `title`, `description` | Wizard and panel title |
| `targets` | API addresses (chosen in the wizard) |
| `clientIdHeader`, `instanceHeader`, `workParameter` | The contract between the console and the API (defaults `X-Client-Id`, `X-Instance`, `?workMs=`) |
| `reset` | `path`, button label, hint. If missing, the button is hidden |
| `outcomes` | Names of the results: `success.label`, `rejected.label` + `rejected.statuses` (which status codes count as "rejected") |
| `endpoints` | `id`, `label`, `path`, `method`, `expect` (what to expect, shown on the summary screen) |
| `bursts` | Timed traffic: `steps` (`atSeconds`, `count`) and `cycleSeconds` |
| `presets` | Ready-made scenarios (in the detailed panel) |

If more than one example has a `console.json`, the first wizard step becomes "which example?". Adding a new example means writing a `console.json` without touching the console code;
if a new kind of result is needed (e.g. "lock acquired / had to wait"), only the `outcomes` labels change, and the 2xx/429/503/other classification stays the same.

## Command line (for recording and verification)

```powershell
.\scripts\console.ps1                                   # open with the wizard
.\scripts\console.ps1 --example rate --preset 2         # detailed panel, scenario 2 selected
.\scripts\console.ps1 --preset 4 --endpoint sliding     # endpoint: id, name or path
.\scripts\console.ps1 --preset 3 --autostart --seconds 20 --snapshot out\two-instances.png
.\scripts\console.ps1 --step 4 --snapshot out\step4.png            # screenshot of wizard step 4
.\scripts\console.ps1 --wizard-auto --seconds 10 --snapshot out\run.png   # finish the wizard with all defaults and run it
```

With `--snapshot` the console takes a screenshot when the time is up and closes. Git Bash turns an `/api/...` path into a Windows path; pass the id (`sliding`) instead.
