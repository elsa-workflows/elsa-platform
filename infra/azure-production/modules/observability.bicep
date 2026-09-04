@description('Deterministic name for the Log Analytics workspace.')
@minLength(4)
@maxLength(63)
param name string

@description('Azure region for the workspace.')
param location string

@description('Safe metadata tags applied to the workspace.')
param tags object = {}

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    // Thirty days is the shortest supported retention for the PerGB2018 tier.
    retentionInDays: 30
    sku: {
      name: 'PerGB2018'
    }
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

output id string = workspace.id
output name string = workspace.name
output customerId string = workspace.properties.customerId
