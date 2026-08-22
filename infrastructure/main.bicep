// Phase 17 â€” deployment target for Beacon, written and locally validated (`az bicep build`,
// `az deployment group validate`/`what-if` where those don't themselves require real credentials)
// but never actually deployed: this environment has no real Azure subscription or credentials.
// See LEARNING_GUIDE.md's Phase 17 entry for exactly what was and wasn't checkable without one.
//
// Deliberately targets Azure Container Apps, not the "App Service/AKS" ARCHITECTURE.md originally
// named â€” the same kind of considered, documented deviation as every other Table C choice in this
// project. Container Apps is the direct, natural next step from Phase 15's Docker images (the
// same five images docker-compose.yml already builds deploy here unmodified), gets a managed
// Kubernetes-grade platform (autoscale, revisions, internal service discovery by app name â€” the
// same "call it by name" pattern docker-compose.yml already uses) without hand-rolling AKS
// cluster/node-pool/ingress-controller config that this project's actual scale doesn't justify.
// App Service would need a different deployment story per app (zip-deploy vs. container), and AKS
// is real, substantial extra operational surface (cluster upgrades, node pools, an ingress
// controller) that would be pure ceremony at Beacon's scale.
//
// RabbitMQ/Azure Service Bus, named in ARCHITECTURE.md's tech list, is deliberately absent here
// too â€” same reasoning as docker-compose.yml's omission: no code in this project actually uses a
// message queue, so provisioning one would be paying for and operating infrastructure nothing
// calls.

targetScope = 'resourceGroup'

@description('Short environment name, used in resource naming (e.g. dev, staging).')
param environmentName string = 'dev'

param location string = resourceGroup().location

@description('SQL Server administrator password. In a real pipeline this comes from a GitHub Actions secret, never committed â€” passed here as a parameter with no default specifically so a plain `az deployment group create` without one fails loudly instead of deploying with something guessable.')
@secure()
param sqlAdminPassword string

@description('Optional: Claude API key for Beacon.Api\'s postmortem-draft endpoint (Phase 8). Left empty by default â€” the app already degrades that one endpoint to a 503 without it (ExternalServiceNotConfiguredException), the same graceful-absence behavior as local dev.')
@secure()
param claudeApiKey string = ''

var uniqueSuffix = uniqueString(resourceGroup().id)
var prefix = 'beacon-${environmentName}'
var sqlAdminLogin = 'beaconadmin'

// ---- Observability (ties into Phase 18) ----

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---- Container Registry ----

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: replace('${prefix}acr${uniqueSuffix}', '-', '')
  location: location
  sku: { name: 'Basic' }
  properties: {
    // No admin username/password credential â€” each Container App authenticates to pull images
    // via its own system-assigned managed identity + an AcrPull role assignment below instead.
    adminUserEnabled: false
  }
}

// ---- Data stores ----

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${uniqueSuffix}'
  location: location
  properties: {
    administratorLogin: sqlAdminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: 'BeaconDb'
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    maxSizeBytes: 2147483648
  }
}

// Container Apps' outbound IPs aren't static/predictable without a VNet integration this demo
// scope doesn't need â€” AllowAllWindowsAzureIps (0.0.0.0-0.0.0.0, Azure's own documented
// convention for this exact rule name/range) is the pragmatic, honestly-labeled trade for "any
// Azure-hosted caller can reach this," not a real production network-isolation posture.
resource sqlFirewallAllowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${prefix}-redis-${uniqueSuffix}'
  location: location
  properties: {
    sku: {
      name: 'Basic'
      family: 'C'
      capacity: 0
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
  }
}

// ---- Secrets ----

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: '${prefix}-kv-${uniqueSuffix}'
  location: location
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=BeaconDb;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};Encrypt=true;TrustServerCertificate=false;'
// Redis connection-string params mirror Phase 13's tuned local values (appsettings.json) so the
// same fast-fail circuit-breaker behavior applies here too, not just in local dev.
var redisConnectionString = '${redis.properties.hostName}:6380,password=${redis.listKeys().primaryKey},ssl=True,abortConnect=false,connectTimeout=1000,connectRetry=1,syncTimeout=1000'

// Key Vault holds the source-of-truth copies (what a deployment operator/pipeline would consult);
// the Container Apps below get their own copies as Container-App-level secrets (see
// modules/container-app.bicep) rather than a live Key Vault reference at container startup â€” the
// simpler, still-genuinely-secret-not-plaintext option for this project's scope. Wiring
// Key-Vault-reference-at-runtime (each app's managed identity granted "Key Vault Secrets User",
// referencing keyVaultUrl directly in the container secret) is real, correct, and a natural next
// iteration, not done here to avoid the deployment-ordering complexity of a role assignment that
// has to exist before the container that needs it does.
resource sqlSecretInVault 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'sql-connection-string'
  properties: { value: sqlConnectionString }
}

