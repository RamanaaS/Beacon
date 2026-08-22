# Self-contained build context (this folder), unlike the .NET Dockerfiles — a Vite SPA has no
# cross-project references to reach outside itself for.
#
# Beacon.WarRoom's API base URL (src/api/client.ts) is hardcoded to http://localhost:5252, not
# read from a Vite env var at build time — a real, deliberate-not-to-touch limitation (changing it
# would mean editing already-shipped, tested Phase 9 code for a Docker-specific benefit that can't
# be verified end-to-end in this environment anyway; see LEARNING_GUIDE.md's Phase 15 entry). That
# hardcoded URL still works from docker-compose as long as Beacon.Api's container publishes port
# 5252 on the host — the browser itself reaches localhost:5252 directly, not through the Docker
# network, since the SPA runs in the *browser*, not inside a container. docker-compose.yml keeps
# that port mapping specifically so this keeps working without any code change.

FROM node:22-alpine AS build
WORKDIR /src

COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine AS runtime
COPY --from=build /src/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
