# Diagnosing a slow request

ARCHITECTURE.md's Phase 19 line names this explicitly as something to be able to walk through in
an interview. Rather than write a hypothetical, this documents the actual investigation that found
and fixed the real latency bug described in LEARNING_GUIDE.md's Phase 13 entry — the same incident,
told from "how would you even start looking" instead of "here's what was wrong."

## The symptom

`POST /api/incidents` (declare) felt slow during manual testing — not broken, just noticeably not
instant. No error, no failed request, no test failure. The kind of thing that's easy to shrug off
("it's fine, it responded") and exactly the kind of thing worth not shrugging off.

## Step 1 — get a number, not a feeling

Before reading any code, the first move was timing it for real:

```powershell
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-RestMethod -Uri "http://localhost:5252/api/incidents" -Method Post -Headers $headers -Body $body
$sw.Stop()
Write-Output "$($sw.Elapsed.TotalSeconds)s"
```

Result: **11.5 seconds.** "Feels slow" and "takes 11.5 seconds" are different claims — the second
one is the one worth acting on, and it's the one a passing test suite would never have surfaced
(nothing in this project's test suite asserts on latency, only correctness — Phase 13's
`LEARNING_GUIDE.md` entry names this explicitly as a mistake worth naming).

## Step 2 — find the request's correlation ID

Phase 18 wired every response with an `X-Correlation-Id` header (`Program.cs`), set to the same
W3C trace ID (`Activity.Current?.TraceId`) that OpenTelemetry's spans use — deliberately the same
ID in both places, not two independently-numbered systems:

```
curl -i http://localhost:5252/api/incidents ...
X-Correlation-Id: 0bd4edfdf293a6ec4d2bc1f10a241050
```

## Step 3 — read every log line for that one request

```
Select-String -Path logs/beacon-api-*.log -Pattern "0bd4edfdf293a6ec4d2bc1f10a241050"
```

This is the actual point of pushing the correlation ID into Serilog's `LogContext` (`Program.cs`)
instead of leaving it to only appear in a trace exporter: grepping a plain log file for one ID
surfaces every event tied to that specific request, in order, with real timestamps — no tracing
backend required to start the investigation.

## Step 4 — the log told a story, once actually read

The declare-incident code path touches two things beyond the database: `RedisRecentAlertsStore`
(alert dedup) and, as of Phase 13, `ItsmBridgeClient` (ticket sync). Filtering the correlated log
lines down to their timestamps showed most of the 11.5 seconds sitting inside a Redis call — not
distributed evenly, not in application logic, in one specific dependency call.

## Step 5 — reproduce it in isolation, confirm the theory

```powershell
for ($i=1; $i -le 4; $i++) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-RestMethod ...
    Write-Output "Call $i : $($sw.Elapsed.TotalSeconds)s"
}
# Call 1 : 6.48s   Call 2 : 2.50s   Call 3 : 0.016s   Call 4 : 0.009s
```

This shaped the actual root cause: `abortConnect=false` on the Redis connection string only stops
the *initial* `ConnectionMultiplexer.Connect()` call from throwing when Redis is unreachable — it
does nothing for individual commands issued afterward against a still-disconnected multiplexer,
which each independently retry and time out. The first couple of requests pay that cost in full;
nothing was ever caching "Redis is down, stop trying."

## Step 6 — the actual fix, and how it was verified (not just written)

A Polly circuit breaker (`RedisRecentAlertsStore.cs`) that opens after repeated failures and fails
open immediately for a cooldown window, rather than re-attempting a doomed connection on every
single request. Verified the same way the bug was found — timing real sequential requests, not
reading the new code and assuming it worked — which is exactly how Phase 15 later caught a **second
regression** of the identical class of bug (`AddServiceDefaults()`'s blanket resilience handler
double-stacking with this same fix) and a **third**, more subtle one (the circuit breaker's
sampling window being shorter than a single failing call's own duration, so it never actually
tripped) — both described in `LEARNING_GUIDE.md`'s Phase 15 entry, both found by re-running this
exact same "time it for real, don't trust that fixing it once means it stays fixed" process.

## The generalizable version

1. Get a real number before touching any code — "slow" isn't actionable, "11.5s" is.
2. Correlation ID first — one identifier that ties every log line (and, with a real OTLP exporter
   configured, every trace span) for one request together.
3. Read the actual log output for that one request, in order — don't guess which dependency is
   slow, let the timestamps show it.
4. Reproduce outside the full request path once a theory exists — isolating "is it Redis" from
   "is it something else in the handler" is what turns a guess into a diagnosis.
5. Verify the fix the same way the bug was found. A fix that was never re-timed is a hope, not a
   fix — and re-timing is exactly what caught this bug's own two regressions later in this project.
