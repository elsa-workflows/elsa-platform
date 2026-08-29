@description('Proof-only Key Vault name. The name must be globally unique and 3-24 characters.')
@minLength(3)
@maxLength(24)
param name string

@description('Azure region for the vault.')
param location string

@description('Principal ID of the workload identity that reads runtime secrets.')
param workloadPrincipalId string

@description('Interactive Entra operator object ID allowed to seed the two proof secrets.')
param bootstrapObjectId string

@description('Name of the SQL connection secret created by the runbook after the vault exists.')
@minLength(1)
@maxLength(127)
param sqlConnectionSecretName string

@description('Name of the Elsa signing secret created by the runbook after the vault exists.')
@minLength(1)
@maxLength(127)
param signingKeySecretName string

@description('Name of the disposable proof administrator password created by the runbook after the vault exists.')
@minLength(1)
@maxLength(127)
param adminPasswordSecretName string

@description('Tags applied to the vault.')
param tags object = {}

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
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
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// The runtime can read only secret values. It cannot administer the proof vault.
resource workloadSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, workloadPrincipalId, keyVaultSecretsUserRoleId)
  scope: vault
  properties: {
    principalId: workloadPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
  }
}

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
var keyVaultSecretsOfficerRoleId = 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'

resource bootstrapSecretsOfficer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, bootstrapObjectId, keyVaultSecretsOfficerRoleId)
  scope: vault
  properties: {
    principalId: bootstrapObjectId
    principalType: 'User'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsOfficerRoleId)
  }
}

output id string = vault.id
output name string = vault.name
output uri string = vault.properties.vaultUri
output sqlConnectionSecretUri string = '${vault.properties.vaultUri}secrets/${sqlConnectionSecretName}'
output signingKeySecretUri string = '${vault.properties.vaultUri}secrets/${signingKeySecretName}'
output adminCredentialUri string = '${vault.properties.vaultUri}secrets/${adminPasswordSecretName}'
