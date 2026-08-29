@description('Deterministic globally unique Azure SQL logical server name.')
@minLength(3)
@maxLength(63)
param serverName string

@description('Azure SQL database name.')
@minLength(1)
@maxLength(128)
param databaseName string

@description('Azure region for the server and database.')
param location string

@description('Microsoft Entra object ID used only for the controlled bootstrap administrator boundary.')
param bootstrapObjectId string

@description('Microsoft Entra login/display name for the controlled bootstrap administrator boundary.')
@minLength(1)
@maxLength(128)
param bootstrapLogin string

@description('Tags applied to SQL resources.')
param tags object = {}

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: bootstrapLogin
      sid: bootstrapObjectId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    version: '12.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    // Serverless is appropriate for a short-lived, low-duty-cycle proof.
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// ACA has public egress in this no-VNet proof. The rule is removed with the proof RG.
resource azureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  parent: server
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output id string = server.id
output name string = server.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
