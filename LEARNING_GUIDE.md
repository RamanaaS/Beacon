# Beacon — Learning Guide

This file grows one section per phase. Each section follows the same shape: what to understand
*before* touching code, what we actually built and why, and what to check you understand *after* —
plus common mistakes and interview questions. Phase 1 is below; later phases get appended, not
split into new files.

---

## Phase 1 — Domain + Application Core (incident event log, alert deduplication)

**Status: done.** 14/14 tests passing. Commits: `docs: add Phase 0 architecture design for Beacon`,
`feat(domain): incident event log and alert deduplication (Phase 1)`.

### Before coding

**Why start with zero infrastructure at all.** The single biggest mistake beginners make on a new
backend project is starting with the database schema and a controller. That forces you to stand up
SQL Server, EF Core, and ASP.NET Core hosting before you've written a single line of the logic that
actually matters — and every test you write is slow (it hits a real or fake database) and fragile
(it breaks when unrelated infrastructure changes). Domain-first means: model the business rules as
plain C# objects with no dependencies on anything, prove them correct with fast in-memory tests
(the whole suite ran in 300ms), *then* wire infrastructure around them later. This is the standard
practiced by every serious DDD/Clean Architecture codebase, and it's why `Beacon.Domain` has zero
NuGet package references — not even a logging package. If it needed one, that would be a sign logic
had leaked in that belongs in Infrastructure instead.

**Entity vs. Value Object vs. Domain Event — with real definitions, not just labels.**

- An **entity** has an identity that persists across changes — two `Incident`s with identical data
  are still different incidents if they have different IDs. `Incident` is an entity (in fact, the
  *aggregate root* — see below).
- A **value object** has no identity — it's defined entirely by its data, and two instances with the
  same data *are* the same value. `Severity` and `Alert` are value objects: an `Alert` with the same
  fingerprint, source, severity, message, and timestamp as another *is* that alert, not merely equal
  to it. This is exactly why they're C# `record`s — records get structural equality for free
  (`record A(int X)` auto-implements `Equals`/`GetHashCode` by value, unlike a `class`, which compares
  by reference unless you override those yourself).
- A **domain event** is a fact that happened, in the past tense, immutable forever. `IncidentDeclared`,
  `IncidentResolved`, etc. are domain events — notice every one of them is named as something that
  *already occurred* (`Declared`, not `Declare`), which is a deliberate DDD naming convention that
  signals "this cannot be un-happened."

**Aggregate roots.** In DDD, an *aggregate* is a cluster of objects that must stay consistent
together, and the *aggregate root* is the only object outside code is allowed to reference directly
— everything else in the cluster is reached only through it. `Incident` is the aggregate root; its
`IncidentEvent`s are part of the aggregate but are never modified or queried independently of the
`Incident` that owns them. This is *why* `Incident.Events` is `IReadOnlyList<IncidentEvent>` and why
every mutation goes through a method on `Incident` (`Acknowledge`, `AddNote`, ...) rather than some
other class reaching in and appending to the list directly. If you could write
`incident.Events.Add(new IncidentResolved(...))` from outside, nothing would stop you from resolving
an incident twice or skipping the empty-summary validation — the aggregate root's whole job is being
the single gatekeeper for its own consistency rules.

**Why an append-only event log instead of a mutable "status" column.** This is Beacon's central
architectural decision (see `ARCHITECTURE.md` Section 4 and Section 9), and Phase 1 is where you
should feel *why* it's right, not just accept it as a design choice: read `IncidentTests.cs`'s
`Rehydrate_reconstructs_current_state_purely_from_history` test. Nothing about `CurrentStatus` or
`CurrentSeverity` is stored — both are *computed* from the event list every time they're read (look
at the `switch` expression in `Incident.CurrentStatus` and the LINQ chain in `CurrentSeverity`). If
two responders act on the same incident at nearly the same moment, each action is just one more
immutable row appended to the log — there is no shared mutable field for them to race on, and no
write conflict to detect or resolve. A `Status` column model would need explicit optimistic
concurrency handling for exactly that scenario; the event log sidesteps the problem by construction.

**Domain services.** `AlertDeduplicator` is a `static` class with one pure function, not a method on
`Alert`. That's deliberate: the "is this a duplicate" decision genuinely needs *two* alerts (and a
time window) to answer, so it doesn't belong to either one individually. When behavior doesn't
naturally belong to a single entity or value object, DDD calls that a **domain service** — and the
correct implementation is a stateless, side-effect-free function, exactly like this one. It doesn't
know about Redis, doesn't know how "recent" alerts get fetched — it just decides, given the alerts
you hand it.

### During coding — what we built, file by file

| File | What it is |
|---|---|
| [`src/Beacon.Domain/Common/Severity.cs`](src/Beacon.Domain/Common/Severity.cs) | Shared value object (enum) used by both incidents and alerts |
| [`src/Beacon.Domain/Incidents/IncidentId.cs`](src/Beacon.Domain/Incidents/IncidentId.cs) | Strongly-typed ID wrapping `Guid` |
| [`src/Beacon.Domain/Incidents/IncidentStatus.cs`](src/Beacon.Domain/Incidents/IncidentStatus.cs) | `Declared` → `Acknowledged` → `Resolved` |
| [`src/Beacon.Domain/Incidents/IncidentEvent.cs`](src/Beacon.Domain/Incidents/IncidentEvent.cs) | Abstract base + six sealed derived records: the entire event vocabulary |
| [`src/Beacon.Domain/Incidents/Incident.cs`](src/Beacon.Domain/Incidents/Incident.cs) | The aggregate root: behavior methods + projections |
| [`src/Beacon.Domain/Alerts/Alert.cs`](src/Beacon.Domain/Alerts/Alert.cs) | Value object for one inbound alert |
| [`src/Beacon.Domain/Alerts/AlertDeduplicator.cs`](src/Beacon.Domain/Alerts/AlertDeduplicator.cs) | Stateless domain service |
| [`tests/Beacon.UnitTests/Incidents/IncidentTests.cs`](tests/Beacon.UnitTests/Incidents/IncidentTests.cs) | 10 tests covering every behavior method + validation rule |
| [`tests/Beacon.UnitTests/Alerts/AlertDeduplicatorTests.cs`](tests/Beacon.UnitTests/Alerts/AlertDeduplicatorTests.cs) | 4 tests covering the dedup decision matrix |

**`IncidentId` as a `readonly record struct`, not a bare `Guid`.** This defends against a real,
common bug class called *primitive obsession*: if both `IncidentId` and, later, `UserId` are plain
`Guid`s, the compiler will happily let you pass a user's ID where an incident ID is expected — it
compiles, and the bug only shows up at runtime. Making it a distinct type turns that into a compile
error. It's a `record struct` (value type) rather than a `record class` (reference type) because an
ID is small, immutable, and semantically "just a value" — no reason to allocate it on the heap.

**Why `IncidentEvent` is `abstract` with a `protected` constructor, and derived events are
`sealed record`s inheriting from it.** Records support inheritance like classes do. Making the base
`abstract` means you can never construct a bare `IncidentEvent` — only one of the six concrete kinds.
Making each derived type `sealed` means nothing can inherit further from, say, `IncidentDeclared` —
there's no reason to, and closing the hierarchy there makes exhaustiveness checking in `switch`
expressions (used in `Incident.CurrentStatus`) more trustworthy. Notice the syntax
`IncidentDeclared(...) : IncidentEvent(OccurredAt)` — a derived record's primary constructor calls
the base record's constructor exactly like ordinary class inheritance; `OccurredAt` is threaded
through to the base so every event type gets a timestamp without repeating that property six times.

**Why `ChangeSeverity` to the same value is a silent no-op, not an error.** Look at the
`ChangeSeverity_to_the_same_value_is_a_no_op` test. This was a genuine design decision, not an
oversight: re-declaring "still Sev1" isn't a fact worth permanently recording in an audit log (it
would just add noise when someone later reads the timeline), but it also isn't a *mistake* the caller
made — unlike an empty title or an empty resolution summary, which genuinely are invalid input and
do throw `ArgumentException`. Knowing which invalid-looking inputs are errors versus which are simply
"nothing to do" is a real judgment call you'll be asked to defend in review.

**Why `AlertDecision` is an enum (`New` / `Duplicate`) instead of `AlertDeduplicator.Evaluate`
returning `bool IsDuplicate`.** This is called avoiding **boolean blindness**: at a call site,
`if (AlertDeduplicator.Evaluate(...) == AlertDecision.Duplicate)` is self-documenting, while
`if (AlertDeduplicator.IsDuplicate(...))` requires you to already know what `true` means without
looking at the method name at the call site — and a `bool` has nowhere to grow if a third outcome
(say, `Escalate`) is needed later, whereas the enum does.

### How to run it

```
cd D:\Learnings\JobSwitch\Beacon
dotnet test
```

### Expected output

```
Passed!  - Failed:     0, Passed:    14, Skipped:     0, Total:    14, Duration: 300 ms - Beacon.UnitTests.dll (net9.0)
```

### After coding — check yourself

You should now be able to answer, without looking back at the code:

- Why does `Incident` have no public setters anywhere?
- What would break if `Events` were `List<IncidentEvent>` (mutable) instead of
  `IReadOnlyList<IncidentEvent>`?
- Why is `AlertDeduplicator` `static` while `Incident` is an ordinary instance class?
- What's the actual difference between `CurrentSeverity` throwing if there are zero
  `IncidentDeclared` events, versus it silently returning a default? (Hint: look at `.Single()` vs.
  `.DefaultIfEmpty()` in the code — `Single()` on the declared-event lookup is a deliberate choice:
  an `Incident` that exists but was never declared is an impossible state we *want* to throw loudly
  on, not paper over.)

### Common mistakes (and why the code above avoids them)

1. **Anemic domain model.** The most common real-world mistake: making `Incident` a bag of public
   `{ get; set; }` properties and putting all the actual logic (validation, the resolved-check) in a
   separate `IncidentService` class. That scatters business rules across the codebase and makes it
   trivial to bypass them by mutating a property directly. Every rule here lives on `Incident` itself.
2. **Mutable shared state in a "pure" domain service.** If `AlertDeduplicator` cached recent alerts
   in a static field instead of receiving them as a parameter, it would stop being a pure function,
   become impossible to unit test deterministically, and silently break under concurrent requests.
3. **Testing through the database from day one.** Writing `IncidentTests` against a real or
   in-memory EF Core `DbContext` before the domain logic itself is proven would make every test
   slower and would conflate "is my business rule correct" with "does my ORM mapping work" — two
   different questions that deserve two different, separately-fast test suites.
4. **Forgetting `DateTimeOffset` and hardcoding `DateTime.Now` inside domain methods.** Every method
   here takes `occurredAt` as a parameter instead of calling `DateTimeOffset.UtcNow` internally —
   that's what makes the tests deterministic (see the fixed `Now` field at the top of each test
   class) and is exactly the seam a later phase will use to inject a real clock via DI.

