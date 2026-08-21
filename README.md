# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 9 done.** Full incident lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth
and role-based authorization, CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket
telemetry, and now a real React + MobX dashboard (`Beacon.WarRoom`) driving all of it. 58/58 backend
tests passing, plus a Playwright-verified end-to-end browser run of the frontend.
See [LEARNING_GUIDE.md](LEARNING_GUIDE.md) for what each phase teaches, concept by concept.

## Prerequisites

- .NET 9 SDK
- SQL Server (LocalDB is fine — `(localdb)\MSSQLLocalDB`)
- Node.js LTS (for `src/frontend/Beacon.WarRoom`)

## Running it

```
# Apply the database migration (one-time, or after pulling new migrations)
cd src\Beacon.Infrastructure
dotnet ef database update

# Run the API
cd ..\Beacon.Api
dotnet run
# Swagger UI at the printed http://localhost:<port>/swagger

# Run the frontend (separate terminal)
cd ..\..\src\frontend\Beacon.WarRoom
npm install
npm run dev
# http://localhost:5173 — register an account, then log in
```

## Running the tests

```
# Backend
dotnet test

# Frontend type-check + build
cd src\frontend\Beacon.WarRoom
npm run build
```
