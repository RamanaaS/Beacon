// .NET Aspire AppHost (Phase 15) — orchestrates local dev the .NET-native way, as a companion to
// docker-compose.yml (deploy.md/docker-compose.yml) rather than a replacement for it: this file
// is what a developer on this project would actually run locally (`dotnet run` from this
// project); docker-compose.yml is what a CI pipeline or a non-.NET teammate would reach for.
//
// SQL Server and Redis are declared as real Aspire container resources — Aspire builds/starts
// them via Docker exactly like docker-compose would, just from C# instead of YAML. That means
// this AppHost genuinely cannot run end-to-end on this machine: Docker's daemon has never come up
// across this whole project (see LEARNING_GUIDE.md's Phase 3 and Phase 13 entries). What's below
// is written and *compiles* against the real project references, but hasn't been run/verified —
// the same honest "written, not verified" framing as Phase 8's un-tested live Claude call.
var builder = DistributedApplication.CreateBuilder(args);

var sql = builder.AddSqlServer("sql")
    .WithLifetime(ContainerLifetime.Persistent)
    .AddDatabase("BeaconDb");

var redis = builder.AddRedis("Redis")
    .WithLifetime(ContainerLifetime.Persistent);

// Beacon.Api's own appsettings.json Redis connection string carries resilience-relevant
// parameters (abortConnect=false, connectTimeout=1000, syncTimeout=1000 — see Phase 13's
// RedisRecentAlertsStore fix) that Aspire's injected connection string wouldn't know to include;
// running under this AppHost would need those folded into ConfigurationOptions in code instead of
// a raw connection string to keep that same fast-fail behavior. Noted, not solved here — solving
// it can't be verified without a working Docker daemon to actually test against anyway.
var api = builder.AddProject<Projects.Beacon_Api>("beacon-api")
    .WithReference(sql)
    .WithReference(redis)
    .WaitFor(sql)
    .WaitFor(redis);

// Beacon.Admin and Beacon.StatusPage both currently read Beacon.Api's URL from their own
// appsettings.json (BeaconApi:BaseUrl) rather than Aspire's service-discovery env vars — .WithReference
// here still gets them correct startup *ordering* via WaitFor, but doesn't yet rewire them onto
// Aspire's dynamic service discovery. That rewiring is a real next iteration, deliberately not
// done now: it would mean changing already-shipped, tested Phase 11/12 code for a benefit that
// can't be verified end-to-end in an environment where the AppHost itself can't fully start.
builder.AddProject<Projects.Beacon_Admin>("beacon-admin")
    .WithReference(api)
    .WaitFor(api);

builder.AddProject<Projects.Beacon_StatusPage>("beacon-statuspage")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
