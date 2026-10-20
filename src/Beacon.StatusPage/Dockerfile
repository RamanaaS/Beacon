# Build context is the repo root — see src/Beacon.Api/Dockerfile's comment for why.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Beacon.StatusPage/Beacon.StatusPage.csproj src/Beacon.StatusPage/
RUN dotnet restore src/Beacon.StatusPage/Beacon.StatusPage.csproj

COPY src/Beacon.StatusPage/ src/Beacon.StatusPage/
RUN dotnet publish src/Beacon.StatusPage/Beacon.StatusPage.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Same BeaconApi:BaseUrl override note as Beacon.Admin's Dockerfile — set via
# BeaconApi__BaseUrl in docker-compose.yml, not baked in here.
# $PORT is set by hosting platforms such as Render; 8080 otherwise (see Beacon.Api's Dockerfile).
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT} exec dotnet Beacon.StatusPage.dll"]
