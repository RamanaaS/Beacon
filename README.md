# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 13 done.** Full incident lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth
and role-based authorization, CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket
telemetry, a MediatR pub/sub bridge to SignalR for real-time incident broadcasts, four separate
frontends against the same API (React+MobX `Beacon.WarRoom`, Electron `Beacon.OnCallConsole`,
Blazor Server `Beacon.Admin`, MVC `Beacon.StatusPage`), and three polyglot pieces: a Java Spring
Boot service simulating a legacy ITSM ticketing sync (`src/itsm-bridge`), a native C library
parsing raw alert lines via P/Invoke (`native/log-parser-shim`), and a Python synthetic-alert load
generator (`tools/alert-simulator`). Two real Redis/resilience latency bugs found and fixed by
timing live requests, not just trusting a passing test suite. 79/79 backend tests passing (~18s),
8/8 Java tests passing, plus Playwright-verified end-to-end browser runs of all four frontends.
See [LEARNING_GUIDE.md](LEARNING_GUIDE.md) for what each phase teaches, concept by concept.

## Prerequisites

- .NET 9 SDK
- SQL Server (LocalDB is fine — `(localdb)\MSSQLLocalDB`)
- Node.js LTS (for `src/frontend/Beacon.WarRoom`, `Beacon.OnCallConsole`, and `Beacon.Admin`'s
  Playwright-based verification scripts)
- Java 21 + the bundled Maven wrapper (`src/itsm-bridge/mvnw.cmd`) for `src/itsm-bridge`
- gcc (MinGW-w64/UCRT) only if you want to rebuild `native/log-parser-shim` yourself — the built
  `dist/log_parser_shim.dll` is committed, so this isn't required just to run the solution
- Python 3 for `tools/alert-simulator`

## Running it

```
# Apply the database migration (one-time, or after pulling new migrations)
cd src\Beacon.Infrastructure
dotnet ef database update

# Run the API
cd ..\Beacon.Api
dotnet run
# Swagger UI at the printed http://localhost:<port>/swagger

# Run the war-room dashboard (separate terminal)
cd ..\..\src\frontend\Beacon.WarRoom
npm install
npm run dev
# http://localhost:5173 — register an account, then log in

# Run the admin portal (separate terminal)
cd ..\..\src\frontend\Beacon.Admin
dotnet run --urls "http://localhost:5121"
# http://localhost:5121 — register a user with role=Admin via POST /api/auth/register, then log in

# Run the on-call console (separate terminal)
cd ..\..\src\frontend\Beacon.OnCallConsole
npm install
npm start

# Run the public status page (separate terminal)
cd ..\..\src\Beacon.StatusPage
dotnet run --urls "http://localhost:5146"
# http://localhost:5146 — no login needed

# Run the ITSM bridge (separate terminal, optional — incident declare/resolve degrades
# gracefully without it, just logging a warning instead of syncing a ticket)
cd ..\itsm-bridge
.\mvnw.cmd spring-boot:run
# http://localhost:8080/tickets

# Fire synthetic alerts at the running API (separate terminal, optional)
cd ..\..\tools\alert-simulator
python -m venv .venv
.\.venv\Scripts\pip install -r requirements.txt
.\.venv\Scripts\python alert_simulator.py --count 20 --rate 5
.\.venv\Scripts\python alert_simulator.py --dedup-demo
```

## Running the tests

```
# Backend (.NET)
dotnet test

# itsm-bridge (Java)
cd src\itsm-bridge
.\mvnw.cmd test

# log-parser-shim (C) — also runs the throughput benchmark
cd native\log-parser-shim
.\build.ps1
.\dist\test_log_parser.exe

# Frontend type-check + build
cd src\frontend\Beacon.WarRoom
npm run build
```
