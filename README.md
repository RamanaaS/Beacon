# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 14 done** (Testcontainers excepted — blocked on Docker, see below). Full incident
lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth and role-based authorization,
CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket telemetry, a MediatR pub/sub
bridge to SignalR for real-time incident broadcasts, four separate frontends against the same API
(React+MobX `Beacon.WarRoom`, Electron `Beacon.OnCallConsole`, Blazor Server `Beacon.Admin`, MVC
`Beacon.StatusPage`), three polyglot pieces (Java `itsm-bridge`, native C `log-parser-shim` via
P/Invoke, Python `alert-simulator`), and a testing-formalization pass adding an MSTest comparison
suite, a Selenium comparison E2E suite, and a Postman/Newman API collection alongside the primary
xUnit/Playwright suites. 79/79 core backend tests passing (~18s), plus 24/24 MSTest, 1/1 Selenium,
8/8 Java, and 21/21 Postman requests via Newman. Docker's daemon has never come up on this machine
across the whole project, so Testcontainers and Phase 15's Docker/Aspire work are the one
explicitly blocked area — everything else in this project runs and is verified live, not just
compiled.
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

## Running the API test collection (Postman/Newman)

```
cd docs\postman
npm install
npx newman run Beacon.postman_collection.json
# needs Beacon.Api running at http://localhost:5252
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

# Selenium E2E comparison suite — needs Beacon.Api (localhost:5252) and
# Beacon.WarRoom's dev server (localhost:5173) already running
dotnet test tests\Beacon.E2ETests.SeleniumComparison

# Frontend type-check + build
cd src\frontend\Beacon.WarRoom
npm run build
```
