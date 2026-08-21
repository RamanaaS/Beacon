# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 12 done.** Full incident lifecycle (declare/acknowledge/resolve/escalate) behind JWT auth
and role-based authorization, CQRS-orchestrated, Excel/PDF/AI-postmortem reporting, live WebSocket
telemetry, a MediatR pub/sub bridge to SignalR for real-time incident broadcasts, and four separate
frontends against the same API: a React + MobX war-room dashboard (`Beacon.WarRoom`), an Electron
on-call console with an offline write queue (`Beacon.OnCallConsole`), a Blazor Server admin portal
for user/role management and analytics (`Beacon.Admin`), and a public, unauthenticated, cached
ASP.NET Core MVC status page (`Beacon.StatusPage`). 73/73 backend tests passing, plus
Playwright-verified end-to-end browser runs of all four frontends.
See [LEARNING_GUIDE.md](LEARNING_GUIDE.md) for what each phase teaches, concept by concept.

## Prerequisites

- .NET 9 SDK
- SQL Server (LocalDB is fine — `(localdb)\MSSQLLocalDB`)
- Node.js LTS (for `src/frontend/Beacon.WarRoom`, `Beacon.OnCallConsole`, and `Beacon.Admin`'s
  Playwright-based verification scripts)

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
```

## Running the tests

```
# Backend
dotnet test

# Frontend type-check + build
cd src\frontend\Beacon.WarRoom
npm run build
```
