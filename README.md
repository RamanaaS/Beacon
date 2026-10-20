# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**All 20 phases done.** Full incident lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth
and role-based authorization, CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket
telemetry, a MediatR pub/sub bridge to SignalR for real-time incident broadcasts, four separate
frontends against the same API (React+MobX `Beacon.WarRoom`, Electron `Beacon.OnCallConsole`,
Blazor Server `Beacon.Admin`, MVC `Beacon.StatusPage`), three polyglot pieces (Java `itsm-bridge`,
native C `log-parser-shim` via P/Invoke, Python `alert-simulator`), a testing-formalization pass
(MSTest comparison, Selenium comparison, Postman/Newman), containerization (five Dockerfiles +
`docker-compose.yml`, a `.NET Aspire` AppHost), three GitHub Actions workflows plus a PR template,
Bicep infrastructure for an Azure Container Apps target, observability (Serilog structured
logging, a three-tier `/health`/`/alive`/`/health/detail` split so a real Redis outage degrades
gracefully instead of pulling a working instance out of rotation, and correlation IDs tying every
log line for one request to the same ID a trace would show), and a hardening pass driven by live
chaos testing: a genuine crash bug found and fixed (an unhandled exception in a background service
used to take the *entire* API process down over a transient database blip, not just fail one
request — confirmed live by forcing the database offline mid-run and watching the process die, then
re-verified surviving the identical test after the fix), a catch-all exception handler closing an
information-disclosure gap for unexpected errors, and per-IP rate limiting on the two endpoints
with no auth wall. 107/107 core .NET tests passing (79 backend + 24 MSTest + 4 health-check tests),
8/8 Java tests, 21/21 Postman requests via Newman. Docker's daemon has never come up on this
machine across the whole project — not even the process runs — so Testcontainers and actually
starting any container stay the one explicitly, honestly blocked area locally; the `docker.yml`
workflow would genuinely work once pushed, since GitHub's own runners have real Docker.
See [LEARNING_GUIDE.md](LEARNING_GUIDE.md) for what each phase teaches, concept by concept,
[docs/observability/diagnosing-a-slow-request.md](docs/observability/diagnosing-a-slow-request.md)
for a real (not hypothetical) worked example of using this project's own logging/correlation-ID
setup to find and fix an actual bug, and [docs/interview-prep.md](docs/interview-prep.md) for a
"defend this architecture" pass — including a genuinely real, checkable scalability gap this doc
surfaced while being written, not a hypothetical one.

## Prerequisites

- .NET 9 SDK
- SQL Server (LocalDB is fine — `(localdb)\MSSQLLocalDB`)
- Node.js LTS (for `src/frontend/Beacon.WarRoom`, `Beacon.OnCallConsole`, and `Beacon.Admin`'s
  Playwright-based verification scripts)
- Java 21 + the bundled Maven wrapper (`src/itsm-bridge/mvnw.cmd`) for `src/itsm-bridge`
- gcc (MinGW-w64/UCRT) only if you want to rebuild `native/log-parser-shim` yourself — the built
  `dist/log_parser_shim.dll` is committed, so this isn't required just to run the solution
- Python 3 for `tools/alert-simulator`
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) + its Bicep extension
  (`az bicep install`) only if you want to compile/validate `infrastructure/main.bicep` yourself —
  not required to run anything else in this repo

## Free public demo

| Piece | Where | URL |
|---|---|---|
| War room (React) | GitHub Pages, `.github/workflows/pages.yml` | https://ramanaas.github.io/Beacon/ |
| Beacon.Api | Render free web service (Docker), `render.yaml` | https://ramanaas-beacon-api.onrender.com/swagger |
| Status page | Render free web service (Docker), `render.yaml` | https://ramanaas-beacon-status.onrender.com |
| Database | Neon PostgreSQL free tier | — |

Everything here is on a free tier with no card on file. The API sleeps after about 15 idle
minutes, so the first request after that takes around a minute. Sign up in the war room with the
Responder role to declare and resolve incidents; the demo doesn't allow self-signup as Admin.

What makes it run there, all opt-in through configuration so local dev and the tests are unchanged:

- `Database:Provider=Postgres` switches EF Core to Npgsql. PostgreSQL migrations live in
  `src/Beacon.Infrastructure.Postgres`, because SQL Server and PostgreSQL histories can't share
  one migrations assembly. `Database:MigrateOnStartup=true` applies them and seeds roles on boot.
- `Cors:AllowedOrigins`, `ForwardedHeaders:Enabled` (real client IPs for the rate limiter behind
  Render's proxy), `Swagger:Enabled`, `Auth:SelfSignupRoles` and `ItsmBridge:Enabled=false`.
- The API image compiles a Linux `liblog_parser_shim.so` from the same C source, and installs
  Liberation Sans so PDF reports work without Windows fonts.

Setup, once: create a Neon project in the Singapore region and copy its .NET connection string
(turn connection pooling off). In Render, choose New, then Blueprint, select this repo, and paste
that string when asked. In GitHub, set Settings > Pages > Source to "GitHub Actions".

To add a PostgreSQL migration after a model change:

```
dotnet ef migrations add <Name> --project src/Beacon.Infrastructure.Postgres --startup-project src/Beacon.Infrastructure.Postgres --output-dir Migrations
```

## Deploying to Azure

Written and locally compiled (`az bicep build`) but never deployed — no Azure subscription exists
in this environment. See `infrastructure/main.bicep` and `.github/workflows/deploy.yml` for the
full design (Azure Container Apps, ACR, Azure SQL, Azure Cache for Redis, Key Vault, Log
Analytics/App Insights) and LEARNING_GUIDE.md's Phase 17 entry for what was and wasn't verifiable
without real credentials.

```
az group create --name beacon-dev-rg --location eastus
az deployment group create \
  --resource-group beacon-dev-rg \
  --template-file infrastructure/main.bicep \
  --parameters environmentName=dev sqlAdminPassword=<your-password>
```

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

## Running everything at once (Docker, or .NET Aspire)

Neither path has been run end-to-end on the machine this was built on — Docker's daemon has never
come up here (see LEARNING_GUIDE.md's Phase 3/13/15 entries). Both are written and, for the Aspire
path, partially verified (the dashboard itself runs and correctly shows every container-backed
resource blocked on Docker).

```
# Everything via Docker Compose
docker compose up --build

# Everything via .NET Aspire (needs the Aspire CLI: https://aspire.dev, and the same "no Docker
# means the sql/redis container resources can't actually come up" caveat)
cd src\Beacon.AppHost
dotnet run
# Dashboard at the printed https://localhost:<port> URL
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
