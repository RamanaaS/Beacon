# Beacon — Architecture & Design Document

*Phase 0 deliverable. No code yet. Written 2026-08-12. Supersedes the earlier charging-domain draft (ChargeMesh) — same rigor, different domain, and more freedom to pick better-than-resume tooling where it's genuinely the stronger choice.*

> Still the "one real project" from `career_roadmap_reasoning.md` Part 4: ASP.NET Core + EF Core + Docker + GitHub Actions CI/CD + Azure + one LLM endpoint. What changed on your request: the domain is no longer wireless-charging/WPC-adjacent, and I'm no longer constrained to *only* what's literally on your resume — where a better modern tool exists, I use it as the primary choice and demonstrate the resume tool alongside it for comparison, rather than defaulting to the resume tool everywhere.

---

## 0. Ground rules, updated

Same honesty principle as before, one addition:

1. **Table A** — resume skills, mapped to a real place in the project.
2. **Table B** — genuinely new technologies this project introduces to close your roadmap's named gap (EF Core, Docker, Azure, JWT, one LLM endpoint, etc.) — never claimed as existing resume experience.
3. **Table C (new)** — places where I deliberately chose a *better modern alternative* over your literal resume tool, per your instruction. Your resume tool is still demonstrated somewhere in the project for comparison, so you can speak to both in an interview — but it's not the default everywhere just because it's what you already know.

---

## 1. The Project: Beacon

**Tagline:** Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

### Problem statement

When a production incident happens, an engineering team needs, in order: to know something's wrong (alerting), to coordinate a response (war room, roles, timeline), to fix it (runbooks, remediation actions), to tell customers what's happening (status page), and afterward to understand what happened well enough to prevent it recurring (postmortem) — and today that's usually five disconnected tools (PagerDuty + Slack + a wiki + Statuspage + a Google Doc) stitched together by habit, with the timeline reconstructed from memory after the fact.

Beacon is a single system that owns that whole lifecycle: alert ingestion → incident declaration → real-time coordinated response → public status communication → AI-assisted postmortem generation, with every step captured as a structured, queryable timeline instead of scattered chat logs.

### Why this domain, not another generic app

- It's universally understood by any engineering interviewer — you don't have to explain the domain before explaining the architecture, unlike a niche compliance-test tool.
- It's genuinely technically deep: event-sourced timelines, alert-storm deduplication, real-time multi-user collaboration, an automated runbook execution engine, and AI-generated postmortems are all real, hard problems — not CRUD with a different noun.
- It still lets your actual differentiators show up honestly: you have real experience with release management, CI/CD gates, and root-causing production issues at GRL ("root-causing 100+ protocol, hardware-communication, and UI issues... sustaining a 98% defect-free deployment rate") — that's a legitimate, first-hand reason you understand *why* an incident tool needs to work a certain way, without the project itself needing to be about wireless charging.

### Target users

- **Responding engineers** — declare/join an incident, coordinate in a live timeline, run remediation actions.
- **Incident commanders / SRE leads** — own the response, manage escalation, own the postmortem.
- **On-call engineers** — get paged, acknowledge/escalate from a desktop console even when no browser is open.
- **Customers / the public** — read the status page during an outage, with no login.
- **Engineering managers** — review MTTR/MTTA trends and postmortem action-item completion over time.

### Major features

1. Alert ingestion from webhook-based monitoring sources (Prometheus Alertmanager–style payloads), with dedup and noise suppression
2. Incident declaration, severity, role assignment (commander/scribe/comms), and a structured, append-only timeline
3. Real-time multi-user war room (live timeline, presence, chat) — one incident, many responders, no stale state
4. Runbook engine — versioned, scriptable remediation steps that can be executed (simulated) directly from an incident
5. On-call scheduling and escalation policies, with a desktop console for acknowledge/escalate/snooze
6. Public status page — component health, active incidents, historical uptime, no auth required
7. AI-drafted postmortem — after resolution, Beacon assembles the full timeline + metrics and calls an LLM to draft a root-cause summary, timeline narrative, and action items for a human to edit, not publish verbatim
8. MTTR/MTTA analytics and exportable incident reports (Excel/PDF)
9. Legacy ITSM bridge — incidents optionally sync out to a (simulated) legacy Java-based ticketing system some enterprises still run

### Technical challenges