### Interview questions this phase prepares you for

**Beginner**
- What's the difference between a `record` and a `class` in C#, concretely?
- Why use `Guid` for an ID instead of an auto-incrementing `int`?

**Intermediate**
- What's the difference between an entity and a value object? Give an example of each from a system
  you've worked on.
- What is "primitive obsession" and how does a strongly-typed ID fix it?

**Senior**
- Why model this as an append-only event log instead of a mutable status column? What do you give up
  by doing that (hint: projections require replaying events — is that a real cost at scale, and how
  would you mitigate it)?
- When *would* you reach for a mutable row instead, and why doesn't that apply here?
- Where do you draw the line between "throw an exception" and "silently no-op" for an invalid-looking
  domain method call? Defend the specific choices made in `Incident`.

---

## Phase 2 — Data Layer + First API (EF Core, SQL Server, Minimal APIs)

**Status: done.** 19/19 tests passing (14 unit + 5 integration, the latter against a real
SQL Server LocalDB instance, not a stand-in). Full vertical slice — declare, get, acknowledge,
resolve — verified twice: once by hand with real HTTP calls while the API was running, once as an
automated `WebApplicationFactory`-driven test suite.

### Before coding

**Why "Application" shows up now, not in Phase 1.** Phase 1's plan called this phase "Domain +
Application core," but there was nothing genuine for Application to do yet — no repository seam to
define an interface against, no clock to inject. Application earns its existence the moment
something *outside* the domain (a database, a clock, an HTTP request) needs to be coordinated
without the domain knowing about it. That's exactly what showed up this phase: `IIncidentRepository`
and `IClock` are the two seams Application defines and Infrastructure implements.

