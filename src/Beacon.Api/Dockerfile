# Build context is the repo root (see docker-compose.yml: `context: .`, `dockerfile: src/Beacon.Api/Dockerfile`)
# because Beacon.Api's own project references (Beacon.Application, Beacon.Domain,
# Beacon.Infrastructure, Beacon.ServiceDefaults) live elsewhere in the solution — a Docker build
# context scoped to just this folder couldn't see them at all.

# Linux build of native/log-parser-shim. The committed dist/log_parser_shim.dll is a Windows PE
# (MinGW, build.ps1) and can't load in the Linux runtime image below. log_parser.h already
# compiles LOGPARSER_API to nothing off Windows, so the same source builds a shared object
# as-is. .NET's DllImport("log_parser_shim") probes liblog_parser_shim.so on Linux.
FROM gcc:13 AS native
WORKDIR /native
COPY native/log-parser-shim/log_parser.c native/log-parser-shim/log_parser.h ./
RUN gcc -shared -fPIC -O2 -Wall -Wextra -o liblog_parser_shim.so log_parser.c

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first and restore before copying the rest of the source, so Docker's layer
# cache only invalidates the (slow) restore step when a .csproj actually changes, not on every
# code edit.
COPY src/Beacon.Api/Beacon.Api.csproj src/Beacon.Api/
COPY src/Beacon.Application/Beacon.Application.csproj src/Beacon.Application/
COPY src/Beacon.Domain/Beacon.Domain.csproj src/Beacon.Domain/
COPY src/Beacon.Infrastructure/Beacon.Infrastructure.csproj src/Beacon.Infrastructure/
COPY src/Beacon.Infrastructure.Postgres/Beacon.Infrastructure.Postgres.csproj src/Beacon.Infrastructure.Postgres/
COPY src/Beacon.ServiceDefaults/Beacon.ServiceDefaults.csproj src/Beacon.ServiceDefaults/
RUN dotnet restore src/Beacon.Api/Beacon.Api.csproj

COPY src/Beacon.Api/ src/Beacon.Api/
COPY src/Beacon.Application/ src/Beacon.Application/
COPY src/Beacon.Domain/ src/Beacon.Domain/
COPY src/Beacon.Infrastructure/ src/Beacon.Infrastructure/
COPY src/Beacon.Infrastructure.Postgres/ src/Beacon.Infrastructure.Postgres/
COPY src/Beacon.ServiceDefaults/ src/Beacon.ServiceDefaults/
# native/log-parser-shim's committed dist/ output (Phase 13) — Beacon.Infrastructure.csproj's
# Content reference expects it at this relative path.
COPY native/log-parser-shim/dist/ native/log-parser-shim/dist/

RUN dotnet publish src/Beacon.Api/Beacon.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Liberation Sans for PDF reports (LinuxFontResolver): metric-compatible with Arial, which is
# what WindowsFontResolver uses on Windows.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fonts-liberation \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
COPY --from=native /native/liblog_parser_shim.so .

# Hosting platforms that assign the listening port through $PORT (Render, Koyeb, ...) set it at
# run time; everywhere else this falls back to 8080, which docker-compose.yml publishes.
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} exec dotnet Beacon.Api.dll"]
