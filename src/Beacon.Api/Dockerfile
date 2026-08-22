# Build context is the repo root (see docker-compose.yml: `context: .`, `dockerfile: src/Beacon.Api/Dockerfile`)
# because Beacon.Api's own project references (Beacon.Application, Beacon.Domain,
# Beacon.Infrastructure, Beacon.ServiceDefaults) live elsewhere in the solution — a Docker build
# context scoped to just this folder couldn't see them at all.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first and restore before copying the rest of the source, so Docker's layer
# cache only invalidates the (slow) restore step when a .csproj actually changes, not on every
# code edit.
COPY src/Beacon.Api/Beacon.Api.csproj src/Beacon.Api/
COPY src/Beacon.Application/Beacon.Application.csproj src/Beacon.Application/
COPY src/Beacon.Domain/Beacon.Domain.csproj src/Beacon.Domain/
COPY src/Beacon.Infrastructure/Beacon.Infrastructure.csproj src/Beacon.Infrastructure/
COPY src/Beacon.ServiceDefaults/Beacon.ServiceDefaults.csproj src/Beacon.ServiceDefaults/
RUN dotnet restore src/Beacon.Api/Beacon.Api.csproj

COPY src/Beacon.Api/ src/Beacon.Api/
COPY src/Beacon.Application/ src/Beacon.Application/
COPY src/Beacon.Domain/ src/Beacon.Domain/
COPY src/Beacon.Infrastructure/ src/Beacon.Infrastructure/
COPY src/Beacon.ServiceDefaults/ src/Beacon.ServiceDefaults/
# native/log-parser-shim's committed dist/ output (Phase 13) — Beacon.Infrastructure.csproj's
# Content reference expects it at this relative path.
COPY native/log-parser-shim/dist/ native/log-parser-shim/dist/

RUN dotnet publish src/Beacon.Api/Beacon.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# log_parser_shim.dll (Phase 13) is a Windows PE built via MinGW — it will NOT load on this
# Linux-based runtime image. Alert ingestion's /api/alerts/ingest-raw path would need a Linux
# build of the native shim (a real, honest gap: Phase 13's build.ps1 only targets Windows, and
# there's no Docker daemon available in this environment to actually build/test a Linux .so
# counterpart against). Every other endpoint is unaffected.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Beacon.Api.dll"]
