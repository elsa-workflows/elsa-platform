@description('Deterministic name for the workload user-assigned identity.')
@minLength(3)
@maxLength(128)
param name string

@description('Azure region for the identity.')
param location string

@description('Safe metadata tags applied to the identity.')
param tags object = {}

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: name
  location: location
  tags: tags
}

output id string = identity.id
output name string = identity.name
output clientId string = identity.properties.clientId
output principalId string = identity.properties.principalId
