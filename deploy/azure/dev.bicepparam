// dev: scale to zero, a small log cap and no purge protection so the environment can be torn down and rebuilt.
// Values that change per run or are personal come from environment variables the deploy workflow sets
// (docs/runbooks/azure.md lists them); the rest are committed here.
using './main.bicep'

param environmentName = 'dev'

param deployApps = bool(readEnvironmentVariable('TENDERSYNC_DEPLOY_APPS', 'true'))
param deployerPrincipalId = readEnvironmentVariable('AZURE_DEPLOYER_PRINCIPAL_ID')
param operatorPrincipalId = readEnvironmentVariable('AZURE_OPERATOR_PRINCIPAL_ID', '')
param appImageTag = readEnvironmentVariable('TENDERSYNC_APP_IMAGE_TAG', '')
param migrateImageTag = readEnvironmentVariable('TENDERSYNC_MIGRATE_IMAGE_TAG', '')
param postgresAdminPassword = readEnvironmentVariable('TENDERSYNC_POSTGRES_ADMIN_PASSWORD', '')
param seedAdminEmail = readEnvironmentVariable('TENDERSYNC_SEED_ADMIN_EMAIL')
param presentSecretNames = json(readEnvironmentVariable('TENDERSYNC_PRESENT_SECRETS', '[]'))

param apiMinReplicas = 0
param webMinReplicas = 0
param exposeWorkerDashboard = false
param keyVaultPurgeProtection = false
param logDailyCapGb = 1
param postgresBackupRetentionDays = 7

// Plain settings only; secrets go in Key Vault (optionalSecretEnvironment in main.bicep).
param appSettings = {
  Notifications__Email__FromName: 'TenderSync (dev)'
}
