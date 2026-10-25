// prod: api and web stay warm, purge protection on (company documents depend on the master key), longer backups.
// Values that change per run or are personal come from environment variables the deploy workflow sets
// (docs/runbooks/azure.md lists them); the rest are committed here.
using './main.bicep'

param environmentName = 'prod'

param deployApps = bool(readEnvironmentVariable('TENDERSYNC_DEPLOY_APPS', 'true'))
param deployerPrincipalId = readEnvironmentVariable('AZURE_DEPLOYER_PRINCIPAL_ID')
param operatorPrincipalId = readEnvironmentVariable('AZURE_OPERATOR_PRINCIPAL_ID', '')
param appImageTag = readEnvironmentVariable('TENDERSYNC_APP_IMAGE_TAG', '')
param migrateImageTag = readEnvironmentVariable('TENDERSYNC_MIGRATE_IMAGE_TAG', '')
param postgresAdminPassword = readEnvironmentVariable('TENDERSYNC_POSTGRES_ADMIN_PASSWORD', '')
param seedAdminEmail = readEnvironmentVariable('TENDERSYNC_SEED_ADMIN_EMAIL')
param presentSecretNames = json(readEnvironmentVariable('TENDERSYNC_PRESENT_SECRETS', '[]'))

param apiMinReplicas = 1
param webMinReplicas = 1
param exposeWorkerDashboard = false
param keyVaultPurgeProtection = true
param logDailyCapGb = 2
param postgresBackupRetentionDays = 14

// Plain settings only; secrets go in Key Vault (optionalSecretEnvironment in main.bicep).
param appSettings = {}
