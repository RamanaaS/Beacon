# Beacon

Incident command and SRE operations platform — the tool a team opens the moment something breaks in production.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full design, skill mapping, and phase plan.

## Status

**Phase 2 done.** Domain + Application core, EF Core data layer (SQL Server), and the first
`Beacon.Api` endpoints (declare / get / acknowledge / resolve an incident), 19/19 tests passing.
See [LEARNING_GUIDE.md](LEARNING_GUIDE.md) for what each phase teaches, concept by concept.

## Prerequisites

- .NET 9 SDK
- SQL Server (LocalDB is fine — `(localdb)\MSSQLLocalDB`)

## Running it

```
# Apply the database migration (one-time, or after pulling new migrations)
cd src\Beacon.Infrastructure
dotnet ef database update

# Run the API
cd ..\Beacon.Api
dotnet run
# Swagger UI at the printed http://localhost:<port>/swagger
```

## Running the tests

```
dotnet test
```
