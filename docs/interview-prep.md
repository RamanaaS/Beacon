# Interview prep: defending Beacon's design

ARCHITECTURE.md's own learning roadmap names this explicitly as the senior/architect-level
exercise: not "what did you build" but "why this shape and not another one, and what would actually
break first." Every answer below is grounded in this repository's real code and, where one exists,
a real bug this project found and fixed — not a rehearsed generic answer. File paths and phase
numbers are given so any claim here can be checked against the actual source in under a minute.

## "Defend this architecture"

### Why an event log instead of a mutable status column?

[Incident.cs](../src/Beacon.Domain/Incidents/Incident.cs) has no `Status` field. It has a private
`List<IncidentEvent> _events`, and every public-looking property — `CurrentStatus`,
`CurrentSeverity`, `Responders`, `IsEscalated` — is computed by folding over that list on read
(lines 99-119). `Resolve()`, `Acknowledge()`, `Escalate()` don't mutate state; they call `Raise()`,
which just appends.

The concrete problem this solves: an incident's timeline — who joined, when severity changed, what
was tried, in what order — *is* the postmortem. [IncidentReportGenerator.cs](../src/Beacon.Infrastructure/Incidents/IncidentReportGenerator.cs)
and the AI-postmortem endpoint (Phase 8) both consume the raw event stream directly. If the domain
model only stored "current status: Resolved," that history wouldn't exist to report on — you'd need
a second, parallel audit-log table anyway, kept in sync with the "real" row by hand, which is where
that pattern usually rots. Modeling the log as the actual source of truth instead of a derived
side-effect means there's only one place a bug in "what happened" could hide, and it can't drift out
of sync with itself.

It's also the honest answer to concurrent responders: two people acting on the same incident at once
isn't a write conflict to resolve, it's two facts that both happened and both belong in the record.
A status column has to pick a winner; an append-only log just has two rows.

**Where this pattern was deliberately NOT applied**, and why that matters more than the incident
model itself: `Beacon.Admin`'s user/role management (Phase 11) and the public status page's
component list (Phase 12) are both plain mutable rows. Neither has a "what happened when, in what
order, who did it" requirement — a user's role either is or isn't Admin right now, and forcing that
through an event log would be paying real complexity for a problem that doesn't exist there. The
answer to "why an event log" has to include "and why not everywhere," or it's a slogan, not a
decision.

### Why is CQRS/MediatR scoped to incident orchestration only, not the whole app?

MediatR handlers exist for `DeclareIncidentCommand`, `AcknowledgeIncidentCommand`,
`EscalateIncidentCommand`, `IngestAlertCommand`, and a handful of queries — all under
`Beacon.Application/Incidents` and `/Alerts`. Auth (Phase 7), user/role admin (Phase 11), and the
public status endpoint (Phase 12) all call their application-layer classes directly from their
Minimal API handlers, no `ISender.Send()` in sight.

The actual reason: MediatR earns its cost when a command has multiple things that need to happen off
the back of it and those things shouldn't know about each other — `IncidentDeclaredNotification`
(Phase 11) is the clean example, published once from `DeclareIncidentCommandHandler`, consumed by
both `IncidentAlertsHub` (SignalR broadcast to `Beacon.Admin`) and, separately,
`ItsmTicketSyncHandler` (Phase 13, syncs to the Java bridge) — neither handler references the other,
and adding a third consumer tomorrow wouldn't touch either existing one. User registration has none
of that fan-out; routing it through the same pipeline for consistency's sake would be indirection
with no payoff, just an extra file to open to see what a login call actually does.

### Why one service boundary (the Java ITSM bridge) and not a proper microservices split?

`itsm-bridge` (Phase 13, Spring Boot 4) is a separate deployable for a specific, concrete reason:
it's a different language, different build toolchain (`mvnw`, not `dotnet`), and stands in for
something that's realistically a *pre-existing legacy system* in most real incident-response
shops — you don't rewrite a working ticketing system just because you're building a new incident
tool next to it. That's a genuine, structural reason to draw a service boundary: different
ownership, different lifecycle, nothing to gain by merging it into the .NET solution even if the
language difference weren't there.

