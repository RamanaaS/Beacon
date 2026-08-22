# Build context is the repo root — see src/Beacon.Api/Dockerfile's comment for why (this project
# only actually depends on the ASP.NET Core shared framework, no cross-project references, but the
# same repo-root-context convention is kept for consistency across every Dockerfile here).

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/frontend/Beacon.Admin/Beacon.Admin.csproj src/frontend/Beacon.Admin/
RUN dotnet restore src/frontend/Beacon.Admin/Beacon.Admin.csproj

COPY src/frontend/Beacon.Admin/ src/frontend/Beacon.Admin/
RUN dotnet publish src/frontend/Beacon.Admin/Beacon.Admin.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# BeaconApi:BaseUrl (appsettings.json) defaults to http://localhost:5252, which is meaningless
# inside a container — override it via the BeaconApi__BaseUrl env var pointing at the compose
# service name instead (docker-compose.yml sets this to http://beacon-api:8080).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Beacon.Admin.dll"]
