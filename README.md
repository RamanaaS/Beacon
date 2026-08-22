# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 17 done.** Full incident lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth
and role-based authorization, CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket
telemetry, a MediatR pub/sub bridge to SignalR for real-time incident broadcasts, four separate
frontends against the same API (React+MobX `Beacon.WarRoom`, Electron `Beacon.OnCallConsole`,
Blazor Server `Beacon.Admin`, MVC `Beacon.StatusPage`), three polyglot pieces (Java `itsm-bridge`,
native C `log-parser-shim` via P/Invoke, Python `alert-simulator`), a testing-formalization pass
(MSTest comparison, Selenium comparison, Postman/Newman alongside the primary xUnit/Playwright
suites), containerization (five Dockerfiles + `docker-compose.yml`, plus a `.NET Aspire` AppHost
whose dashboard was actually run and screenshotted), three GitHub Actions workflows plus a PR
template (CI, a Docker/compose smoke test, and an Azure deploy pipeline — OIDC login, immutable
SHA-tagged images, correctly sequenced against ACR's own chicken-and-egg bootstrap problem), and
Bicep infrastructure for an Azure Container Apps deployment target (`infrastructure/`, compiled
clean via `az bicep build` — no Azure subscription exists in this environment to deploy it
against). 104/104 core .NET tests passing (79 backend + 24 MSTest + 1 Selenium), 8/8 Java tests,
21/21 Postman requests via Newman. Docker's daemon has never come up on this machine across the
whole project — not even the process runs — so Testcontainers and actually starting any container
stay the one explicitly, honestly blocked area locally; the `docker.yml` workflow would genuinely
work once pushed, since GitHub's own runners have real Docker.
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
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) + its Bicep extension
  (`az bicep install`) only if you want to compile/validate `infrastructure/main.bicep` yourself —
  not required to run anything else in this repo

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