**Why exactly one `Repository` interface exists, and why it's justified here.** The architecture
doc explicitly rejected a blanket Repository/Unit-of-Work layer over EF Core — `DbContext` already
*is* a Unit of Work. `IIncidentRepository` is not that anti-pattern: it exists because
`Beacon.Application` must stay ignorant of EF Core entirely (no project reference to it at all —
check `Beacon.Application.csproj`, there's no EF Core package there), which is what makes
`DeclareIncidentHandler` unit-testable with a fake repository instead of a real database. One
interface, one real reason. If a second aggregate root shows up later and *also* needs one, that's
a second justified interface — not a signal to build a generic `IRepository<T>` "just in case."

**TPH vs. TPT vs. JSON column — the decision promised at the end of Phase 1.** All three map a
class hierarchy (`IncidentEvent` and its six derived record types) onto relational tables:

- **TPH (Table-Per-Hierarchy)** — one table for the whole hierarchy, one nullable column per
  field that *any* derived type needs, plus a discriminator column saying which type each row is.
  Simple, keeps the whole event log queryable with ordinary LINQ/SQL, but the table gets wide and
  sparse as the hierarchy grows (see `IncidentEvents` — `Title` is `NULL` on every row except
  `IncidentDeclared` ones).
- **TPT (Table-Per-Type)** — one table per derived type, joined back to a shared base table by a
  foreign key. Normalized, no sparse columns, but every query that wants "the whole timeline" pays
  a multi-way join, and EF Core's TPT query generation is notably slower in practice.
- **One JSON column** — a single narrow table (`Id`, `IncidentId`, `EventType`, `OccurredAt`,
  `Payload` jsonb/nvarchar) where `Payload` holds whatever fields that event type needs. No schema
  migration needed to add a new event type's fields, but you lose the ability to filter/index on a
  specific event field in plain SQL without JSON path functions.

**What Beacon uses, and why:** TPH. Six event types with stable, small shapes is exactly the case
TPH is good at, and keeping `_events` queryable with plain LINQ (`.OfType<SeverityChanged>()` etc.,
the same pattern already used inside `Incident`'s own projections) matters more here than schema
flexibility no one needs yet. The real, live consequence of that choice is visible in
`IncidentEvents` right now — go look at the table. The one thing worth fixing by hand rather than
accepting as TPH's default: EF Core initially split every event type's "who did this" field into
its *own* column (`UserId`, `NoteAdded_UserId`, `ResponderJoined_UserId`, ...) because they're
named differently on each event record. `BeaconDbContext` explicitly points all of them at one
shared `PerformedByUserId` column — see the `HasColumnName(performedByColumn)` calls. Leaving that
unfixed would have been a real, avoidable schema smell, not a necessary cost of TPH.

**Persisting an aggregate root without polluting the domain model.** This is the part of EF Core
most tutorials skip. `Incident` has no public setters and no parameterless constructor — on
purpose, from Phase 1, so nothing outside `Incident` can mutate its state except through a
validated behavior method. EF Core still needs to materialize instances of it from query results.
The mechanism: `BeaconDbContext` maps the `_events` navigation straight to `Incident`'s *private
backing field* (`entity.HasMany<IncidentEvent>("_events")...` +
`entity.Navigation("_events").UsePropertyAccessMode(PropertyAccessMode.Field)`), and materializes
both `Incident` and each `IncidentEvent` subtype through their existing constructors (EF Core's
"constructor binding," matching constructor parameter names to mapped properties). Zero EF Core
attributes, zero public setters, zero compromise to the domain model from Phase 1 — this is the
same pattern Microsoft's own eShopOnContainers reference architecture uses for its `Order`
aggregate, not something improvised for this project.

### During coding — what got built, and what broke first (and why that's normal)

| File | What it is |
|---|---|
| [`src/Beacon.Application/Common/IClock.cs`](src/Beacon.Application/Common/IClock.cs) | The seam Phase 1's `occurredAt` parameters were built for |
| [`src/Beacon.Application/Incidents/IIncidentRepository.cs`](src/Beacon.Application/Incidents/IIncidentRepository.cs) | The one justified Repository interface |
| [`src/Beacon.Application/Incidents/DeclareIncident.cs`](src/Beacon.Application/Incidents/DeclareIncident.cs), `AcknowledgeIncident.cs`, `ResolveIncident.cs`, `GetIncident.cs` | Plain orchestration handlers — deliberately not MediatR commands yet |
| [`src/Beacon.Infrastructure/Incidents/BeaconDbContext.cs`](src/Beacon.Infrastructure/Incidents/BeaconDbContext.cs) | TPH mapping, backing-field navigation, shared `PerformedByUserId` column |
| [`src/Beacon.Infrastructure/Incidents/IncidentRepository.cs`](src/Beacon.Infrastructure/Incidents/IncidentRepository.cs) | `IIncidentRepository`'s real implementation |
| [`src/Beacon.Infrastructure/Migrations/`](src/Beacon.Infrastructure/Migrations/) | Generated + applied against real SQL Server LocalDB |
| [`src/Beacon.Api/Incidents/IncidentEndpoints.cs`](src/Beacon.Api/Incidents/IncidentEndpoints.cs) | 4 Minimal API endpoints: declare, get, acknowledge, resolve |
| [`src/Beacon.Api/Common/DomainExceptionHandler.cs`](src/Beacon.Api/Common/DomainExceptionHandler.cs) | Maps domain exceptions to ProblemDetails + the right HTTP status |
| [`src/Beacon.Api/Common/IncidentIdJsonConverter.cs`](src/Beacon.Api/Common/IncidentIdJsonConverter.cs) | Fixes strongly-typed-ID JSON serialization (see below) |
| [`tests/Beacon.IntegrationTests/`](tests/Beacon.IntegrationTests/) | `WebApplicationFactory`-driven tests against real LocalDB |

Three real things broke while building this, in order — each is worth knowing about because
they're generic EF Core/ASP.NET Core gotchas, not Beacon-specific mistakes:

1. **FK/PK type mismatch.** First migration attempt failed with *"The relationship from
   'IncidentEvent' to 'Incident._events' ... cannot target the primary key ... because it is not
   compatible."* Cause: `Incident.Id` is `IncidentId` (converted to `Guid` for storage), but the
   shadow foreign-key property on `IncidentEvent` was declared as plain `Guid`. EF Core compares
   relationship compatibility using the *CLR* type before conversion, not the storage type — so
   the shadow FK had to be declared `IncidentId` too, with its own matching conversion. Fix:
   `entity.Property<IncidentId>("IncidentId").HasConversion(...)` instead of `Property<Guid>`.
2. **Enum-as-number by default.** `POST /api/incidents` 500'd on the very first real request —
   `System.Text.Json`'s default enum handling reads/writes enums as their underlying numbers, so
   sending `"severity": "Sev1_Critical"` failed to parse. Fix: register `JsonStringEnumConverter`
   globally in `Program.cs`. Worth knowing cold — it's one of the most common "works with Swagger's
   generated numbers, breaks the moment a human sends a readable string" bugs in ASP.NET Core APIs.
3. **Strongly-typed ID round-trip.** After fixing #2, `POST /api/incidents` returned
   `{"id": {"value": "..."}}` instead of a plain GUID string — `IncidentId` serialized as its own
   object shape, which then couldn't be pasted back into a `GET /api/incidents/{id:guid}` URL. This
   is the real, recurring cost of the strongly-typed-ID pattern from Phase 1: every place a
   strongly-typed ID crosses a boundary (JSON here; later, MassTransit/RabbitMQ message contracts)
   needs its own explicit conversion. Fix: a small `JsonConverter<IncidentId>` registered once,
   globally — cheaper than it sounds, and it's a one-time cost per ID type, not per endpoint.

All three were caught by actually running the thing against a real database and real HTTP calls —
not by code review, and not by the unit test suite, because none of them are domain logic bugs.
That's the whole reason integration tests and manual smoke-testing exist as separate disciplines
from unit tests: Phase 1's 14 unit tests were 100% green through all three of these failures,
because the bugs lived entirely in the EF Core mapping and JSON wire format, layers those tests
don't touch on purpose.

**Why the API is Minimal APIs, not controllers (Table C, revisited with a real example in
front of you).** Compare `IncidentEndpoints.cs` to what a controller-based equivalent would need:
no `[ApiController]` class, no `[HttpPost]`/`[FromBody]` attributes repeated per action, no
separate route-attribute string to keep in sync with a method name. For a solo-owned API surface
that's a real, measurable reduction in ceremony — not just a style preference. The trade only
reverses for `Beacon.StatusPage` in a later phase, which needs actual server-rendered HTML views,
where MVC's view-engine integration is the tool built for that job.

**Why exceptions, not a Result-type pattern, for domain errors.** `Incident.Resolve` throwing
`ArgumentException`/`InvalidOperationException` and `DomainExceptionHandler` translating those to
400/409 is a deliberate, simpler choice over a `Result<T>`/`OneOf<T>` return-type pattern some
codebases prefer. With a single exception-handling middleware doing the translation in one place,
every handler method stays focused on the happy path — `DeclareIncidentHandler.HandleAsync` has no
branching for "did this fail" at all. The trade-off: exceptions carry a real performance cost if
thrown in a hot loop, which is why this is fine for "user submitted invalid input" (rare, one-shot)
and would be the wrong call for something like per-item validation inside a bulk-import loop.

### How to run it

```
# One-time per machine: apply the migration to LocalDB
cd D:\Learnings\JobSwitch\Beacon\src\Beacon.Infrastructure
dotnet ef database update

# Run the API (auto-migrates in Development too)
cd ..\Beacon.Api
dotnet run
# Swagger UI: http://localhost:<port>/swagger

# Run everything (unit + integration)
cd D:\Learnings\JobSwitch\Beacon
dotnet test
```

### Expected output

```
Passed!  - Failed:     0, Passed:    14, ... - Beacon.UnitTests.dll (net9.0)
Passed!  - Failed:     0, Passed:     5, ... - Beacon.IntegrationTests.dll (net9.0)
```

### Common mistakes (and why this phase's code avoids them)

1. **Letting EF Core migrations run automatically in production.** `Program.cs` only
   auto-migrates when `IsDevelopment()` — a real deployment applies migrations as an explicit,
   reviewable CI/CD step (Phase 17), never as a side effect of an app instance starting up (two
   instances racing to migrate on deploy is a real, avoidable production incident).
2. **Reusing server-side DTOs inside integration tests.** `IncidentEndpointsTests` declares its
   own tiny `IncidentDto`/`CreatedIncidentDto` records instead of referencing
   `Beacon.Application.Incidents.IncidentResponse` directly. Reusing the exact server type would
   let a real wire-format break (like mistake #3 above) hide behind a compile-time contract that
   isn't actually what crosses the network.
3. **Testing the mapping with the InMemory EF Core provider.** It's tempting because it's fast and
   needs no real database — but it doesn't enforce relational constraints or validate TPH
   configuration the way a real provider does, so it would have hidden all three bugs found this
   phase. LocalDB (real SQL Server underneath) was used instead specifically so "the tests pass"
   means "this actually works," not "this works against a provider that's lenient in ways
   production isn't."

### After coding — check yourself

- Why does the FK shadow property on `IncidentEvent` have to be typed `IncidentId`, not `Guid`,
  even though the column in the database is a plain `uniqueidentifier`?
- What would happen if `Incident.Events` (the public property) weren't `.Ignore()`d in
  `OnModelCreating`? Why does EF Core need telling twice — once to map `_events`, once to ignore
  `Events` — instead of figuring it out from one of them?
- Why is `PerformedByUserId` one shared column instead of six per-type ones? What would you lose,
  concretely, if you left EF Core's default (per-type) naming in place?

### Interview questions this phase prepares you for

**Beginner**
- What's the difference between `dotnet ef migrations add` and `dotnet ef database update`?
- What does `[FromBody]` do in a controller-based API, and why don't Minimal API parameters need it?

**Intermediate**
- Explain TPH vs. TPT vs. owned-entity/JSON-column mapping for an inheritance hierarchy in EF
  Core. When would you pick each?
- Why does this project have exactly one Repository interface instead of one per entity? What's
  the actual test that decides whether a Repository is justified?

**Senior**
- How do you persist an aggregate root through an ORM without adding public setters or a
  parameterless constructor "just for EF"? What are the mechanisms available (constructor binding,
  backing-field access) and what do they cost?
- All three bugs this phase were caught by integration/manual testing, not unit tests. What does
  that tell you about where the *boundary* of a domain unit test should be drawn — and what's the
  actual argument for still keeping the unit test suite separate and fast, instead of just writing
  everything as integration tests?
- Why exceptions + centralized `IExceptionHandler` here instead of a `Result<T>` pattern? Under
  what conditions would you reach for the other one?

---

## Phase 3 — Alert Ingestion + Redis Dedup (partial — blocked on infra, not on code)

**Status: code complete and unit-tested; live end-to-end verification blocked pending a system
restart.** This phase turned out to need Docker (for Redis), and this machine had neither Docker
nor WSL installed. Rather than fake a "verified" result, here's the honest state: what's done,
what's proven, and what's still an open item.

Also: this phase's scope shrank on purpose from what Phase 2 originally planned ("Real-Time
Telemetry — raw WebSockets, Device Simulator, Redis dedup"). Bundling alert ingestion + Redis
dedup with WebSocket telemetry streaming + a whole Device Simulator was overloading one phase with
two genuinely separate concerns — alerts becoming incidents vs. continuous metrics streaming to a
dashboard. Split: this phase is alert ingestion + Redis dedup only; WebSocket telemetry + the
Device Simulator move to the next phase. Re-scoping a phase when its real shape becomes clearer is
normal — the plan in ARCHITECTURE.md was a starting estimate, not a contract.

### What got built

- `IRecentAlertsStore` (Application) — the seam `AlertDeduplicator` (Phase 1's pure domain
  service) needed to work against something real instead of a hand-built list in a unit test.
- `IngestAlertHandler` (Application) — ties together `IRecentAlertsStore`, `IIncidentRepository`,
  `IClock`, and `Incident.Declare`, all from earlier phases. Nothing new domain-wise; pure
  orchestration, which is exactly what Application is for.
- `RedisRecentAlertsStore` (Infrastructure) — real `StackExchange.Redis` implementation: one
  sorted set per fingerprint, score = timestamp, `ZRANGEBYSCORE` for "recent," pruned on write so
  it doesn't grow unbounded.
- `POST /api/alerts/ingest` (Api) — a thin endpoint over `IngestAlertHandler`.
- 4 new unit tests in `Beacon.UnitTests/Alerts/` using hand-written in-memory fakes for
  `IRecentAlertsStore` and `IIncidentRepository` — these prove the *orchestration logic* (dedup
  decision → declare-or-skip) is correct, independent of whether Redis is actually reachable.

### Why Redis needed a real decision, not just code

Redis isn't installable directly on Windows in any officially supported way — it needs either
Docker or WSL, and this machine had neither. Rather than silently pick a fallback, this got a real
choice put to the user: install Docker now (bigger interruption, but it's already a named gap in
the job-search roadmap and every later phase needs it anyway — RabbitMQ, the full docker-compose
stack, eventually Kubernetes), use an in-memory stand-in permanently, or reorder phases to avoid
the dependency for now. Docker Desktop install now was chosen. `winget install Docker.DockerDesktop`
ran successfully, but it needs WSL2 as its backend, which needs a Windows feature enabled that
requires a **restart** to take effect — not something to trigger without asking, since it closes
every open application. That restart is still pending as of this write-up.

### The resilience decision, and why it's not a workaround

`ConnectionMultiplexer.Connect(...)` normally **throws immediately** if it can't reach Redis at
startup — which would have meant the entire API, including every already-working incident
endpoint, refuses to start just because a not-yet-provisioned dependency isn't up. That's a real,
common production failure mode worth knowing by name: one optional dependency taking down
everything else because nobody thought about *degraded* startup. Fix: `abortConnect=false` on the
Redis connection string lets the multiplexer construct successfully and retry in the background;
only code paths that actually *use* it (right now, only `/api/alerts/ingest`) fail until a
connection exists. This was verified for real, not assumed: with Redis genuinely unreachable,
`POST /api/incidents` (Phase 2, unrelated to Redis) still returned `201 Created` against real SQL
Server, while `POST /api/alerts/ingest` returned `500`. One feature degrades; nothing else does.
The full existing test suite (18 unit + 5 integration) also stayed green with Redis down, for the
same reason — nothing in those tests touches the alert-ingestion path.

### Open item — do this once the restart happens

1. Restart Windows so the WSL2 feature Docker's installer enabled actually activates.
2. Open Docker Desktop once (first-run setup, no sign-in required for local use).
3. `docker run -d --name beacon-redis -p 6379:6379 redis:7`
4. Re-run `POST /api/alerts/ingest` — it should now return `200` with a real `New`/`Duplicate`
   decision instead of `500`.
5. At that point this phase's live-Redis integration test (mirroring `IncidentEndpointsTests`'s
   pattern against real LocalDB) can actually get written and verified, instead of asserted from
   unit tests with fakes alone.

### Interview questions this phase prepares you for

- Why does one dependency being down (Redis) not take down the whole API here? What's the actual
  mechanism (`abortConnect`), and what's the failure mode it prevents?
- What's the argument for testing `IngestAlertHandler`'s orchestration logic with hand-written
  in-memory fakes instead of either (a) mocks from a library or (b) always running it against a
  real Redis/SQL Server?
- Why is alert ingestion split from WebSocket telemetry into two separate phases instead of one?

---

## Phase 4 — Real-Time Telemetry (raw WebSockets)

**Status: done.** 24/24 tests passing (18 unit + 6 integration), including 3 back-to-back clean
runs of the integration suite to rule out flakiness in the WebSocket-specific fix below.

Note on naming: earlier phases referred to this as needing a "Device Simulator." That term was
left over from the project's first draft (ChargeMesh, the charging-fleet domain this project moved
away from before Phase 0). Beacon doesn't have physical devices — it has *monitored services*, so
what got built is a metrics simulator, generating plausible CPU/latency/error-rate samples for a
small set of services, not a device simulator.

### Before coding

**Why this runs in-process instead of as a separate simulator process.** The original phase plan
implied a standalone simulator talking to the API over some protocol. Building that would mean
inventing a transport (HTTP? gRPC? a queue?) for a component that only exists to generate fake
data — real complexity serving no real requirement yet. `MetricsSimulatorService` is a
`BackgroundService` living inside `Beacon.Api` itself instead: same demonstrated skill (broadcasting
live samples over a raw WebSocket), zero invented infrastructure. If a second consumer of synthetic
metrics ever shows up — a load test running independently of the API process — that's the concrete
reason to split it out; not before.

**Why raw `System.Net.WebSockets`, not SignalR, for this one.** Table C of ARCHITECTURE.md
already commits to demonstrating both, deliberately, for direct comparison. This is the raw-socket
side — it's the same mechanism used for GRL's production telemetry streaming. `Beacon.Admin`
(Blazor, a later phase) is where SignalR shows up instead, for its live alerts.

**The concurrency rule that broke the first version of this code, and why.** `System.Net.WebSockets.WebSocket`
supports exactly **one concurrent send and one concurrent receive** — not one send, full stop, but
specifically *one at a time*. Two independent code paths writing to the same socket at once
(here: the broadcast timer, and a close handshake) corrupts the connection. This is a real,
easy-to-miss constraint of the raw WebSocket API — the exact kind of thing a higher-level
abstraction like SignalR normally hides from you, and exactly why it's worth having hit it directly
once.

### During coding — what got built, and a bug caught by isolating a failure, not just by re-running

| File | What it is |
|---|---|
| [`src/Beacon.Api/Telemetry/WebSocketConnectionManager.cs`](src/Beacon.Api/Telemetry/WebSocketConnectionManager.cs) | Tracks connections, serializes all writes to each socket through a per-connection lock |
| [`src/Beacon.Api/Telemetry/MetricSample.cs`](src/Beacon.Api/Telemetry/MetricSample.cs) | The broadcast payload shape |
| [`src/Beacon.Api/Telemetry/MetricsSimulatorService.cs`](src/Beacon.Api/Telemetry/MetricsSimulatorService.cs) | `BackgroundService`, ticks every 2s, broadcasts only if someone's listening |
| [`src/Beacon.Api/Telemetry/TelemetryEndpoints.cs`](src/Beacon.Api/Telemetry/TelemetryEndpoints.cs) | `/ws/telemetry` — accept, receive-loop, correct close handling |
| [`tests/Beacon.IntegrationTests/TelemetryWebSocketTests.cs`](tests/Beacon.IntegrationTests/TelemetryWebSocketTests.cs) | Connects via `TestServer`'s real in-process WebSocket client |
| [`tests/Beacon.IntegrationTests/IntegrationTestCollection.cs`](tests/Beacon.IntegrationTests/IntegrationTestCollection.cs) | Fixes a real test-isolation bug — see below |

**Bug #1 — two test classes racing to reset the same database.** Adding `TelemetryWebSocketTests`
alongside the existing `IncidentEndpointsTests` immediately broke *both*, with SQL errors like
`Cannot drop the database... because it does not exist`. Cause: `IClassFixture<BeaconApiFactory>`
gives each test *class* its own factory instance, and xUnit runs different classes in parallel by
default — so two factories both called `EnsureDeletedAsync()` + `MigrateAsync()` against the same
physical `BeaconDb_IntegrationTests` database at the same time. One class's drop landed while the
other's migrate/tests were still using it. This had been invisible with only one integration test
class in the project — it's exactly the kind of bug that only shows up the moment a second
consumer of a shared pattern gets added, which is worth remembering generally. Fix:
`IntegrationTestCollection` (`[CollectionDefinition]` + `ICollectionFixture<BeaconApiFactory>`),
with every integration test class opting into `[Collection(IntegrationTestCollection.Name)]`
instead of its own `IClassFixture`. One shared factory, one reset, classes in that collection run
sequentially against each other by xUnit's own rules.

**Bug #2 — `CloseAsync` after already receiving a Close frame, and no send serialization.** After
fixing the database race, the WebSocket test *specifically* kept failing on its own, with
`ObjectDisposedException` deep inside the close handshake — a different failure from bug #1,
confirming it needed its own diagnosis rather than being the same thing. Two real problems, found
by reading the actual .NET WebSocket API contract rather than guessing:
1. The server's `finally` block called `socket.CloseAsync(...)` unconditionally — but if the loop
   had already received a `Close` message from the client, the peer's close frame had *already*
   arrived. Calling `CloseAsync` (which sends a close frame *and waits for the peer's reply*) at
   that point tries to start a second handshake and waits for a reply that will never come. The
   correct call once you've already received the peer's close is `CloseOutputAsync` — acknowledge
   only, don't re-initiate.
2. Nothing prevented `MetricsSimulatorService`'s broadcast (a send, on a timer) from firing at the
   same moment a client's close handshake was in flight (also effectively a send) on the same
   socket — violating the one-concurrent-send rule above. Fixed by giving every tracked connection
   its own `SemaphoreSlim`, so a broadcast and a close-acknowledgment for the same socket can never
   overlap, no matter how their timing lines up.

Both bugs were caught by isolating a *specific, reliably reproducing* failure and reading its
actual stack trace and API docs, not by trial-and-error re-running until something happened to
pass — worth internalizing as the general debugging move, not just the fix for these two.

### How to run it

```
dotnet test                          # 18 unit + 6 integration, all green

# Or manually: run the API, then in a browser console or a WebSocket client:
#   new WebSocket("ws://localhost:<port>/ws/telemetry")
# and watch JSON samples arrive every ~2 seconds once connected.
```

### Common mistakes (and why this phase's code avoids them)

1. **Assuming `WebSocket.SendAsync` is safe to call from multiple places at once.** It isn't — see
   above. Any design that lets more than one code path independently reach the same `WebSocket`
   instance needs a serialization point, full stop.
2. **Calling `CloseAsync` reflexively in a `finally` block.** The right close call depends on
   *which side initiated the close and whether a close frame was already received* — not always
   the same method.
3. **Giving every test class its own infrastructure fixture "for isolation."** It looks safer, but
   for *shared, named, stateful infrastructure* (a database with a fixed name, in this case) it's
   the opposite — one shared, sequenced fixture is what actually gives you isolation.

### Interview questions this phase prepares you for

- What's the actual concurrency contract of `System.Net.WebSockets.WebSocket`, and what breaks if
  you violate it?
- Why is `CloseOutputAsync` sometimes correct instead of `CloseAsync`? Walk through both sides of
  a WebSocket close handshake.
- `IClassFixture<T>` vs. `ICollectionFixture<T>` in xUnit — what's the actual difference in
  instance lifetime and parallelism, and when does it matter?
- Why does `MetricsSimulatorService` skip generating samples when `ConnectionCount == 0`? What's
  the cost being avoided?

---

## Phase 5 — Incident Orchestration (CQRS/MediatR, scoped)

**Status: done.** 35/35 tests passing (28 unit + 7 integration), plus a real live end-to-end check
of the new background service (below) — not just automated tests this time, because a timer-driven
`BackgroundService` is exactly the kind of "wiring" code this project's bugs have kept coming from,
not domain logic.

Naming note: ARCHITECTURE.md called this module `Beacon.IncidentEngine`, a separate project.
Nothing here got split into a new project — the commands/handlers live in `Beacon.Application`,
where the incident use cases already were. A physical project split is warranted by an independent
deployment or scaling need, and nothing currently has one; see ARCHITECTURE.md Section 9's own
reasoning about not adding structure speculatively, now applied to itself.

### Before coding

**What CQRS/MediatR actually buys here, concretely — not just "the architecture doc said to."**
Before this phase, `EscalationCheckerService` (a background timer, not an HTTP request) would have
needed to either duplicate `EscalateIncidentCommandHandler`'s logic, or reach into
`IIncidentRepository` and `Incident.Escalate` directly, coupling a worker to the same internals the
API uses. With MediatR, both the API endpoint and the background checker do the exact same thing:
`sender.Send(new EscalateIncidentCommand(...))`. One command, one handler, two callers that don't
know about each other. That's the actual justification — not "CQRS is a best practice."

**Why MediatR is pinned to 12.5.0, not the latest version.** Same situation as FluentAssertions in
Phase 1: MediatR's maintainer moved it to a commercial license starting with v13. 12.5.0 is the
last MIT-licensed release. Worth knowing this landscape is shifting — AutoMapper went the same way
— rather than pulling in whatever `dotnet add package` resolves to by default and finding out later.

**Filling in a gap from Phase 1.** ARCHITECTURE.md's own layering diagram named `IncidentEscalated`
as one of the domain events from the start — Phase 1 never actually implemented it (only
`IncidentDeclared` through `IncidentResolved` exist there). This phase adds it properly:
`Incident.Escalate()` (same `EnsureNotResolved` guard as every other mutator) and `EscalationPolicy`,
a pure domain service shaped exactly like `AlertDeduplicator` from Phase 1 — no I/O, just "given
this incident's severity, status, and age, is escalation due right now."

### During coding — three real bugs, one from each layer

| File | What it is |
|---|---|
| [`src/Beacon.Domain/Incidents/EscalationPolicy.cs`](src/Beacon.Domain/Incidents/EscalationPolicy.cs) | Pure function: Sev1 due at 5 min, Sev2 at 15 min, Sev3/4 never auto-escalate |
| [`src/Beacon.Application/Incidents/EscalateIncident.cs`](src/Beacon.Application/Incidents/EscalateIncident.cs) | New command — issued identically by the API and the checker |
| [`src/Beacon.Api/Incidents/EscalationCheckerService.cs`](src/Beacon.Api/Incidents/EscalationCheckerService.cs) | `BackgroundService`, 30s timer, fresh DI scope per tick |
| All of `Beacon.Application/Incidents/*.cs`, `Alerts/IngestAlert.cs` | Refactored from plain `HandleAsync` classes to `IRequest`/`IRequestHandler` |

1. **A genuine EF Core TPH gotcha — forgetting to register a new derived type.** Adding
   `IncidentEscalated` to `Incident.cs` and writing it via `EscalateIncidentCommandHandler` compiled
   and ran fine... until the very next `GET` on that incident threw `InvalidOperationException:
   Unable to materialize entity instance of type 'IncidentEvent'. No discriminators matched the
   discriminator value 'IncidentEvent'`. The domain model and the command handler were correct —
   `BeaconDbContext`'s `HasDiscriminator<string>("EventType")` chain simply never got
   `.HasValue<IncidentEscalated>("Escalated")` added to it. EF let the *write* succeed (silently
   falling back to the base type's name as the discriminator value instead of erroring), and the
   failure only surfaced on *read*, one layer and one HTTP request away from the actual mistake.
   The general lesson: when TPH is in play, adding a derived type to the C# hierarchy and adding it
   to the EF discriminator mapping are two separate steps, and only one of them is enforced by the
   compiler.
2. **A real xUnit test-isolation bug, in the tests I had to fix to even see bug #1.** Not new this
   phase, but worth restating: the `IntegrationTestCollection` fix from Phase 4 is exactly what let
   this new failure surface as *one specific, deterministic, reproducible test failure* instead of
   as flaky noise across three test classes.
3. **`Task<TResponse> Handle(...)` takes a non-optional `CancellationToken`.** Converting
   `IngestAlertHandlerTests` to call the new `IngestAlertCommandHandler.Handle(...)` directly (MediatR
   handlers are still plain classes — nothing stops calling them without going through `ISender`)
   needed an explicit `CancellationToken.None` at every call site; the old hand-rolled
   `HandleAsync(request, CancellationToken ct = default)` signature had a default that MediatR's
   interface contract doesn't offer. Small, but a real compile-time signal of the same shape as the
   others: an interface's exact contract matters, and skimming past it costs you a rebuild-and-fix
   cycle every time.

### How it got verified live, not just by the test suite

`EscalationCheckerService` is the first piece of code this project has that only *does* anything on
a timer, unprompted by a request — none of the existing automated tests exercise "does the
background service actually find and act on real data sitting in the real database." So it got
checked the same way the Phase 2 REST endpoints did originally: for real, against the running app.
A Sev1 incident with `OccurredAt` backdated 6 minutes was inserted directly into the dev database
via `sqlcmd`, the API was started, and its own log output was read 35 seconds later:

```
warn: Beacon.Api.Incidents.EscalationCheckerService[0]
      Incident e669d8c0-... (Sev1_Critical) unacknowledged past its escalation threshold — escalating
```

— confirmed independently via `GET /api/incidents/{id}` showing `"isEscalated": true`. As a bonus,
the same run also escalated a *genuinely* 5-hour-old incident left over from Phase 2's original
manual smoke-testing, still sitting unresolved in the dev database from earlier in this project —
useful confirmation that `GetOpenIncidentsAsync` really does scan everything open, not just the one
row planted for the test.

### Common mistakes (and why this phase's code avoids them)

1. **Trusting that a passing test suite means new "wiring" code (a background service, a DI
   registration) actually works.** It means the *logic* it calls into is correct. Nothing in the 35
   automated tests would have caught a `BackgroundService` that was registered but never actually
   ticked, or ticked but resolved the wrong scope. The live check above is what actually verifies
   the wiring, not the unit tests underneath it.
2. **Assuming EF Core will error loudly on an unmapped derived type.** It doesn't, not at write
   time. The failure mode is silent-then-delayed, which is worse than an immediate crash — write
   down every derived-type addition to a TPH hierarchy as a two-step change (the CLR type, *and*
   the discriminator registration) rather than trusting the compiler to remind you of the second one.
3. **Pinning a package version without checking why.** MediatR 12.5.0 isn't "the version that
   happened to install" — it's a deliberate choice with a documented reason, the same as
   FluentAssertions' pin in Phase 1.

### Interview questions this phase prepares you for

- What does routing a background worker's actions through the same command bus as the API actually
  buy you, concretely — not "it's cleaner," but what breaks without it?
- Walk through what happens in EF Core when you save an entity of a derived type that was never
  given an explicit TPH discriminator value. Why does the failure show up on read, not write?
- Why is `EscalationCheckerService` given its own `IServiceScopeFactory`-created scope per tick
  instead of just injecting `IIncidentRepository` directly into the (singleton) background service?
- `IRequest` vs. `IRequest<TResponse>` in MediatR — what's actually different about the handler
  interface each one requires you to implement?

---

## Phase 6 — Reporting (EPPlus/PDFsharp)

**Status: done.** 43/43 tests passing (33 unit + 10 integration), plus a real live check: declared,
acknowledged, and resolved an incident against the running app and real SQL Server, downloaded both
generated files to disk, and verified their actual file signatures (`PK` for the xlsx's underlying
ZIP container, `%PDF-` for the PDF) — not just that bytes came back.

### Before coding

**What the report actually is.** Not a generic "export to Excel" — a postmortem-shaped document:
a Summary sheet/section (title, severity, status, declared time, escalation flag, responders) plus
a full Timeline (every `IncidentEvent`, in order, human-readable). This is the direct continuation
of the resume bullet ("Automated certification-ready Excel and PDF report generation... directly
from measurement data") — same two libraries, applied to this project's own domain instead of Qi
compliance measurements.

**Why a shared `IncidentEventFormatter` instead of formatting logic duplicated in the Excel and PDF
generators.** Both reports need to render the same seven event types as readable text. Writing that
switch-expression once in `Beacon.Application` (pure, no I/O, unit-tested on its own) and having
both `IncidentReportGenerator` methods call it is the direct payoff of catching duplication before
it happens, not after two copies have quietly drifted apart.

**Two licenses checked before writing a line of report code, not after.** Same discipline as
MediatR (Phase 5) and FluentAssertions (Phase 1) — verify a package's current licensing model
before depending on it, because these change over time and "it's on NuGet" isn't the same as "it's
free to use here":
- **EPPlus** has been dual-licensed since v5: free under the Polyform Noncommercial license (this
  project's situation), commercial license required otherwise. EPPlus 8 changed *how* you declare
  that from a `LicenseContext` enum to `ExcelPackage.License.SetNonCommercialPersonal(...)` — get
  this wrong and every `ExcelPackage` construction throws at runtime, not a warning.
- **PDFsharp** is plain MIT, no license declaration needed — but see the font issue below, which
  is a different (and, this session, more consequential) kind of "the library needs one more thing
  before it'll actually run."

### During coding — two real runtime failures, neither caught by a code review

Both compiled cleanly on the first try. Both threw the instant a report was actually generated —
worth sitting with, because it's the same lesson as Phase 2's JSON-serialization bugs: a type
checker proves the code is *shaped* correctly, not that it *runs* correctly the first time a real
code path executes.

1. **EPPlus's license check is enforced at the first `ExcelPackage` construction, not at
   compile time.** Already handled correctly here (`ExcelPackage.License.SetNonCommercialPersonal`
   in the static constructor, checked against the real 8.6.3 API before writing the report methods
   themselves) — flagging it because getting this *one line* wrong is the single most common EPPlus
   support question online, and it fails at first use, in production, not in a code review.
2. **PDFsharp 6.x doesn't resolve OS fonts automatically.** The very first PDF generation threw
   `System.InvalidOperationException: No appropriate font found for family name 'Arial'. Implement
   IFontResolver...`. Older, GDI+-backed PDFsharp versions could just ask Windows for an installed
   font; PDFsharp 6's cross-platform core dropped that entirely — every consumer has to supply an
   `IFontResolver` that returns real font file bytes for a requested face name. Fixed with
   [`WindowsFontResolver.cs`](src/Beacon.Infrastructure/Incidents/WindowsFontResolver.cs), which
   reads `arial.ttf`/`arialbd.ttf` straight from `C:\Windows\Fonts`. This is flagged in that file's
   own comment as a genuine, not incidental, constraint: it works *because this runs on Windows
   today*. A Linux container (Phase 16+) won't have `C:\Windows\Fonts` — that phase will need to
   either bundle real font files into the image or resolve to whatever fonts the container image
   ships, and that's a real decision to make then, not a detail already handled.

### How the tests actually verify the files, not just that bytes came back

`IncidentReportTests` re-opens the *actual generated bytes* with the real libraries that would read
them in production — `ExcelPackage` for the xlsx, `PdfReader.Open` for the PDF — and asserts on
real structure: `Summary` sheet's title cell matches what was declared, `Timeline` sheet's second
and third rows read "Declared" then "Acknowledged" in order, the PDF's page count is at least 1.
A test that only checked `response.Content.Length > 0` would have passed even against a completely
empty or corrupt file — round-tripping through the real reading library is what makes this a
genuine test instead of a shape check.

### Common mistakes (and why this phase's code avoids them)

1. **Assuming "it compiled" or "the response came back 200" means the file is valid.** Neither
   would have caught a truncated write or a corrupted stream — only actually re-opening the bytes
   with a real parser does.
2. **Trusting a library's happy-path example without checking its current license terms.** Both
   EPPlus and MediatR this project depends on have non-MIT default licensing as of their latest
   major versions; the fix each time was cheap (pin an older version, or call the right API), but
   only because it got checked *before* depending on it, not discovered from a legal notice later.
3. **Writing font/rendering code without ever generating a real file and opening it.** The
   `IFontResolver` requirement is not something a code review would catch — it's a runtime-only
   failure that only shows up the moment `XFont` construction actually executes.

### Interview questions this phase prepares you for

- Walk through EPPlus's licensing model and what `SetNonCommercialPersonal` actually declares.
- Why does PDFsharp need an explicit `IFontResolver` in this project, and what would break if this
  got deployed to a Linux container unchanged?
- What does round-tripping a generated file through its own reading library prove that checking
  `response.Content.Length` or the HTTP status code doesn't?
- Where should `IIncidentReportGenerator`'s implementation change when Blob Storage (Phase 18)
  becomes the real place these files live — what's the actual boundary between "generate the
  bytes" and "persist the bytes," and why does that boundary matter?

---

## Phase 7 — Auth (JWT + ASP.NET Core Identity)

**Status: done.** 49/49 tests passing (33 unit + 16 integration, confirmed non-flaky over 3 runs),
plus a full live walkthrough against the running app: unauthenticated declare → 401, register →
login → real JWT, declare with that token → the incident's recorded actor traced back to the
token's own claim (not anything the client sent), Viewer-role token → declare → 403.

### Before coding

**The actual point of this phase wasn't "add a login screen" — it was closing a real spoofing
gap.** Every incident-mutating endpoint through Phase 6 trusted a `userId`/`declaredByUserId`
field the *client* put in the JSON body. Nothing stopped a request claiming to be anyone. That's
not a hypothetical: it's the textbook definition of a broken authentication/authorization
vulnerability (OWASP's Broken Access Control has sat at #1 for a reason). Auth here means every
mutating endpoint now derives "who did this" from a cryptographically signed JWT claim instead of
trusting user input — see the "Security fix" section below for exactly what changed and why it's
the real deliverable of this phase, not the login endpoints themselves.

**Why auth doesn't go through MediatR.** ARCHITECTURE.md Section 9 scoped CQRS to incident
orchestration specifically — Service Catalog and User Admin were named as the deliberate
non-examples. Auth is a second, concrete instance of that same reasoning: `/api/auth/register` and
`/api/auth/login` call `UserManager`/`SignInManager` directly, no command/query wrapper. Forcing
every technically-write-shaped operation through the same CQRS machinery "for consistency" is
exactly the over-application the architecture doc warned against from the start.

**`AddIdentityCore`, not `AddIdentity`.** `AddIdentity` pulls in cookie-based authentication and
external-login plumbing built for a server-rendered login page — dead weight for a pure JSON API
with no cookies and no session state. `AddIdentityCore` + `.AddSignInManager()` gives exactly what
this needs: password hashing, lockout tracking, and role management, nothing built for a UI that
doesn't exist here.

**Real brute-force protection, not just a password check.** Login uses
`SignInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true)`, not a bare
`UserManager.CheckPasswordAsync`. Five failed attempts locks the account for 15 minutes
(`Lockout.MaxFailedAccessAttempts`/`DefaultLockoutTimeSpan` in `AddInfrastructure`) — a real,
built-in Identity feature that a bare password comparison skips entirely.

### The security fix — what actually changed across every incident endpoint

| Before | After |
|---|---|
| `DeclareIncidentCommand` bound directly from the request JSON, `DeclaredByUserId` supplied by the client | A separate `DeclareIncidentRequest` (no user-id field) bound from JSON; the endpoint builds `DeclareIncidentCommand` itself using `user.GetUserId()` |
| `AcknowledgeIncidentCommand`/`ResolveIncidentCommand`/`EscalateIncidentCommand` all took a client-supplied user id in the body | Same pattern — the body only carries what the *client* actually knows (a summary, an escalation target); the actor comes from the token |
| No endpoint required authentication | `.RequireAuthorization()` on every incident endpoint; mutating ones additionally require the `IncidentResponder` policy (`Responder` or `Admin` role) |

`ClaimsPrincipalExtensions.GetUserId()` reads the JWT's `sub` claim — the same claim
`JwtTokenGenerator` writes when a token is issued, matched up explicitly (see the
`MapInboundClaims = false` note below for why that match isn't automatic by default).

**Deliberately not touched:** `/api/alerts/ingest` stays unauthenticated. Its caller is a
monitoring tool (the real analog: Prometheus Alertmanager), not a human with a login flow —
machine-to-machine ingestion needs an API-key or mTLS-based scheme, a genuinely different problem
from user JWT auth, not something to bolt on incorrectly just to make every endpoint uniformly
"protected." Also not touched: the `/ws/telemetry` WebSocket — browsers can't attach an
`Authorization` header to a native WebSocket handshake, so real WebSocket auth needs the token
passed as a query parameter and read in a custom `OnMessageReceived` handler. Left open, flagged
explicitly, until a real client (the React dashboard, a later phase) actually needs it — right now
telemetry is synthetic CPU/latency numbers, nothing sensitive enough to justify that complexity yet.

### During coding — two real, non-obvious gotchas

1. **`MapInboundClaims = false` — or your "sub" claim silently isn't "sub" anymore.**
   `JwtSecurityTokenHandler` has default behavior (dating back to WCF-era .NET) that silently
   remaps short JWT claim names to long `ClaimTypes.*` URIs when validating an incoming token — so
   without this flag, a token written with a `sub` claim would come back out the other side under
   `ClaimTypes.NameIdentifier` instead, and `GetUserId()`'s `FindFirstValue(sub)` would quietly
   return null. Set once, in `AddJwtBearer`'s options, and now write-side and read-side claim
   names actually match. (A decoded token's *role* claim still shows up as the long
   `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` URI, visible if you paste a
   token into jwt.io — that one's from using `ClaimTypes.Role` deliberately in
   `JwtTokenGenerator`, matching what ASP.NET Core's role-based authorization checks by default.
   Different mechanism, same "claim names aren't always what you typed" lesson.)
2. **`Microsoft.OpenApi` v2's breaking rewrite.** Adding a Swagger "Authorize" button
   (`AddSecurityDefinition`/`AddSecurityRequirement`) failed to compile —
   `Microsoft.OpenApi.Models` no longer exists in the v2.x line Swashbuckle 10 now pulls in
   transitively. Deliberately not chased: it's a convenience for manually testing in the browser,
   not something the actual auth enforcement depends on. Every endpoint's JWT validation and role
   policy works identically with or without it — verified via the live curl-equivalent walkthrough,
   not the missing button.

### Common mistakes (and why this phase's code avoids them)

1. **Treating "add a login endpoint" as the finish line.** The login endpoint existing means
   nothing if every other endpoint still trusts a client-supplied identity field. The actual fix
   is removing that field everywhere it existed and proving (with a test, not an assumption) that
   the server-derived identity is what actually gets recorded.
2. **`AddIdentity` by reflex, without asking whether the cookie/external-login half of it applies.**
   A pure JSON API has no session cookie to issue — pulling in the tooling for one anyway is
   exactly the kind of unnecessary-by-default choice this project has avoided everywhere else.
3. **A bare password-hash comparison instead of `CheckPasswordSignInAsync`.** They look almost
   identical to write and produce identical happy-path behavior — the difference only shows up
   under a real brute-force attempt, which is exactly the scenario a demo/test run never exercises
   by accident. Worth choosing deliberately, not by whichever one autocompletes first.

### Interview questions this phase prepares you for

- Walk through exactly what changed in the request/response contract of `POST /api/incidents` to
  close the client-supplied-identity gap, and why the naive fix ("just validate the userId field
  matches the token") would still be wrong.
- Why does `/api/alerts/ingest` deliberately stay unauthenticated while every incident endpoint
  requires a token? What's the actual difference in trust model?
- What does `MapInboundClaims = false` actually change, and what bug does it prevent?
- Why `AddIdentityCore` + `AddSignInManager` instead of `AddIdentity` for an API-only service?
- Why doesn't auth get routed through MediatR when incident orchestration does? What's the actual
  test for "this belongs in the CQRS-scoped module" vs. "this doesn't"?

---

## Phase 8 — AI Integration (Claude API postmortem drafting)

**Status: done, code-verified but not yet live-called.** 57/57 tests passing (39 unit + 18
integration). This is the one LLM endpoint named all the way back in `career_roadmap_reasoning.md`
("covers the AI integration fluency expectation from the market") and it's the direct payoff of the
Anthropic "AI Fluency" cert already on the resume — this gives that cert a real, working example
behind it, not just a line item.

**Honest state of verification:** the request/response contract is proven correct against a
stubbed HTTP handler built from Anthropic's real documented API shape (below), and the app's
behavior with no API key configured is proven live (starts fine, the one endpoint that needs it
returns a clean 503). What's *not* yet proven is a real network round-trip against the actual
Anthropic API, because that needs a real, billable API key this environment doesn't have. If you
want that last mile verified, set one via `dotnet user-secrets set Claude:ApiKey <key>` in
`src/Beacon.Api` (never in appsettings.json — see `ClaudeSettings.cs`) and say so; until then, this
phase's live-verification bar is met the same way Phase 3's Redis work was: everything provably
correct except the one piece needing a credential I don't have.

### Before coding

**Why the query, not just a raw prompt, matters.** `PostmortemPromptBuilder.Build` reuses
`IncidentEventFormatter` (Phase 6) directly for the timeline section — the same event-to-text
rendering the Excel/PDF reports use, not a third, independently-drifting way of describing an
`IncidentEvent`. The prompt also explicitly tells the model this is a **draft for human review**
and instructs it not to invent details beyond the timeline — both are real prompt-engineering
choices to reduce hallucination risk in a business-report context, not boilerplate.

**Why a hand-rolled HTTP call instead of the official Anthropic SDK.** A real production system
calling several Anthropic endpoints would likely prefer the SDK for maintenance reasons. For one
endpoint in a portfolio project, a direct, visible HTTP call is the better teaching artifact — the
request shape, headers, and resilience policy are all *readable in this codebase*, not hidden
inside SDK internals. A real trade-off, made deliberately, not by default.

**Why `AddStandardResilienceHandler`, not the older `Microsoft.Extensions.Http.Polly` package.**
ARCHITECTURE.md named Polly explicitly for "the LLM API call" — a real external dependency that
can fail, rate-limit, or time out independently of Beacon. `AddStandardResilienceHandler` (built on
Polly v8, part of `Microsoft.Extensions.Http.Resilience`) is the current, Microsoft-recommended way
to get retry + circuit breaker + timeout on an `HttpClient` as of .NET 8+; the older
`AddPolicyHandler`-style Polly integration is the deprecated approach worth recognizing, not reaching
for by habit.

**A new, more correct HTTP status than anything used before.** `ExternalServiceNotConfiguredException`
→ 503 is a genuinely different case from every existing mapping in `DomainExceptionHandler`: not
"bad input" (400), not "doesn't exist" (404), not "wrong domain state" (409) — a missing
*deployment-time configuration* is its own failure mode with its own correct status code, and
conflating it with 409 (as reusing `InvalidOperationException` would have done) would have been
semantically sloppy.

### During coding — one real, easy-to-miss deserialization gotcha

`ClaudeMessageResponse`/`ClaudeContentBlock` use explicit `[JsonPropertyName("content")]`,
`[JsonPropertyName("text")]`, etc. on every property, rather than relying on default matching.
This isn't decorative: `System.Net.Http.Json`'s `ReadFromJsonAsync<T>()` does **not** use
ASP.NET Core's web JSON defaults (camelCase + case-insensitive matching) — it uses plain
`System.Text.Json` defaults, which are case-sensitive. Anthropic's real API returns lowercase JSON
keys (`"content"`, `"type"`, `"text"`); without the explicit attributes, PascalCase C# record
properties (`Content`, `Type`, `Text`) simply wouldn't bind, and the code would silently produce an
empty list instead of throwing — surfacing later as a confusing "no text content block" error even
against a perfectly valid API response. Caught by writing the stubbed-response test with Anthropic's
*real* lowercase JSON shape before assuming the naive record definition would just work.

### How this got verified without a real API key

Two genuinely different kinds of test, deliberately not confused with each other:

1. **`ClaudePostmortemDraftGeneratorTests`** constructs `ClaudePostmortemDraftGenerator` directly
   with a hand-written stub `HttpMessageHandler` (not through DI, not through
   `WebApplicationFactory`) that returns realistic Anthropic-shaped JSON. It asserts on the actual
   outgoing request — URL, `x-api-key`/`anthropic-version` headers, `model`/`max_tokens`/`messages`
   body shape — and on the parsed response text. This is what caught the JSON casing gotcha above.
2. **`GenerateIncidentPostmortemDraftQueryHandlerTests`** uses a trivial in-memory fake
   `IPostmortemDraftGenerator` (no HTTP at all) to prove the *business rule* — unresolved incidents
   throw and never even reach the generator — independent of whether Claude's API contract is
   involved.

Deliberately not built: a full `WebApplicationFactory`-based integration test hitting the real
endpoint end-to-end. That would need either a real API key (a real, billable dependency to keep
green in CI) or DI-level handler substitution inside the shared test factory, and the marginal
value over the two test types above is low — the endpoint's routing/auth/MediatR wiring is already
proven correct by every other endpoint using the identical pattern.

### Common mistakes (and why this phase's code avoids them)

1. **Assuming a response DTO's default property matching "just works" against a third-party API.**
   It depends entirely on which JSON stack and which defaults are in play — provably different
   between `System.Net.Http.Json` and ASP.NET Core's own web pipeline. Explicit
   `[JsonPropertyName]` attributes remove the ambiguity instead of hoping the defaults line up.
2. **Giving a real external secret a "dev placeholder" the way `Jwt:SigningKey` got one.** A JWT
   signing key only has to be internally self-consistent for local dev to function; a Claude API
   key is a real, billable, external credential with no honest placeholder value — leaving it empty
   and failing loudly (503, not silent nonsense output) is the correct default, not a gap.
3. **Building a slow, brittle "real" integration test just to look thorough.** A test that needs a
   live, billable external API to stay green is a maintenance liability, not a stronger guarantee —
   the stub-based test proves the same contract correctness deterministically and for free.

### Interview questions this phase prepares you for

- Walk through why the JSON deserialization would have silently failed without explicit
  `[JsonPropertyName]` attributes, and in which specific API (`System.Net.Http.Json` vs. ASP.NET
  Core's own JSON handling) that default actually differs.
- What does `AddStandardResilienceHandler` give you by default, and why does an LLM API call
  specifically need retry/circuit-breaker/timeout more than, say, a call to your own database?
- Why 503 for a missing API key instead of 500 or 409? What's the actual semantic difference a
  caller should be able to rely on?
- What's the actual trade-off between a hand-rolled HTTP client and an official SDK for a
  third-party API — and why did this project deliberately choose differently from what a real
  production system consuming many Anthropic endpoints probably should?

---

## Phase 9 — Web Dashboard (React + MobX)

**Status: done.** `Beacon.WarRoom` — Vite + React + TypeScript + MobX, RxJS, react-router,
Chart.js. First frontend work on this project, and the first phase verified with a real headless
browser instead of `dotnet test`: registered a user, logged in, watched live telemetry actually
render from the Phase 4 WebSocket, declared/acknowledged/resolved a real incident, and confirmed
the Excel/PDF/AI-postmortem buttons all appear correctly — with screenshots, not just "the build
succeeded."

Two small backend additions landed first, in their own commit, because the dashboard genuinely
needed them and they didn't exist yet: `GET /api/incidents` (a list endpoint — only single-incident
lookup existed through Phase 8) and a full `Timeline` array on `GetIncidentQuery`'s response
(previously just an `EventCount`). Both reuse `IncidentEventFormatter` (Phase 6/8) rather than
inventing a fourth way to render an `IncidentEvent` — worth noticing as a real, recurring payoff of
that one shared formatter existing at all.

### Before coding

**Why MobX, and why the store shapes look the way they do.** Three stores, each owning one thing:
`AuthStore` (session + token persistence), `IncidentStore` (list/detail/actions), `TelemetryStore`
(the WebSocket + live chart data). `makeAutoObservable` plus `observer()`-wrapped components is
the whole mechanism — no reducers, no action-type strings, no dispatch. The trade a Redux-shaped
codebase makes for explicit action logs isn't needed here; the trade MobX makes (implicit
reactivity via proxies) is the one that matches "a handful of stores, direct method calls" better.

**The circular-import trap avoided in `api/client.ts`.** `AuthStore` needs to attach its token to
every API request; naively, `apiClient` would import `authStore` to read the token, while
`authStore` imports `apiClient` to make its own login/register calls — a circular dependency.
Fixed by having `client.ts` export a plain `setAuthToken()` function that mutates the axios
instance's default headers directly; `AuthStore` calls it, `client.ts` never imports `AuthStore`
at all. A small, real example of "which module should own the dependency direction" mattering.

**A genuine reason to reach for RxJS, not just because the resume lists it.**
`MetricsSimulatorService` (Phase 4) broadcasts 3 WebSocket messages per 2-second tick — one per
simulated service. Applying each one straight to MobX state as it arrives would trigger 3
React re-renders in quick succession for what's really one tick of new data.
`TelemetryStore` pipes incoming samples through `rawSamples$.pipe(bufferTime(500), filter(...))`
first, batching whatever arrived in the last half-second into a single state update. That's the
actual justification for RxJS here — reducing render churn against a known, bursty message
pattern — not a checkbox for the skill.

**Why file downloads go through `apiClient`, not a plain `<a href>`.** Every incident endpoint
requires a Bearer token (Phase 7), and a browser's native anchor navigation can't attach a custom
`Authorization` header. `downloadReport()` fetches the file through the authenticated `apiClient`
with `responseType: "blob"`, then hands the browser a `URL.createObjectURL(blob)` via a
programmatically-clicked, invisible anchor. This is the actual correct pattern for an authenticated
file download in an SPA — a real, recurring gotcha, not specific to this project.

### During coding — what got built, and what real verification actually caught

| File | What it is |
|---|---|
| `src/frontend/Beacon.WarRoom/src/stores/*.ts` | Auth, Incident, Telemetry — one store, one responsibility each |
| `src/frontend/Beacon.WarRoom/src/pages/*.tsx` | Login, Dashboard, Incident Detail |
| `src/frontend/Beacon.WarRoom/src/components/*.tsx` | Protected route guard, declare-incident form, telemetry chart |
| `src/frontend/Beacon.WarRoom/verify.mjs` | A one-off Playwright driver — not part of the app; see below |

**`chromium-cli` (the `run` skill's default browser driver) isn't available on this Windows
machine.** The skill's own fallback guidance — adapt the Electron example's `_electron`/Playwright
REPL pattern — was followed instead: installed `playwright` as a dev dependency, pulled the
Chromium binary (`npx playwright install chromium`), and wrote `verify.mjs`, a small script that
drives the real running app through the full register → login → declare → acknowledge → resolve
flow and screenshots each step. This is a genuine example of "the fallback pattern needed real
setup work" — the skill itself suggests that's the signal to consider `/run-skill-generator` so a
future session doesn't rediscover the same Windows-specific `chromium-cli` gap from scratch.

**What looking at the actual screenshots caught, beyond "the script didn't throw."** The Playwright
run itself reported all 8 steps green with zero console errors — but the *resolved incident
timeline screenshot* surfaced something the passing script didn't: the "Actor" next to each
timeline event renders as a raw GUID (`6c5975b9-2c9e-...`), not an email or display name. This is
technically correct — `IncidentEventFormatter.Actor()` returns exactly the user-id claim `Phase 7`'s
`GetUserId()` extracts from the JWT, and that's genuinely the user's Identity `Id` — but it's a
real, honest UX gap: a human reading this timeline sees an opaque ID, not "who did this." Fixing it
properly means resolving a display name for a user id somewhere in the stack — a real design
question (denormalize a display name onto each domain event at write time? Look it up at read
time via a new endpoint?) — not a one-line patch, so it's tracked here as an explicit open item
rather than silently left for someone to notice later. This is exactly why "look at the screenshot"
is the skill's own explicit instruction, not "check the exit code": an all-green automated run and
an actually-correct UI are different claims.

### Common mistakes (and why this phase's code avoids them)

1. **Trusting "the script exited 0" as proof the UI is correct.** It proves the selectors matched
   and nothing threw — not that what rendered was right. The GUID-instead-of-name gap above was
   invisible to the passing script and only became visible by reading the screenshot.
2. **Letting two modules import each other for convenience.** The `AuthStore`/`apiClient`
   circular-import shape is an easy trap the moment "the thing that has the token" and "the thing
   that uses the token" both feel like they should own the relationship.
3. **Assuming a browser will attach auth headers to a plain link.** It won't — and a report
   download button that quietly 401s because of this is a real, easy-to-ship bug in any SPA sitting
   in front of a JWT-protected API.

### Interview questions this phase prepares you for

- Walk through the actual mechanism `makeAutoObservable` + `observer()` uses to trigger re-renders
  — what is MobX actually watching?
- Why does `TelemetryStore` use `bufferTime` instead of applying every WebSocket message to state
  immediately? What would the UI cost be without it?
- Why can't a plain `<a href="/api/incidents/{id}/report.pdf">` work once that endpoint requires a
  JWT, and what's the actual fix?
- The Playwright run passed cleanly, but the screenshot revealed a real UX bug it didn't catch.
  What's the general lesson about what an automated E2E pass does and doesn't prove?

---

## Phase 10 — On-Call Console (Electron)

**Status: done.** `Beacon.OnCallConsole` — plain HTML/JS renderer (no framework; one screen
doesn't justify React/MobX the way Beacon.WarRoom's several interlocking views do), a proper
`contextIsolation`/no-`nodeIntegration` main process, a native SQLite offline queue, and native OS
notifications. Verified with two real, separate Playwright `_electron` runs — one happy-path, one
that genuinely killed and restarted the backend mid-test to prove the offline queue actually
recovers, not just that it accepts a queued write.

### Before coding

**Why `node:sqlite`, not `better-sqlite3`.** `better-sqlite3` is a native addon — it needs
compiling against Electron's specific ABI (via `@electron/rebuild`), which in turn needs a full
Visual Studio C++ workload. This machine has VS2022 Community *without* that workload, and
installing a multi-GB workload for one small embedded queue wasn't a good trade. Electron 43
bundles Node 24, which ships a stable built-in `node:sqlite` — zero native compilation, at the
cost of a smaller API than `better-sqlite3`'s. The right trade here, not the only possible one.

**Why `contextIsolation: true` and `nodeIntegration: false`, not the simpler legacy pattern.**
The easy path (`nodeIntegration: true`) lets renderer JS call `require()` directly — convenient,
and a real security hole: any content that ends up running in that window (a bug that lets a URL
load unexpectedly, a future feature that renders untrusted text) gets full Node access — the
filesystem, `child_process`, everything. `preload.js` exposes exactly six functions via
`contextBridge.exposeInMainWorld`; that's the renderer's entire reachable surface, deliberately,
the same "narrow the trust boundary" instinct as Phase 7's claims-derived identity.

**The optimistic-write design for the offline queue.** `beacon:acknowledge`/`beacon:escalate`
try the real API call *first* and only fall back to queuing on failure — not "always queue, sync
later." The common case on a real on-call console is a working connection; a UI that always shows
"queued, will send" would be misleading almost all the time. Queuing is the exception path, not
the default path.

### During coding — one real environment gotcha, not an app bug

Both the raw `electron.cmd` binary and Playwright's `_electron.launch()` failed with confusing
errors on the first attempt — `ipcMain` came back `undefined` when destructured from
`require("electron")`. Not a bug in `main.js`: `ELECTRON_RUN_AS_NODE=1` is set globally in this
sandboxed shell (almost certainly to stop agent tooling from accidentally spawning GUI windows),
which makes any Electron binary run as plain Node instead of the real app framework — so
`require("electron")` returns a path string, not the API object. Fixed by explicitly clearing that
one variable for the verification launches only (`delete launchEnv.ELECTRON_RUN_AS_NODE` before
`electron.launch()`), not by hunting for a code-level cause that didn't exist.

### How the offline queue actually got verified

Not simulated — two cooperating processes, coordinated for real: `verify-offline.mjs` logs in
while the API is up, then polls `/health` waiting for it to go down; a *separate* tool call kills
the real `dotnet run` backend process while the script is mid-wait; the script detects the outage,
clicks Acknowledge, and confirms the `.queued-note` actually appears and `node:sqlite`'s queue
depth reads 1. The backend is then genuinely restarted, and — this is the part that matters —
the check that mattered most wasn't in the Electron window at all: a completely separate
`Invoke-RestMethod` call, using a freshly-registered throwaway user, queried
`GET /api/incidents` directly and confirmed the incident's status was really `Acknowledged` in
the database. That's the actual proof the queue → retry → real API chain works, not just that the
UI's queued-indicator toggled.

One test-script bug surfaced along the way and is worth naming: the first version asserted
`waitForSelector("#queue-indicator.hidden")`, which can never pass — Playwright's `waitForSelector`
defaults to `state: "visible"`, and an element carrying a `hidden` class is by definition never
visible. The fix was polling the element's `class` attribute directly instead of asking Playwright
to wait for visibility of something deliberately invisible — a real, easy mistake to make once,
worth remembering exactly because it produces a *timeout*, not a clear assertion failure.

### Common mistakes (and why this phase's code avoids them)

1. **`nodeIntegration: true` "just to get something working quickly."** It works, and it's a real
   security regression the moment anything not fully trusted ever renders in that window.
2. **Assuming a native npm module will "just work" in Electron the way it does in plain Node.**
   Different ABI, different toolchain requirements — checked before committing to a dependency,
   not discovered after a confusing runtime crash.
3. **Testing "does the queue accept a write" instead of "does the write eventually really reach
   the server."** The first is a much weaker claim — this phase's verification deliberately checks
   the second, from a source (a fresh, unrelated API call) that can't be fooled by anything the
   Electron app itself might get wrong.

### Interview questions this phase prepares you for

- What does `contextIsolation` + `contextBridge` actually protect against, concretely — walk
  through what a malicious or buggy renderer *could* do with `nodeIntegration: true` that it can't
  do here.
- Why optimistic-write-then-queue-on-failure instead of always queuing and syncing on an interval?
  What's the actual UX cost of the alternative?
- What's the real difference between "the offline queue accepted the action" and "the offline
  queue's contents eventually reached the server" as claims — and why does only the second one
  actually matter?
- Native Node addons vs. built-in modules like `node:sqlite` — what's the actual trade-off, and
  when would you reach for the native addon anyway despite the extra build complexity?

---

## Phase 11 — Admin Portal (Blazor Server)

**Status: done.** `Beacon.Admin` — a Blazor Server admin portal (login, user/role management,
an analytics dashboard with a real ApexCharts bar chart, and a live incident ticker fed by a
server-to-server SignalR connection). Backend additions: a MediatR pub/sub notification
(`IncidentDeclaredNotification`) published alongside the existing declare-incident command, an
unauthenticated `IncidentAlertsHub`, and admin-only user/role endpoints. Verified end-to-end with a
real Playwright browser run — and that run is what actually found three genuine bugs no compiler
or test suite would have caught.

### Before coding

**Why a third UI stack, not a third React app.** Beacon.WarRoom (Phase 9) is React/MobX,
Beacon.OnCallConsole (Phase 10) is Electron/vanilla JS. Beacon.Admin is Blazor Server: C# on both
sides of the wire, server-rendered HTML pushed over a persistent SignalR circuit instead of a JSON
API consumed by client-side JS. That's a deliberate resume choice — it demonstrates a genuinely
different execution model (stateful server-side UI, not a stateless API + thin client), which is
exactly the kind of breadth an interviewer probes for when a candidate claims ".NET full-stack."

**MediatR pub/sub vs. request/response, used deliberately side by side.** Every earlier phase used
MediatR's `ISender`/`IRequestHandler` for commands and queries — one handler, one response.
`IncidentDeclaredNotification` is the other half of MediatR: an `INotification` published via
`IPublisher`, fanned out to *every* registered `INotificationHandler<T>` with no return value. The
declare-incident use case doesn't know or care that Beacon.Api also happens to broadcast over
SignalR — `DeclareIncidentCommandHandler` just publishes a fact; `IncidentDeclaredNotificationHandler`
(living in Beacon.Api, not Application) reacts to it. That separation is the point: the write side
of the domain stays ignorant of every downstream side effect.

**Applying a known SignalR gotcha *before* it became a bug.** SignalR's Hub protocol serializes
messages with its own JSON pipeline, entirely separate from `ConfigureHttpJsonOptions` — the exact
category of bug hit twice already this project (Phase 2's `IncidentId` wrapper-object leak, Phase
8's missing `[JsonPropertyName]`). `IncidentDeclaredNotificationHandler` sends
`notification.IncidentId.Value` (a plain `Guid`) and `notification.Severity.ToString()` (a plain
`string`), not the domain types directly — recognized and fixed proactively by pattern-matching
against the earlier bugs, not discovered by a failing client.

### During coding — three real bugs, all found by actually running the app

**1. The `--empty` Blazor template scaffold never applied an interactive render mode.**
`Program.cs` had `.AddInteractiveServerComponents()` and `.AddInteractiveServerRenderMode()` —
which make interactivity *available*, but nothing opts into it. The full "Blazor Web App" template
normally stamps `@rendermode="InteractiveServer"` onto `<Routes />` in `App.razor`; the `--empty`
scaffold used to create this project skips that. Result: every page rendered as static SSR only —
no SignalR circuit ever connected, so `EditForm`'s submit handler was never wired up as an
interactive event, and clicking "Log in" fell through to a plain browser HTTP POST, which the
server rejected outright (`"The POST request does not specify which form is being submitted... add
a @formname attribute"`). Fixed by adding `@rendermode="InteractiveServer"` to both `<HeadOutlet />`
and `<Routes />`. A footnote bug inside the fix itself: the first attempt wrote the explanatory
comment directly above those tags using the literal text `@rendermode`/`@formname` inside an HTML
`<!-- -->` comment — Razor still parses `@` inside HTML comments, so it tried to treat the comment
text itself as a directive (`RZ2005`). Fixed by escaping to `@@rendermode` — a real, easy-to-repeat
trap for anyone writing prose about Razor syntax inside a `.razor` file.

**2. `IHttpClientFactory` message handlers don't share the circuit's DI scope.**
`AuthorizationMessageHandler` was wired via `.AddHttpMessageHandler<AuthorizationMessageHandler>()`
on the typed `BeaconApiClient`, and depended on the Scoped `AdminAuthState` to attach the bearer
token. This is a known, documented Blazor Server pitfall: `IHttpClientFactory` builds and pools its
message handlers in its *own* internal DI scope (so handlers can be reused across the factory's
handler-lifetime window), which is a different scope from the one the current circuit resolves
`AdminAuthState` from. The handler's captured `AdminAuthState` was never the same instance
`Login.razor` called `SetSession` on — its `Token` was always `null`, no `Authorization` header was
ever sent, and every admin API call silently 401'd into an unhandled circuit exception (the yellow
"An unhandled error has occurred" bar). Fixed by removing the message handler entirely and having
`BeaconApiClient` set `http.DefaultRequestHeaders.Authorization` itself, from an `AdminAuthState`
injected directly into the typed client (which *does* resolve correctly, since the client itself is
requested through the circuit's own scope, not the factory's pooled one).

**3. `MainLayout.OnInitializedAsync` runs exactly once per circuit — before login, not after.**
The live-alerts ticker's `AlertsClient.StartAsync()` call lived in `MainLayout.OnInitializedAsync`,
gated on `AuthState.IsAuthenticated`. That looks right, but a layout component instance is created
once per circuit and is *not* re-initialized on later in-app navigation (`RouteView`'s `Body` swaps;
the surrounding layout persists) — so `OnInitializedAsync` only ever runs during the very first
render, which happens before the user has logged in. The SignalR connection to
`IncidentAlertsHub` was therefore never started for the entire lifetime of any session. Fixed by
also calling `AlertsClient.StartAsync()` from the `AuthState.Changed` event handler, so the
connection starts the moment login actually flips the auth state, not only at circuit creation.

A fourth, smaller issue surfaced while chasing bug 3: `Analytics.razor`'s `DisposeAsync` called
`JS.InvokeVoidAsync` unconditionally to clean up the ApexCharts instance, catching only
`JSDisconnectedException`. If the component is disposed before its first `OnAfterRenderAsync` JS
call ever ran, the JS runtime throws a *different* exception
(`InvalidOperationException: "...component is being statically rendered"`) — caught by adding a
`_rendered` guard so disposal is a no-op when there was never a chart to clean up.

### How it was actually verified

A Playwright script (`verify.mjs`) registers two real users directly against
`POST /api/auth/register` — Beacon.Admin's login page has no sign-up UI by design (an internal ops
tool), and registration deliberately lets the caller pick their own role, an explicit demo
simplification documented on the endpoint itself. It then drives a real Chromium browser: logs in
through the actual form, confirms the users table renders, changes a *different* user's role
through the UI and confirms the change round-trips through a separate, independent
`GET /api/admin/users` call (not just that the UI displayed "Saved"), navigates to `/analytics` and
asserts a real `<svg>` exists inside the ApexCharts container (not just that the page loaded), then
declares a brand-new incident through a completely separate API call and confirms the live-alerts
ticker actually displays that incident's title — proof the SignalR broadcast chain works
end-to-end, not just that the hub exists.

One test-design detail worth naming: the script originally used `page.goto()` to navigate between
`/users` and `/analytics`, which immediately broke the session — `AdminAuthState` is Scoped and
held only in server memory per circuit, with no cookie or localStorage backing it, so a hard
navigation (a real new HTTP GET) tears down the old circuit and starts a fresh one with no token.
That's correct, intentional behavior for this app, not a bug to route around in the app itself —
the fix belongs in the test, which now clicks the in-app nav links instead (Blazor Server's
"enhanced navigation" intercepts same-origin link clicks and keeps them on the same circuit).

### Common mistakes (and why this phase's code avoids them)

1. **Assuming `AddInteractiveServerComponents()` + `AddInteractiveServerRenderMode()` is enough.**
   Both only make interactivity *available* at the DI/endpoint level — nothing renders
   interactively until a component tree actually opts in via `@rendermode`.
2. **Injecting a Scoped Blazor Server service into anything resolved through
   `IHttpClientFactory`'s handler pipeline** (`AddHttpMessageHandler<T>()`). The handler is built in
   the factory's own pooled scope, not the circuit's — any Scoped dependency it captures is silently
   the wrong instance, with no compiler error and no obvious runtime error pointing at the cause.
3. **Trusting a layout component's `OnInitializedAsync` to react to state that changes later.** It
   runs once, for the life of the circuit — anything that depends on state established *after* that
   first render (like post-login auth) needs its own event subscription, not a one-time check.
4. **Treating "the Playwright run exited 0" as proof of correctness before actually reading the
   screenshots and logs.** Every one of the three real bugs above showed up as either a clear
   assertion failure or a visibly broken screenshot — never a green run with a silent gap.

### Interview questions this phase prepares you for

- Walk through what actually happens, request by request, between a browser loading a Blazor Server
  page and a button click on that page triggering server-side C# code. Where does static SSR end
  and the interactive circuit begin?
- Why can't a `DelegatingHandler` registered via `AddHttpMessageHandler<T>()` safely depend on a
  Scoped service in a Blazor Server app? What's the actual mechanism, not just "it doesn't work"?
- MediatR command/query handlers vs. notification handlers — when does a side effect belong in the
  command handler itself, and when does it belong in a separate `INotificationHandler`?
- Why does SignalR need its own JSON serialization handling separate from the rest of the API, and
  what's the concrete failure mode if you forget that and send a domain type directly?

---

## Next: Phase 12 — Public Status Page (ASP.NET Core MVC)

Not started. Still open from earlier phases: Docker's daemon still isn't running (checked again
this session — unchanged), so Redis-dependent verification (Phase 3's live-Redis test) and any
Docker/Testcontainers-dependent phase stay blocked on that; push this repo's history to GitHub once
the account/authentication issue on that end is sorted out; if a real Anthropic API key gets
configured via user-secrets, do the one remaining live end-to-end call Phase 8's code is otherwise
ready for; and the raw-GUID-actor UX gap from Phase 9 is still open (needs a real design decision,
not a quick patch).
