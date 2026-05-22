@description('Azure SQL logical server name.')
param serverName string

@description('Catalog database name.')
param databaseName string

@description('Azure region.')
param location string

@description('SQL administrator login.')
param administratorLogin string

@secure()
@description('SQL administrator password.')
param administratorPassword string

@description('SQL database SKU name.')
param databaseSkuName string

@description('SQL database SKU tier.')
param databaseSkuTier string

@description('SQL database SKU family.')
param databaseSkuFamily string

@description('SQL database capacity.')
param databaseSkuCapacity int

@description('SQL database minimum serverless capacity.')
param minCapacity string

@description('SQL database auto-pause delay in minutes.')
param autoPauseDelay int

@description('Allow Azure services to access the SQL server.')
param allowAzureServiceAccess bool

@description('Resource tags.')
param tags object = {}

resource server 'Microsoft.Sql/servers@2023-08-01' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  name: databaseName
  parent: server
  location: location
  tags: tags
  sku: {
    capacity: databaseSkuCapacity
    family: databaseSkuFamily
    name: databaseSkuName
    tier: databaseSkuTier
  }
  properties: {
    autoPauseDelay: autoPauseDelay
    collation: 'SQL_Latin1_General_CP1_CI_AS'
    minCapacity: json(minCapacity)
    requestedBackupStorageRedundancy: 'Zone'
    zoneRedundant: false
  }
}

resource azureServicesFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-08-01' = if (allowAzureServiceAccess) {
  name: 'AllowAllWindowsAzureIps'
  parent: server
  properties: {
    endIpAddress: '0.0.0.0'
    startIpAddress: '0.0.0.0'
  }
}

output databaseName string = database.name
output fullyQualifiedDomainName string = server.properties.fullyQualifiedDomainName
output serverName string = server.name
