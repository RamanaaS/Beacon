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

## Next: Phase 4 — Real-Time Telemetry (Device Simulator, raw WebSockets) + closing out Phase 3's live verification

Not started. Will cover: standing up the Device Simulator, streaming synthetic telemetry over a
raw `System.Net.WebSockets` handler (the direct continuation of the GRL production experience —
see ARCHITECTURE.md Table A) — plus, once Redis is reachable, the live-Redis integration test and
manual verification that Phase 3 left open.