Nothing else in Beacon has that property. Splitting `Beacon.Api`'s incident orchestration and, say,
report generation into separate services would buy independent scaling and independent deployment —
neither of which this system currently needs — at the cost of network calls where there were
in-process ones, a second thing to keep available, and distributed-transaction reasoning where there
used to be one `DbContext.SaveChangesAsync()`. Microservices are a cost paid up front for a benefit
that has to materialize later; the honest answer here is that benefit doesn't exist yet anywhere
else in this system, so the rest stays one deployable.

### How does this handle a real alert storm?

Layered, and each layer exists because of something that actually broke during this build, not
because a textbook says to have layers:

1. **Dedup at ingestion** — `RedisRecentAlertsStore` (Phase 3) keys on alert fingerprint within a
   5-minute window; a monitoring tool retrying the same firing alert doesn't create a second
   incident.
2. **Fail-open, not fail-closed, on the dedup store itself** — Phase 13 found `abortConnect=false`
   only covers Redis's *initial* connection, not each command once connected; a genuinely
   unreachable Redis was adding 11.5 seconds to every ingest call, found by timing a live request,
   not by reading the StackExchange.Redis docs. Fixed with a Polly circuit breaker
   ([RedisRecentAlertsStore.cs](../src/Beacon.Infrastructure/Alerts/RedisRecentAlertsStore.cs)) that
   opens after repeated failures and skips straight to "no dedup, just ingest" — an alert storm
   during a Redis outage still gets incidents declared, just without deduplication, which is the
   correct trade for an incident tool (a few duplicate incidents beats losing a real one).
3. **Per-IP rate limiting at the edge** (Phase 19,
   [AlertEndpoints.cs](../src/Beacon.Api/Alerts/AlertEndpoints.cs)) — `/api/alerts/ingest` has no
   auth wall (machine-to-machine, no API-key mechanism built yet), so a 60/minute-per-IP fixed
   window caps how much load any single caller — malfunctioning or malicious — can put on the
   pipeline, independent of whether Redis or the database are healthy.
