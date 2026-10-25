// Container Apps environment (consumption) with api, worker and web, plus the migrate job (PLAN.md §15). Every app runs
// as the user-assigned identity: it pulls from the registry, resolves Key Vault references, wraps document keys and
// reads and writes blobs with it. Only web has public ingress; the dashboard's server calls the API inside the
// environment.

param location string
param tags object
param namePrefix string
param identityId string
param identityClientId string
param workspaceName string
param appInsightsName string
param registryServer string

@description('Image tag for api, worker and web. The deploy workflow passes the running tag so the apps only move after the migrate job.')
param appImageTag string

@description('Image tag for the migrate job: the release being deployed.')
param migrateImageTag string

param vaultUri string
param masterKeyUri string
param blobEndpoint string
param blobContainerName string
param seedAdminEmail string

@description('Names of optional Key Vault secrets that exist (see optionalSecretEnvironment in main.bicep).')
param optionalSecrets array

@description('Plain (non-secret) settings passed to api, worker and migrate as environment variables, e.g. Notifications__Email__SmtpHost.')
param appSettings object

param apiMinReplicas int
param webMinReplicas int

@description('Expose the Worker\'s Hangfire dashboard (/hangfire, Admin sign-in) on a public address.')
param exposeWorkerDashboard bool

param apiCpu string
param apiMemory string
param workerCpu string
param workerMemory string
param webCpu string
param webMemory string

var names = {
  environment: 'cae-${namePrefix}'
  api: 'ca-${namePrefix}-api'
  worker: 'ca-${namePrefix}-worker'
  web: 'ca-${namePrefix}-web'
  migrate: 'caj-${namePrefix}-migrate'
}

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-07-01' existing = {
  name: workspaceName
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource environment 'Microsoft.App/managedEnvironments@2025-07-01' = {
  name: names.environment
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    zoneRedundant: false
  }
}

var webOrigin = 'https://${names.web}.${environment.properties.defaultDomain}'

// Container Apps secrets are Key Vault references resolved with the identity; no secret value is stored on the app.
var dotnetSecretNames = concat(['postgres-connection-string', 'jwt-signing-key'], map(optionalSecrets, secret => secret.name))
var dotnetSecrets = map(dotnetSecretNames, name => {
  name: name
  keyVaultUrl: '${vaultUri}secrets/${name}'
  identity: identityId
})

// Settings every .NET container shares (the Azure counterpart of x-dotnet-environment in docker-compose.yml).
var dotnetEnvironment = concat(
  [
    { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
    { name: 'AZURE_CLIENT_ID', value: identityClientId }
    { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
    { name: 'ConnectionStrings__TenderSync', secretRef: 'postgres-connection-string' }
    { name: 'Jwt__SigningKey', secretRef: 'jwt-signing-key' }
    { name: 'Encryption__Provider', value: 'KeyVault' }
    { name: 'Encryption__KeyVault__KeyUri', value: masterKeyUri }
    { name: 'DocumentStore__Provider', value: 'AzureBlob' }
    { name: 'DocumentStore__AzureBlob__ServiceUri', value: blobEndpoint }
    { name: 'DocumentStore__AzureBlob__ContainerName', value: blobContainerName }
    { name: 'Notifications__DashboardBaseUrl', value: webOrigin }
  ],
  map(optionalSecrets, secret => { name: secret.variable, secretRef: secret.name }),
  map(items(appSettings), setting => { name: setting.key, value: string(setting.value) })
)

var registries = [
  {
    server: registryServer
    identity: identityId
  }
]

var dotnetProbes = [
  {
    type: 'Startup'
    httpGet: { path: '/health/live', port: 8080 }
    periodSeconds: 5
    failureThreshold: 24
  }
  {
    type: 'Liveness'
    httpGet: { path: '/health/live', port: 8080 }
    periodSeconds: 30
  }
  {
    type: 'Readiness'
    httpGet: { path: '/health/ready', port: 8080 }
    periodSeconds: 15
    failureThreshold: 4
  }
]

resource api 'Microsoft.App/containerApps@2025-07-01' = {
  name: names.api
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      // Internal only: web's server is the one caller. Plain HTTP inside the environment avoids a redirect on POST.
      ingress: {
        external: false
        targetPort: 8080
        transport: 'http'
        allowInsecure: true
      }
      registries: registries
      secrets: dotnetSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${registryServer}/tendersync-api:${appImageTag}'
          resources: { cpu: json(apiCpu), memory: apiMemory }
          env: concat(dotnetEnvironment, [{ name: 'Cors__AllowedOrigins__0', value: webOrigin }])
          probes: dotnetProbes
        }
      ]
      scale: {
        minReplicas: apiMinReplicas
        maxReplicas: 2
      }
    }
  }
}

resource worker 'Microsoft.App/containerApps@2025-07-01' = {
  name: names.worker
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: exposeWorkerDashboard
        ? {
            external: true
            targetPort: 8080
            transport: 'http'
            allowInsecure: false
          }
        : null
      registries: registries
      secrets: dotnetSecrets
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${registryServer}/tendersync-worker:${appImageTag}'
          resources: { cpu: json(workerCpu), memory: workerMemory }
          env: dotnetEnvironment
          probes: dotnetProbes
        }
      ]
      // Exactly one: Hangfire's recurring schedule and the polite eTenders concurrency assume a single Worker.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

resource web 'Microsoft.App/containerApps@2025-07-01' = {
  name: names.web
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 3000
        transport: 'auto'
        allowInsecure: false
      }
      registries: registries
    }
    template: {
      containers: [
        {
          name: 'web'
          image: '${registryServer}/tendersync-web:${appImageTag}'
          resources: { cpu: json(webCpu), memory: webMemory }
          env: [
            { name: 'API_BASE_URL', value: 'http://${api.name}' }
            { name: 'AUTH_COOKIE_SECURE', value: 'true' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/login', port: 3000 }
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: { path: '/login', port: 3000 }
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: webMinReplicas
        maxReplicas: 2
      }
    }
  }
}

// Applies EF migrations and reference data, then exits (the image's CMD is --migrate-seed). Started by the deploy
// workflow before the apps move to the new image.
resource migrate 'Microsoft.App/jobs@2025-07-01' = {
  name: names.migrate
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${identityId}': {} }
  }
  properties: {
    environmentId: environment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registries
      secrets: concat(dotnetSecrets, [
        {
          name: 'seed-admin-password'
          keyVaultUrl: '${vaultUri}secrets/seed-admin-password'
          identity: identityId
        }
      ])
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: '${registryServer}/tendersync-migrate:${migrateImageTag}'
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(dotnetEnvironment, [
            { name: 'Seed__AdminEmail', value: seedAdminEmail }
            { name: 'Seed__AdminPassword', secretRef: 'seed-admin-password' }
          ])
        }
      ]
    }
  }
}

output webUrl string = 'https://${web.properties.configuration.ingress.fqdn}'
output apiName string = api.name
output workerName string = worker.name
output webName string = web.name
output migrateJobName string = migrate.name
output workerDashboardUrl string = exposeWorkerDashboard ? 'https://${worker.properties.configuration.ingress.fqdn}/hangfire' : ''
