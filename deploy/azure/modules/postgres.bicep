// Azure Database for PostgreSQL Flexible Server (PLAN.md §15: Burstable B1ms to start) with the tendersync database.
// Public access limited to Azure-hosted clients plus password and TLS; private networking needs a VNet-integrated
// Container Apps environment and is a later hardening step (docs/runbooks/azure.md).

param location string
param tags object
param serverName string
param administratorLogin string

@secure()
param administratorPassword string

param skuName string
param skuTier string
param storageSizeGb int
param backupRetentionDays int

@description('Key Vault that receives the postgres-connection-string secret the apps reference.')
param vaultName string

@description('Npgsql pool cap per pool. B1ms allows about 50 connections; API and Worker each hold two pools.')
param maxPoolSize int = 10

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2025-08-01' = {
  name: serverName
  location: location
  tags: tags
  sku: {
    name: skuName
    tier: skuTier
  }
  properties: {
    version: '17'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    authConfig: {
      activeDirectoryAuth: 'Disabled'
      passwordAuth: 'Enabled'
    }
    storage: {
      storageSizeGB: storageSizeGb
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'Disabled'
    }
    network: {
      publicNetworkAccess: 'Enabled'
    }
  }
}

// 0.0.0.0 is Azure's "allow Azure services" rule: Container Apps on the consumption plan have no fixed outbound address.
resource allowAzure 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2025-08-01' = {
  parent: server
  name: 'AllowAllAzureServicesAndResourcesWithinAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2025-08-01' = {
  parent: server
  name: 'tendersync'
  properties: {
    charset: 'UTF8'
    collation: 'en_US.utf8'
  }
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' existing = {
  name: vaultName
}

// VerifyFull checks the server certificate against the image's CA bundle; Gss Encryption Mode=Disable stops Npgsql
// probing Kerberos, which the aspnet image cannot load (same as Compose).
resource connectionString 'Microsoft.KeyVault/vaults/secrets@2025-05-01' = {
  parent: vault
  name: 'postgres-connection-string'
  properties: {
    contentType: 'Npgsql connection string'
    value: 'Host=${server.properties.fullyQualifiedDomainName};Port=5432;Database=${database.name};Username=${administratorLogin};Password=${administratorPassword};SSL Mode=VerifyFull;Gss Encryption Mode=Disable;Maximum Pool Size=${maxPoolSize}'
  }
}

output fqdn string = server.properties.fullyQualifiedDomainName
output connectionStringSecretName string = connectionString.name