4. **The database write path itself isn't specifically storm-hardened** — this is the honest gap,
   not a hidden one: `IngestAlertCommandHandler` writes one incident per genuinely-new fingerprint
   synchronously. ARCHITECTURE.md's original design named RabbitMQ for exactly this — buffering
   ingestion so bursts don't apply direct backpressure to SQL — and it's real,
   installed-in-`docker-compose.yml`-but-never-wired-into-any-code (see "what breaks first," below,
   for why that's the first thing to actually build if this went to production).

### What breaks first at scale — and this one isn't hypothetical

`EscalationCheckerService` (Phase 5) runs every 30 seconds. Its query
([IncidentRepository.cs:14-18](../src/Beacon.Infrastructure/Incidents/IncidentRepository.cs#L14-L18)):

```csharp
public async Task<IReadOnlyList<Incident>> GetOpenIncidentsAsync(CancellationToken ct = default)
{
    var all = await context.Incidents.Include("_events").ToListAsync(ct);
    return all.Where(i => i.CurrentStatus != IncidentStatus.Resolved).ToList();
}
```

That loads **every incident this system has ever seen, and every event on every one of them,
resolved or not,** into application memory, twice a minute — the `Where` runs in C#, after the
data's already been pulled, because `CurrentStatus` is computed from the event list
([Incident.cs:107-112](../src/Beacon.Domain/Incidents/Incident.cs#L107-L112)) and EF Core can't
translate that fold into a SQL `WHERE` clause. The only index in the whole schema is the
EF-convention FK index on the events table's `IncidentId` (confirmed by grepping
`HasIndex` across every migration — there is exactly one, and it's not on anything status-related).
At the scale this doc's own roadmap names — 10M events/day — this query doesn't degrade gracefully,
it falls over: unbounded growth in both rows scanned and bytes pulled over the wire, on a fixed
30-second clock, forever, even though the checker only ever needs the *open* subset.

This is a genuinely real, present-day gap in Beacon's current code, not a hedge — and it's a good
interview answer specifically because it's checkable: read the two files above and the finding
either holds up or it doesn't. The fix is the same idea already used for
`IncidentDeclaredNotification` (Phase 11): stop pretending one model has to serve both jobs.
A persisted, indexed `Status` column updated alongside each event append (cheap: one more column
write in the same transaction) turns this into `WHERE Status != 'Resolved'` at the SQL level and
the row-scan problem disappears. That's a deliberate, narrow crack in "no field is ever read from a
separate current-state record" ([Incident.cs:8-9](../src/Beacon.Domain/Incidents/Incident.cs#L8-L9))
— a genuine CQRS read-model, exactly the kind ARCHITECTURE.md scoped this pattern for in the first
place, added only once a specific query actually needs it rather than speculatively everywhere.

Second-order answer, for "and after that": `GetOpenIncidentsAsync` and `GetAllAsync` both call
`.Include("_events")`, loading full event history for every matching incident even when the caller
(the escalation checker, in this case) only needs `CurrentSeverity` and the acknowledgment
timestamp to decide if a Sev1 is overdue — not the full note/responder/severity-change history. A
projection query selecting just those fields would cut both the SQL result set and the
deserialization cost further, again only worth doing once the status-column fix above isn't enough
on its own.

## "Tell me about a bug you found and fixed" — a curated bank

Every phase in [LEARNING_GUIDE.md](../LEARNING_GUIDE.md) documents its own bugs in full; these five
are the ones worth having ready verbatim, ordered by how good a story they make, each already in
enough of a STAR shape to say out loud without notes.

1. **The background-service crash (Phase 19) — the best story in the project.** *Situation:* a
   chaos test (forcing the database offline while the API was already running) found that the
   entire API process died, not just one feature. *Task:* find out why one background timer tick
   could kill unrelated things like login and incident declaration. *Action:* traced it to
   `EscalationCheckerService.ExecuteAsync` having zero exception handling — .NET's Generic Host
   treats an unhandled exception from a `BackgroundService` as fatal to the whole host by default.
   Wrapped the loop body in try/catch, log-and-continue. *Result:* re-ran the identical chaos test
   post-fix and watched the process survive a real database outage and self-recover the moment the
   database came back, no restart needed — verified live both before and after, not just reasoned
   about. Good because it shows systems-level knowledge (Generic Host semantics) most candidates
   who've only worked in the request/response path have never had a reason to learn.
2. **The resilience bug written twice in one project (Phases 13 and 15).** Recognizing a pattern in
   the abstract didn't stop the same class of mistake from happening again in different code weeks
   later: Phase 13 fixed a Redis circuit breaker's `SamplingDuration` being shorter than a single
   failing call could take, meaning sequential failures landed in separate windows and the breaker
   never tripped; Phase 15 found the *exact same* latent bug already sitting in a different client
   (`ItsmBridgeClient`) that had been written using the same (buggy) pattern before the first fix
   was even understood as a pattern. Good because the honest framing — "knowing about a bug class
   doesn't automatically stop you from writing it again, only re-verifying does" — reads as
   self-aware rather than defensive.
3. **The middleware-ordering bug only visible in real log output (Phase 18).** Correlation-ID
   middleware registered *after* `UseSerilogRequestLogging()` meant the one log line most likely to
   be searched for first — the request summary line — consistently had an empty correlation ID,
   while every other line in the same request had it correctly. Not a compile error, not a failing
   test — invisible unless someone actually reads the console output line by line. Good for
   demonstrating the "test coverage isn't the same claim as correctness" distinction.
4. **The screenshot that caught what a passing E2E script didn't (Phase 9).** A full Playwright run
   through register→login→declare→acknowledge→resolve passed with zero console errors — and still
   shipped a real UX bug (incident actor rendering as a raw Identity GUID instead of a name) that
   only became visible by actually looking at the screenshots afterward. Good for "how do you know
   your feature actually works" — the answer isn't "the test suite is green."
5. **Three real bugs in one Blazor Server page, all root-caused (Phase 11).** A scaffold that never
   applied its own interactive render mode (silent static-SSR fallback), a `DelegatingHandler`
   picking up a different DI-scoped instance than the rest of the circuit because
   `IHttpClientFactory` builds handlers in its own internal scope, and a lifecycle method
   (`OnInitializedAsync`) that only runs once per circuit — before login had happened — gating a
   feature that needed post-login state. None caught by 62/62 passing backend tests. Good for
   showing framework-specific depth (Blazor Server's circuit/DI model specifically) beyond generic
   ASP.NET Core knowledge.

## If asked "what would you do differently starting over"

Answer honestly from the two gaps this doc already names above — the unindexed status query and
the never-wired RabbitMQ queue named in the original design — rather than a rehearsed "nothing,
it's perfect" or a vague "more tests." Both are real, both are checkable in the actual code, and
both have a concrete, already-reasoned-through fix path. That combination — a real gap, honestly
stated, with a specific next step — is a stronger answer than a project that claims to have no
gaps at all.
