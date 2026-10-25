// Key Vault (PLAN.md §12): RBAC authorisation, the company-document master key (ADR-013) and the secrets the apps read
// through Key Vault references with the user-assigned identity. Secret values never pass through this template except
// the PostgreSQL connection string, which main.bicep writes from a secure parameter.

param location string
param tags object
param vaultName string

@description('Principal id of the apps\' user-assigned identity: reads secrets, wraps and unwraps data keys.')
param appPrincipalId string

@description('Object id of the deploying service principal (GitHub Actions); creates the generated secrets.')
param deployerPrincipalId string

@description('Object id of a person who may read and set secrets (the first admin password, provider keys). Empty for none.')
param operatorPrincipalId string

@description('Purge protection cannot be turned off once on; keep it on wherever company documents matter (prod).')
param purgeProtection bool

var roles = {
  secretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  secretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  cryptoUser: '12338af0-0e69-4776-bea7-57ae8d297424'
}

resource vault 'Microsoft.KeyVault/vaults@2025-05-01' = {
  name: vaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: purgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

// Wraps the per-document AES data keys (RSA-OAEP-256). ARM creates the first version only and never rotates it on a
// redeploy; rotation is docs/runbooks/key-rotation.md.
resource masterKey 'Microsoft.KeyVault/vaults/keys@2025-05-01' = {
  parent: vault
  name: 'tendersync-master'
  properties: {
    kty: 'RSA'
    keySize: 3072
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource appSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, appPrincipalId, roles.secretsUser)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.secretsUser)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource appCrypto 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(masterKey.id, appPrincipalId, roles.cryptoUser)
  scope: masterKey
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cryptoUser)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource deployerSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, deployerPrincipalId, roles.secretsOfficer)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.secretsOfficer)
    principalId: deployerPrincipalId
    principalType: 'ServicePrincipal'
  }
}

resource operatorSecrets 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(operatorPrincipalId)) {
  name: guid(vault.id, operatorPrincipalId, roles.secretsOfficer)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.secretsOfficer)
    principalId: operatorPrincipalId
    principalType: 'User'
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
@description('Versionless key URI: KeyVaultMasterKeyProvider resolves the newest version and records it on each document.')
output masterKeyUri string = masterKey.properties.keyUri
