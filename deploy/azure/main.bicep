// TenderSync on Azure (PLAN.md §15, WP17). Deployed into an existing resource group by .github/workflows/deploy-azure.yml
// in two passes:
//   1. deployApps=false: identity, monitoring, Key Vault with the master key, registry, storage.
//   2. The workflow creates any missing generated secrets in Key Vault, imports the images, then deploys again with
//      deployApps=true: PostgreSQL, the Container Apps environment, api, worker, web and the migrate job.
// Runbook: docs/runbooks/azure.md.

targetScope = 'resourceGroup'

@description('Environment name; part of every resource name.')
@allowed([
  'dev'
  'prod'
])
param environmentName string

param location string = resourceGroup().location

@description('False for the first pass (shared resources only); see the header.')
param deployApps bool = true

@description('Object id of the deploying service principal (the GitHub Actions federated identity).')
param deployerPrincipalId string

@description('Object id of the person who operates the environment (reads the first admin password, sets provider keys). Empty for none.')
param operatorPrincipalId string = ''

@description('Image tag for api, worker and web (a commit SHA in the registry).')
param appImageTag string = ''

@description('Image tag for the migrate job: the release being deployed. Empty means appImageTag.')
param migrateImageTag string = ''

@secure()
@description('PostgreSQL administrator password, read from Key Vault (postgres-admin-password) by the workflow. Required when deployApps is true.')
param postgresAdminPassword string = ''

@description('Email of the first Admin user the migrate job seeds; its password is the seed-admin-password secret.')
param seedAdminEmail string

@description('Optional Key Vault secrets that exist in the vault. The workflow lists the vault and passes the names it finds.')
param presentSecretNames array = []

@description('Plain settings for the .NET apps, as Section__Key names (SMTP host, Telegram chat id, LLM routing...). No secrets here.')
param appSettings object = {}

param postgresSkuName string = 'Standard_B1ms'
param postgresSkuTier string = 'Burstable'
param postgresStorageGb int = 32
param postgresBackupRetentionDays int = 7

param apiMinReplicas int = 0
param webMinReplicas int = 0
param exposeWorkerDashboard bool = false
param apiCpu string = '0.5'
param apiMemory string = '1Gi'
param workerCpu string = '0.5'
param workerMemory string = '1Gi'
param webCpu string = '0.25'
param webMemory string = '0.5Gi'

param logRetentionDays int = 30
param logDailyCapGb int = 1
param blobSoftDeleteDays int = 14
param keyVaultPurgeProtection bool = true

var namePrefix = 'tendersync-${environmentName}'
// Globally unique names (Key Vault, storage, registry, PostgreSQL) get a stable suffix from the resource group.
var suffix = take(uniqueString(resourceGroup().id), 8)
var tags = {
  application: 'tendersync'
  environment: environmentName
}

// Key Vault secret name → setting it fills. A secret is wired up only when it exists, so a missing provider key
// leaves that channel or provider unconfigured instead of failing the revision.
var optionalSecretEnvironment = {
  'anthropic-api-key': 'Llm__Anthropic__ApiKey'
  'openai-compatible-api-key': 'Llm__OpenAiCompatible__ApiKey'
  'smtp-password': 'Notifications__Email__SmtpPassword'
  'telegram-bot-token': 'Notifications__Telegram__BotToken'
  'teams-webhook-url': 'Notifications__Teams__WebhookUrl'
  'sms-api-key': 'Notifications__Sms__ApiKey'
}
var optionalSecrets = map(
  filter(items(optionalSecretEnvironment), entry => contains(presentSecretNames, entry.key)),
  entry => { name: entry.key, variable: entry.value }
)

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-${namePrefix}'
  location: location
  tags: tags
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    tags: tags
    workspaceName: 'log-${namePrefix}'
    appInsightsName: 'appi-${namePrefix}'
    retentionDays: logRetentionDays
    dailyCapGb: logDailyCapGb
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    tags: tags
    vaultName: 'kv-tsync-${environmentName}-${suffix}'
    appPrincipalId: identity.properties.principalId
    deployerPrincipalId: deployerPrincipalId
    operatorPrincipalId: operatorPrincipalId
    purgeProtection: keyVaultPurgeProtection
  }
}

module registry 'modules/registry.bicep' = {
  name: 'registry'
  params: {
    location: location
    tags: tags
    registryName: 'crtsync${environmentName}${suffix}'
    appPrincipalId: identity.properties.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    tags: tags
    accountName: 'sttsync${environmentName}${suffix}'
    appPrincipalId: identity.properties.principalId
    softDeleteDays: blobSoftDeleteDays
  }
}

module postgres 'modules/postgres.bicep' = if (deployApps) {
  name: 'postgres'
  params: {
    location: location
    tags: tags
    serverName: 'psql-${namePrefix}-${suffix}'
    administratorLogin: 'tendersync'
    administratorPassword: postgresAdminPassword
    skuName: postgresSkuName
    skuTier: postgresSkuTier
    storageSizeGb: postgresStorageGb
    backupRetentionDays: postgresBackupRetentionDays
    vaultName: keyVault.outputs.name
  }
}

module apps 'modules/apps.bicep' = if (deployApps) {
  name: 'apps'
  // The apps reference the postgres-connection-string secret that the postgres module writes.
  dependsOn: [
    postgres
  ]
  params: {
    location: location
    tags: tags
    namePrefix: namePrefix
    identityId: identity.id
    identityClientId: identity.properties.clientId
    workspaceName: monitoring.outputs.workspaceName
    appInsightsName: monitoring.outputs.appInsightsName
    registryServer: registry.outputs.loginServer
    appImageTag: appImageTag
    migrateImageTag: empty(migrateImageTag) ? appImageTag : migrateImageTag
    vaultUri: keyVault.outputs.uri
    masterKeyUri: keyVault.outputs.masterKeyUri
    blobEndpoint: storage.outputs.blobEndpoint
    blobContainerName: storage.outputs.containerName
    seedAdminEmail: seedAdminEmail
    optionalSecrets: optionalSecrets
    appSettings: appSettings
    apiMinReplicas: apiMinReplicas
    webMinReplicas: webMinReplicas
    exposeWorkerDashboard: exposeWorkerDashboard
    apiCpu: apiCpu
    apiMemory: apiMemory
    workerCpu: workerCpu
    workerMemory: workerMemory
    webCpu: webCpu
    webMemory: webMemory
  }
}

output keyVaultName string = keyVault.outputs.name
output registryName string = registry.outputs.name
output registryServer string = registry.outputs.loginServer
output webUrl string = deployApps ? apps!.outputs.webUrl : ''
output apiName string = deployApps ? apps!.outputs.apiName : ''
output workerName string = deployApps ? apps!.outputs.workerName : ''
output webName string = deployApps ? apps!.outputs.webName : ''
output migrateJobName string = deployApps ? apps!.outputs.migrateJobName : ''
output workerDashboardUrl string = deployApps ? apps!.outputs.workerDashboardUrl : ''
