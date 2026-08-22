// Reusable Container App module — five near-identical resources (beacon-api, beacon-admin,
// beacon-statuspage, beacon-warroom, itsm-bridge) would otherwise mean five copies of the same
// ~40-line block differing only in name/image/port/env vars. A module used via a `for` loop in
// main.bicep is the actual point of Bicep modules, not just directory organization.

@description('Name of the Container App, e.g. beacon-api. Also becomes its internal DNS name within the Container Apps Environment.')
param name string

param location string

@description('Resource ID of the Container Apps managed environment this app runs in.')
param environmentId string

@description('Login server (e.g. beacondevacr1234.azurecr.io) of the Container Registry to pull images from. The registry\'s resource ID itself isn\'t needed in this module — the AcrPull role assignment granting this app\'s identity pull access is done in main.bicep, scoped directly against the registry resource there.')
param registryLoginServer string

@description('Image name (without registry prefix or tag) — e.g. beacon-api. Tag is always :latest here; a real pipeline would pass the build\'s commit SHA instead.')
param imageName string

param targetPort int

@description('External ingress (a real public FQDN) vs. internal-only (still reachable by other apps in the same environment via http://<name>, just not from the public internet). itsm-bridge is the only one of the five that should be internal-only.')
param externalIngress bool = true

param env array = []

@description('Container-App-level secrets (connection strings etc.) — referenced from `env` via secretRef, never inlined as plain env var values. NOT decorated @secure(): Bicep\'s @secure() only supports `string`/`object` targets, not `array` (a real, if slightly obscure, language limitation - discovered via `az bicep build` failing with BCP124 while first writing this). What actually keeps these values from surfacing in Azure Portal/CLI output is Container Apps\' own secrets mechanism (server-side encryption at rest for anything in this array) - the underlying values still originate from `sqlAdminPassword`/`claudeApiKey`, which main.bicep does mark @secure() at the one place Bicep actually supports it.')
param secrets array = []

param cpuCores string = '0.5'
param memory string = '1Gi'

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: name
  location: location
  identity: {
    // System-assigned identity + an AcrPull role assignment (granted in main.bicep) is the
    // correct way to let a Container App pull from a private registry — no admin username/
    // password credential to leak, unlike ACR's "admin user" convenience toggle.
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environmentId
    configuration: {
      ingress: {
        external: externalIngress
        targetPort: targetPort
        transport: 'auto'
      }
      registries: [
        {
          server: registryLoginServer
          identity: 'system'
        }
      ]
      secrets: secrets
    }
    template: {
      containers: [
        {
          name: name
          image: '${registryLoginServer}/${imageName}:latest'
          resources: {
            cpu: json(cpuCores)
            memory: memory
          }
          env: env
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

@description('Grants this Container App\'s managed identity permission to pull images from the registry — resolved in main.bicep since the role assignment needs both the identity\'s principal ID and the registry\'s resource ID.')
output principalId string = containerApp.identity.principalId

output fqdn string = externalIngress ? containerApp.properties.configuration.ingress.fqdn : '${name}.internal'