resource redisSecretInVault 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'redis-connection-string'
  properties: { value: redisConnectionString }
}

resource claudeKeySecretInVault 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = if (!empty(claudeApiKey)) {
  parent: keyVault
  name: 'claude-api-key'
  properties: { value: claudeApiKey }
}

// ---- Container Apps ----

resource containerAppsEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// itsm-bridge deploys first (no dependency on the others) and internal-only â€” the same
// "unauthenticated because it's not reachable from outside the environment anyway" reasoning
// ItsmBridgeSettings.cs and docker-compose.yml already documented for the local/container case.
module itsmBridgeApp 'modules/container-app.bicep' = {
  name: 'itsm-bridge-app'
  params: {
    name: 'itsm-bridge'
    location: location
    environmentId: containerAppsEnv.id
    registryLoginServer: registry.properties.loginServer
    imageName: 'itsm-bridge'
    targetPort: 8080
    externalIngress: false
  }
}

module apiApp 'modules/container-app.bicep' = {
  name: 'beacon-api-app'
  params: {
    name: 'beacon-api'
    location: location
    environmentId: containerAppsEnv.id
    registryLoginServer: registry.properties.loginServer
    imageName: 'beacon-api'
    targetPort: 8080
    externalIngress: true
    secrets: [
      { name: 'sql-connection-string', value: sqlConnectionString }
      { name: 'redis-connection-string', value: redisConnectionString }
      { name: 'appinsights-connection-string', value: appInsights.properties.ConnectionString }
    ]
    env: concat(
      [
        { name: 'ConnectionStrings__BeaconDb', secretRef: 'sql-connection-string' }
        { name: 'ConnectionStrings__Redis', secretRef: 'redis-connection-string' }
        { name: 'ItsmBridge__BaseUrl', value: 'http://itsm-bridge' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
      ],
      empty(claudeApiKey) ? [] : [{ name: 'Claude__ApiKey', secretRef: 'claude-api-key' }]
    )
  }
  dependsOn: [
    itsmBridgeApp
  ]
}

module adminApp 'modules/container-app.bicep' = {
  name: 'beacon-admin-app'
  params: {
    name: 'beacon-admin'
    location: location
    environmentId: containerAppsEnv.id
    registryLoginServer: registry.properties.loginServer
    imageName: 'beacon-admin'
    targetPort: 8080
    externalIngress: true
    env: [
      { name: 'BeaconApi__BaseUrl', value: 'http://beacon-api' }
    ]
  }
  dependsOn: [
    apiApp
  ]
}

module statusPageApp 'modules/container-app.bicep' = {
  name: 'beacon-statuspage-app'
  params: {
    name: 'beacon-statuspage'
    location: location
    environmentId: containerAppsEnv.id
    registryLoginServer: registry.properties.loginServer
    imageName: 'beacon-statuspage'
    targetPort: 8080
    externalIngress: true
    env: [
      { name: 'BeaconApi__BaseUrl', value: 'http://beacon-api' }
    ]
  }
  dependsOn: [
    apiApp
  ]
}

// Beacon.WarRoom's browser-side API base URL is hardcoded to http://localhost:5252 (Phase 15's
// Dockerfile comment explains why that was left alone) â€” that assumption holds for docker-compose
// (browser and beacon-api share the host's localhost) but does NOT hold once beacon-api is a real
// Container App with its own public FQDN, not localhost. Deployed as-is here anyway for
// completeness of the infrastructure definition, with this gap called out explicitly rather than
// silently implying the deployed WarRoom would actually work end-to-end: fixing it for real means
// the same client.ts change flagged as deliberately not done in Phase 15, now for a second,
// stronger reason.
module warRoomApp 'modules/container-app.bicep' = {
  name: 'beacon-warroom-app'
  params: {
    name: 'beacon-warroom'
    location: location
    environmentId: containerAppsEnv.id
    registryLoginServer: registry.properties.loginServer
    imageName: 'beacon-warroom'
    targetPort: 80
    externalIngress: true
  }
  dependsOn: [
    apiApp
  ]
}

// ---- AcrPull role assignments â€” one per Container App's managed identity ----

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d' // built-in "AcrPull" role, same GUID in every Azure tenant

resource itsmBridgeAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, itsmBridgeApp.name, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: itsmBridgeApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource apiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, apiApp.name, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: apiApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource adminAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, adminApp.name, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: adminApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource statusPageAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, statusPageApp.name, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: statusPageApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

resource warRoomAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, warRoomApp.name, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: warRoomApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output apiUrl string = apiApp.outputs.fqdn
output adminUrl string = adminApp.outputs.fqdn
output statusPageUrl string = statusPageApp.outputs.fqdn
output warRoomUrl string = warRoomApp.outputs.fqdn
output registryLoginServer string = registry.properties.loginServer
