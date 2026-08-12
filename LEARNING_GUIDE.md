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

## Next: Phase 2 — Data Layer (EF Core + SQL Server)

Not started. Will cover: mapping the `Incident` aggregate and its event log to EF Core (including
how to persist a closed hierarchy of event records — this is where the TPH vs. TPT vs. one-JSON-column
decision for `IncidentEvent` gets made and explained), migrations, and the first real HTTP endpoints
in `Beacon.Api` for registering an alert source.