- Deduplicating and rate-limiting bursts of near-identical alerts (an "alert storm") without dropping a genuinely new one
- Keeping a multi-user, real-time incident timeline consistent when several responders act at once (an append-only event log, not a mutable "current state" row, is the actual answer — explained in Section 5)
- Running the on-call console as a background-capable desktop app that still gets native OS notifications with the browser closed
- Bridging to an external legacy system without letting its data shape leak into Beacon's own domain model
- Keeping the public status page fast and correct while the incident it's describing is still actively changing underneath it

---

## 2. Skill Inventory (from your resume — unchanged)

| Category | Skills |
|---|---|
| Languages | C#, JavaScript (ES6+), Java, Python, C/C++, SQL, HTML5, CSS3 |
| Backend & APIs | ASP.NET Core (Web API, MVC), .NET Framework 4.8, .NET Core, REST API design, Swagger/OpenAPI, WebSockets, Newtonsoft.Json, class-library architecture |
| Frontend & Desktop | React, MobX, Electron, Blazor, Chart.js, ApexCharts, RxJS, Axios, Bootstrap |
| Databases | SQLite, MS SQL Server |
| DevOps & Tools | Git, GitHub, GitHub Actions, SonarQube, MSBuild, NuGet, Visual Studio, VS Code, SSMS |
| Testing & Practices | MSTest, unit testing, Postman, Selenium (C#), OOP, SOLID, design patterns, Agile/Scrum, code review, release management |

Domain skills (Qi/WPC, CTS, MPP, TPT/TPR, hardware/instrument control) are **not** carried into this project — that was the point of moving off the charging domain. Where relevant below, I note the closest honest analog instead of pretending they transfer directly.

---

## 3. Skill → Feature Mapping

### Table A — Resume skills, showcased as-is

| Resume skill | Where it's used in Beacon | Why it's appropriate | What you reinforce |
|---|---|---|---|
| C#, OOP, SOLID, design patterns | Entire backend | Daily language; the whole project is the vehicle | Strategy (runbook steps), Factory (alert-source parsers), applied deliberately |
| ASP.NET Core Web API | `Beacon.Api` | Direct continuation of your 141-endpoint API work | Modern hosting model vs. the Framework 4.8 style you use daily |
| ASP.NET Core MVC | `Beacon.StatusPage` — public status page | Public, cacheable, SEO-relevant pages are still MVC/Razor's strongest fit | Server-rendered pages done right, not as a SPA-by-default reflex |
| REST, Swagger/OpenAPI | All `Beacon.Api` surfaces | Continuation of Swagger-documented API delivery at GRL | Versioning, ProblemDetails, pagination at real scale |
| WebSockets (raw) | Internal high-frequency metrics stream feeding the war-room dashboard | Direct continuation of your GRL telemetry-streaming work | Reimplemented from scratch, contrasted against SignalR (Table C) |
| Newtonsoft.Json | Parsing inbound legacy-shaped alert webhook payloads | Realistic: many real monitoring tools still emit slightly non-conforming JSON that needs lenient parsing | Direct, honest comparison against `System.Text.Json` (the new default — see Table C) |
| Class-library architecture | Solution split into Domain/Application/Infrastructure/Api/Workers | Same discipline as your 25+ class-library GRL solution | Doing it on a greenfield solution you fully own |
| React, MobX, RxJS, Axios, Chart.js | `Beacon.WarRoom` — real-time incident dashboard | Matches your actual FE stack at GRL | Building the real-time state layer yourself |
| Electron | `Beacon.OnCallConsole` — desktop on-call app | You already ship an Electron desktop client in production | Background/tray behavior, native OS notifications, works with no browser open |
| Blazor | `Beacon.Admin` — runbook/escalation/service-catalog admin | You already shipped a real Blazor feature at GRL | Blazor Server lifecycle, component model, SignalR-backed live updates |
| ApexCharts | MTTR/MTTA analytics views in `Beacon.Admin` | Better export/annotation for report-style analytics than Chart.js | Deliberate two-chart-library split, not accidental duplication |
| Bootstrap | Styling `Beacon.Admin` | Fast, consistent internal-tool styling | — |
| SQLite | `Beacon.OnCallConsole` local cache (queued acks while offline) | Same local-store pattern you use at GRL | Local write, sync-on-reconnect |
| MS SQL Server | Primary store (`Beacon.Infrastructure`) | Your daily-driver RDBMS | Schema design from scratch |
| EPPlus, PDFsharp | Incident and MTTR report export | Direct continuation of your report-automation work | Async report generation as a background job |
| Postman | Collection in `/docs/postman/`, run via Newman in CI | Continuation of your API-testing practice | Manual testing turned into an automated CI gate |
| Git, GitHub, GitHub Actions | Full repo, branching, CI/CD pipeline | Same tools, greenfield project | Designing the pipeline yourself |
| SonarQube | CI quality gate | You already gate PRs on this at GRL | Configuring the gate and rules yourself |
| MSBuild, NuGet | Solution build | Standard daily tooling | Multi-project build ordering |
| Agile/Scrum, code review, release management | Phases run as sprints; PR template + review checklist in `/docs/` | You already work this way | Setting your own cadence, solo |
| Java | `itsm-bridge` — legacy ITSM sync service | Realistic: many enterprises still run an old Java-based ticketing system that a modern incident tool must bridge to, not replace overnight | The one deliberate service boundary (see Section 5, "why not microservices") |
| Python | `tools/alert-simulator` | Fast scripting to fire synthetic Alertmanager-style webhook bursts for local dev/load testing, without needing real production monitoring | Supporting tool, same role Python plays in most .NET shops |
| C/C++ | `native/log-parser-shim` | High-throughput raw log-line parsing on the ingestion path, P/Invoke-wrapped — the same reason real tools like Fluent Bit/Vector use native code for this exact layer | C#↔native interop, a genuine and commonly-asked interview topic |
| MSTest | One full parallel test suite (resume-parity) | Proves you can work in the framework GRL actually uses | Kept honest, not silently dropped — see Table C for why it isn't the *primary* suite |
| Selenium (C#) | One E2E suite (resume-parity), same flows as the Playwright suite | Same reasoning as MSTest above | Direct side-by-side comparison against Playwright — see Table C |

**Still deliberately excluded: .NET Framework 4.8**, for the same reason as before — it's already proven at your day job; this project exists to close the modern-.NET gap.

### Table B — New technologies (gap-fill from your roadmap, not resume claims)

| New technology | Where it's used | Why it's needed | What you'll learn |
|---|---|---|---|
| Entity Framework Core | `Beacon.Infrastructure` | Named explicitly in your roadmap as gap #1 | Migrations, change tracking, query translation, N+1 pitfalls |
| Docker / Docker Compose | Every service | Baseline for any post-2020 deployment, per your roadmap | Dockerfile authoring, multi-container local orchestration |
| GitHub Actions → Azure deploy | CI/CD pipeline | Extends your existing CI knowledge to CD | Build → test → containerize → deploy, the full loop |
| Azure (App Service, Azure SQL, Blob Storage, Key Vault, Service Bus, Monitor) | Deployment target | Explicitly chosen in your roadmap doc | Real deployed-and-operating experience |
| JWT / ASP.NET Core Identity | Auth on `Beacon.Api` and `Beacon.Admin` | GRL's desktop-hosted app doesn't need this; virtually every modern API does | Token issuance, refresh rotation, role/policy-based auth |
| One LLM endpoint (Claude API) | `POST /api/incidents/{id}/postmortem/draft` | Named explicitly in your roadmap ("AI integration fluency"); you already hold the Anthropic AI Fluency cert on your resume — this gives it a real working example | Prompt construction from structured data, resilience around an external AI call |
| MediatR + CQRS (scoped) | `Beacon.IncidentEngine` only | Incident timeline writes are audit-critical and high-concurrency; the Service Catalog and User Admin (simple CRUD) deliberately do **not** use it | Recognizing where CQRS earns its cost vs. where it's ceremony |
| RabbitMQ (local) / Azure Service Bus (prod) | Alert ingestion queue, ITSM-bridge integration | Alert storms genuinely arrive faster than synchronous handling should absorb | Queues, at-least-once delivery, idempotent consumers |
| Redis | Escalation-policy/on-call-schedule cache; alert-dedup window; SignalR backplane | Named in your roadmap as the highest-leverage cache store | Cache-aside, TTL-based dedup, when *not* to cache |
| SignalR | `Beacon.Admin` live alerts, `Beacon.WarRoom` presence/chat | Deliberately used alongside raw WebSockets (Table A) for a direct comparison | When SignalR's abstraction helps vs. when raw sockets are the better fit |
| gRPC | `Beacon.Api` ↔ `Beacon.RunbookEngine` (executing a remediation step) | Low-latency, strongly-typed internal RPC — a genuine use case | Protobuf contracts, streaming RPCs |
| Polly | Around the ITSM-bridge call and the LLM API call | Both are external dependencies that fail independently of Beacon | Retry, circuit breaker, timeout policies |
| Serilog + OpenTelemetry | All services | Needed the moment there's more than one process | Correlating one request across service boundaries |

### Table C — Where I deliberately chose better-than-resume, per your instruction

| Area | Resume tool | What Beacon uses as primary | Why | Resume tool still shown |
|---|---|---|---|---|
| Testing framework | MSTest | **xUnit + FluentAssertions + Moq** | Current de-facto community standard for greenfield .NET — better parallelism model, `[Theory]`/`[InlineData`] for data-driven tests, far larger ecosystem of analyzers/extensions in 2026 | Yes — one full parallel MSTest suite, so you can speak to both |
| JSON serialization | Newtonsoft.Json | **`System.Text.Json`** | Built into the runtime since .NET Core 3, meaningfully faster, no extra dependency; Microsoft's own guidance is Newtonsoft only for legacy compatibility needs | Yes — one legacy-webhook parsing path, explicitly framed as "why you'd still reach for Newtonsoft" |
| E2E browser testing | Selenium (C#) | **Playwright (.NET)** | Auto-waiting (fewer flaky `Thread.Sleep`-style tests), faster, better trace/debugging tooling, increasingly the default choice over Selenium for new suites | Yes — one comparison suite covering the same flows |
| API endpoint style | Controller-based Web API (implied by "Web API, MVC") | **Minimal APIs** for most of `Beacon.Api` | Less ceremony for a solo-owned API surface; Microsoft's own new-project template default since .NET 6 | Yes — controller-based MVC still used deliberately for the server-rendered Status Page, which genuinely needs it |
| Data-access abstraction | (not on resume — default instinct is often "add a Repository over everything") | **No blanket Repository/Unit-of-Work layer.** `DbContext` already *is* a Unit of Work; `DbSet<T>` already *is* a repository-shaped abstraction | Adding another layer on top is redundant indirection with no real payoff for this project's size — a real senior-level judgment call, not a missing pattern | A genuine `IEscalationScheduleStore` interface *is* introduced, but only where it earns its keep: abstracting Redis+SQL together behind one read path |
| Local multi-service dev | Docker Compose only | **Docker Compose + .NET Aspire** app-host for the .NET-native services | Aspire (GA since .NET 8) gives service discovery, a local dashboard, and structured OpenTelemetry wiring for the .NET services specifically; Compose still owns the non-.NET pieces (SQL Server, Redis, RabbitMQ, the Java bridge) so the whole stack still runs the same way in CI/prod | Not resume-relevant, but documented in Table B/deployment notes so the "why both" reasoning is explicit, not two competing tools left unexplained |

---

## 4. High-Level Architecture

Beacon is a **modular monolith with one deliberate service boundary** (the Java ITSM bridge) — same reasoning as before: one owner, no independent-scaling need, and the one real boundary exists because it's a genuinely different language/lifecycle/ownership situation, not because microservices are the "enterprise" default.

```
                                     ┌───────────────────────────────┐
                                     │           Azure Cloud          │
                                     │  App Service/AKS · Key Vault · │
                                     │  Blob Storage · Azure SQL ·    │
                                     │  Service Bus · Monitor          │
                                     └───────────────┬─────────────────┘
                                                     │
        ┌────────────────────────────────────────────┼────────────────────────────────────────────┐
        │                                              │                                            │
┌───────▼─────────┐  HTTPS/JSON      ┌─────────────────▼──────────────────┐   gRPC   ┌───────────────▼───────────────┐
│ Beacon.WarRoom    │◄────────────────►│            Beacon.Api               │◄────────►│  Beacon.RunbookEngine           │
│ React+MobX+RxJS   │  raw WebSocket   │  Minimal APIs (+ MVC for status page)│          │  (versioned remediation steps,  │
│ (incident timeline,│◄────────────────►│  Swagger · JWT · rate limiting ·    │          │   simulated execution)           │
│  live chat)        │  metrics stream  │  API versioning                     │          └───────────────┬────────────────┘
└──────────────────┘                   └──┬──────────┬──────────┬───────────┘                            │
                                            │          │          │                                        │
┌──────────────────┐  REST/JWT              │          │          │  gRPC                       ┌─────────▼─────────┐
│ Beacon.OnCallConsole│◄──────────────────────┘          │          └────────────────────────────►│  Runbook workers    │
│ (Electron, SQLite    │  syncs acks/escalations           │                                        └─────────────────┘
│  offline cache, tray)│  on reconnect                     │
└──────────────────┘                                       │
                                                            │  RabbitMQ / Azure Service Bus
┌──────────────────┐  SignalR                              │
│ Beacon.Admin       │◄──────────────────────────────────────┘
│ (Blazor Server,     │           ┌───────────────────┐
│  runbook/escalation │──────────►│   itsm-bridge        │  (Java Spring Boot, legacy ticketing sync)
│  admin, ApexCharts) │           └───────────────────┘
└──────────────────┘

┌───────────────────┐   public, unauthenticated, cached
│ Beacon.StatusPage    │   "is Beacon's payments API currently healthy?"
└───────────────────┘

┌───────────────────┐         ┌─────────────────────┐        ┌───────────────────┐
│ Beacon.Workers        │────────►│  SQL Server (EF Core)  │       │       Redis           │
│ (scheduled digests,    │         │  + Blob Storage         │       │  cache · alert-dedup   │
│  report gen, LLM        │         │  (reports)               │       │  window · SignalR       │
│  postmortem draft)       │         └─────────────────────┘        │  backplane              │
└───────────────────┘                                              └───────────────────┘

  native/log-parser-shim (C/C++, P/Invoke) ──► sits on the alert-ingestion path inside Beacon.Api, parsing raw webhook log bodies
  tools/alert-simulator (Python)          ──► fires synthetic alert bursts at Beacon.Api for local dev & load testing
```

**Layering inside `Beacon.Api` and `Beacon.IncidentEngine`** — unchanged from the previous draft's reasoning, still Clean Architecture:

```
Domain          → entities, value objects, domain events (IncidentDeclared, IncidentEscalated,
                  AlertStormDetected). No dependencies on anything else.
Application     → use cases (commands/queries: DeclareIncident, AddTimelineEvent, ResolveIncident),
                  interfaces owned here, not in Infrastructure. Depends only on Domain.
Infrastructure  → EF Core, Redis, RabbitMQ, Blob Storage, the ITSM-bridge HTTP client, the Claude
                  API client. Implements Application's interfaces.
Api / Workers   → composition root: DI wiring, minimal API endpoints, middleware, background jobs.
```

The incident timeline itself is modeled as an **append-only event log** (`IncidentEvent` rows: declared, responder-joined, note-added, escalated, runbook-step-run, resolved — each immutable, timestamped, attributed to a user), not a single mutable "incident" row with a status column. That's the direct answer to the "multiple responders acting at once" challenge from Section 1: two responders adding notes concurrently are just two new rows, not a write conflict. The current incident state (severity, status, assigned commander) is a projection computed from that log — a small, deliberately-scoped taste of event sourcing, applied only where it actually solves a real problem, not applied everywhere as a stylistic default.

---

## 5. Major Modules

| Module | Responsibility | Resume/new-tech mix |
|---|---|---|
| `Beacon.Domain` | Incident, AlertSource, EscalationPolicy, Runbook entities; `IncidentEvent` log; domain events | Pure C#, OOP/SOLID |
| `Beacon.Application` | Use cases: ingest alert, declare incident, add timeline event, escalate, resolve, draft postmortem | CQRS/MediatR, scoped to `Beacon.IncidentEngine` only |
| `Beacon.Infrastructure` | EF Core, Redis cache-aside + dedup, RabbitMQ pub/sub, Blob storage, Claude API client, ITSM-bridge client | EF Core, Redis, Service Bus (Table B) |
| `Beacon.Api` | Minimal API endpoints, MVC controller for the status page, JWT auth, raw-WebSocket metrics hub, rate limiting, versioning | ASP.NET Core (resume) + JWT + Minimal APIs (Table C) |
| `Beacon.IncidentEngine` | Alert-storm dedup, timeline projection, escalation evaluation | New CQRS/MediatR usage, scoped deliberately |
| `Beacon.RunbookEngine` | Executes versioned remediation steps (simulated), gRPC server | gRPC (Table B) |
| `Beacon.Workers` | Scheduled digests, report generation, LLM postmortem-draft job | Background services |
| `native/log-parser-shim` | High-throughput raw log-line parsing on the ingestion path | C/C++ (resume) + P/Invoke technique |
| `itsm-bridge` | Java Spring Boot service syncing incidents to a simulated legacy ticketing system | Java (resume), the one deliberate service boundary |
| `Beacon.WarRoom` | React+MobX real-time incident dashboard, Chart.js metrics | Resume FE stack |
| `Beacon.OnCallConsole` | Electron desktop app, SQLite offline queue, tray/native notifications | Resume stack |
| `Beacon.Admin` | Blazor Server admin: runbooks, escalation policies, service catalog, ApexCharts analytics | Resume (Blazor) + SignalR (Table B, contrasted with raw WebSockets) |
| `Beacon.StatusPage` | Public MVC status page | Resume (MVC) |
| `tools/alert-simulator` | Python synthetic alert generator | Resume (Python) |

---

## 6. Development Phases (summary)

| # | Phase | Objective |
|---|---|---|
| 0 | Architecture (this doc) | Agree on design before touching code |
| 1 | Domain + Application core | TDD the incident event log and dedup logic, zero I/O |
| 2 | Data layer | EF Core + SQL Server, migrations, Alert/Incident CRUD-adjacent endpoints |
| 3 | Alert ingestion | Webhook endpoint, RabbitMQ queue, Redis dedup window |
| 4 | Incident orchestration | MediatR/CQRS, timeline projection, escalation evaluation |
| 5 | Real-time war room | Raw WebSocket metrics stream + SignalR presence/chat |
| 6 | Runbook engine | gRPC service, versioned steps, simulated execution |
| 7 | Reporting | EPPlus/PDFsharp, Blob Storage |
| 8 | AI integration | Claude API postmortem-draft endpoint + Polly resilience |
| 9 | Auth | ASP.NET Core Identity, JWT + refresh tokens, RBAC/policy auth |
| 10 | War room frontend | React + MobX + RxJS + Chart.js |
| 11 | On-call console | Electron, SQLite offline queue, native notifications |
| 12 | Admin portal | Blazor Server, ApexCharts, Bootstrap |
| 13 | Status page | ASP.NET Core MVC, public, cached |
| 14 | Polyglot pieces | Java `itsm-bridge`, C/C++ log-parser shim, Python alert-simulator |
| 15 | Testing | xUnit primary + MSTest comparison, Playwright primary + Selenium comparison, Postman/Newman, Testcontainers |
| 16 | Docker + Aspire | Dockerfiles, `docker-compose.yml`, .NET Aspire app-host for local dev |
| 17 | CI/CD | GitHub Actions: build → SonarQube → tests → image build → push → deploy |
| 18 | Azure deployment | App Service/AKS, Azure SQL, Key Vault, Service Bus, Monitor |
| 19 | Observability | Serilog, OpenTelemetry, health checks, correlation IDs, "diagnose a slow request" walkthrough |
| 20 | Hardening | Failure-scenario handling, load testing, performance tuning |
| 21 | Docs + interview prep | README, LEARNING_GUIDE.md, per-phase Q&A, "Defend This Architecture" |

Phases 1–4 form the first working vertical slice (ingest an alert → it becomes a declared incident with a real timeline) before Docker, Azure, or the polyglot pieces show up at all — same "prove the core first" ordering as before.

---

## 7. Learning Roadmap

```
Beginner            → .NET Fundamentals (Phase 1: records, nullable ref types, the event-log model)
                     → ASP.NET Core (Phase 2: Minimal APIs vs. controllers, DI, hosting)
                     → Architecture (Phase 1–2: Clean Architecture boundaries, why an append-only log)
                     → Database (Phase 2: EF Core, migrations, indexing an event-log table)
                     → Security (Phase 9: JWT, Identity, RBAC/policy auth)
                     → Distributed Systems (Phase 6, 14: CQRS scoping, gRPC, the one service boundary)
                     → Messaging (Phase 3: RabbitMQ, dedup, idempotent consumers)
                     → Caching (Phase 3–4: Redis cache-aside, TTL-based dedup)
                     → Testing (Phase 15: xUnit vs. MSTest, Playwright vs. Selenium, what NOT to test)
                     → Docker/Aspire (Phase 16)
                     → CI/CD (Phase 17)
                     → Cloud (Phase 18: Azure)
                     → Observability (Phase 19)
                     → Performance (Phase 20)
                     → Production (Phase 20: failure scenarios)
Senior/Architect     → Phase 21: "Defend This Architecture" — why event log over a status column, why
                       CQRS here and not the service catalog, why one service boundary and not ten,
                       how this handles a real alert storm, what breaks first at 10M events/day
```

---

## 8. Final Repository Structure

```
Beacon/
│
├── src/
│   ├── Beacon.Domain/
│   ├── Beacon.Application/
│   ├── Beacon.Infrastructure/
│   ├── Beacon.Api/
│   ├── Beacon.IncidentEngine/
│   ├── Beacon.RunbookEngine/
│   ├── Beacon.Workers/
│   ├── Beacon.StatusPage/                    (ASP.NET Core MVC)
│   │
│   ├── frontend/
│   │   ├── Beacon.WarRoom/                   (React + MobX)
│   │   ├── Beacon.OnCallConsole/              (Electron)
│   │   └── Beacon.Admin/                      (Blazor Server)
│   │
│   ├── native/
│   │   └── log-parser-shim/                   (C/C++, P/Invoke target)
│   │
│   └── itsm-bridge/                           (Java Spring Boot, separate build)
│
├── tools/
│   └── alert-simulator/                        (Python)
│
├── tests/
│   ├── Beacon.UnitTests/                       (xUnit + FluentAssertions + Moq — primary)
│   ├── Beacon.UnitTests.MSTestComparison/       (MSTest — resume parity)
│   ├── Beacon.IntegrationTests/                 (Testcontainers: SQL Server, RabbitMQ, Redis)
│   ├── Beacon.E2ETests/                         (Playwright — primary)
│   ├── Beacon.E2ETests.SeleniumComparison/       (Selenium C# — resume parity)
│   └── Beacon.ArchitectureTests/                 (dependency-direction enforcement)
│
├── docs/
│   ├── architecture.md
│   ├── database.md
│   ├── api.md
│   ├── security.md
│   ├── testing.md
│   ├── deployment.md
│   ├── observability.md
│   ├── troubleshooting.md
│   ├── learning-path.md
│   └── postman/
│       └── Beacon.postman_collection.json
│
├── infrastructure/          (Bicep/Terraform for Azure resources — later phase)
├── deploy/                  (Azure deployment configs, App Service/AKS manifests)
├── scripts/
├── docker/                  (per-service Dockerfiles)
├── aspire/                  (.NET Aspire app-host project — local orchestration)
│
├── docker-compose.yml
├── .gitignore
├── README.md
├── LEARNING_GUIDE.md
├── ARCHITECTURE.md          (this file, promoted to repo root once Phase 1 starts)
└── Beacon.sln
```

---

## 9. Decisions stated plainly, so we don't re-litigate them

- **Modular monolith, one deliberate service boundary** (`itsm-bridge`, Java). Same cost/benefit reasoning as before: microservices cost (network calls, distributed transactions, operational overhead) has to buy you something real (independent scaling/deployment/ownership) — here it mostly wouldn't, except at that one genuine external-system boundary.
- **Event log over a mutable status row** for incidents specifically — because the actual problem (concurrent responders, "what happened when" being the whole point of a postmortem) is exactly what an append-only log solves and a status column doesn't. Not applied to the Service Catalog or User Admin, where a normal mutable row is simply correct and an event log would be needless complexity.
- **CQRS/MediatR scoped to `Beacon.IncidentEngine` only**, for the same reason as before — it earns its place where commands are audit-critical and reads (dashboards) have a very different shape from writes (timeline events); everywhere else it would be exactly the "add complexity nobody asked for" your original brief explicitly warned against.
- **No blanket Repository/Unit-of-Work layer over EF Core.** `DbContext` already is a Unit of Work. A `IEscalationScheduleStore` interface is introduced only where it does real work (unifying a Redis-cached read over a SQL-backed write).
- **Azure over AWS**, per your roadmap doc's explicit reasoning.
- **xUnit/Playwright as primary, MSTest/Selenium kept as parity suites** — per your new instruction that better-than-resume is fine, while still leaving you able to speak to the resume tools directly since they're demonstrated, not abandoned.

---

## Next step

No code yet, per your instructions. If this design holds up, say so and we start **Phase 1**: the Domain + Application core — the incident event log and alert-dedup logic, test-driven, zero infrastructure.
