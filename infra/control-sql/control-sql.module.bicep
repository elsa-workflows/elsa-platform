@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource sqlServerAdminManagedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: take('control_sql-admin-${uniqueString(resourceGroup().id)}', 63)
  location: location
}

resource control_sql 'Microsoft.Sql/servers@2023-08-01' = {
  name: take('controlsql-${uniqueString(resourceGroup().id)}', 63)
  location: location
  properties: {
    administrators: {
      administratorType: 'ActiveDirectory'
      login: sqlServerAdminManagedIdentity.name
      sid: sqlServerAdminManagedIdentity.properties.principalId
      tenantId: subscription().tenantId
      azureADOnlyAuthentication: true
    }
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    version: '12.0'
  }
  tags: {
    'aspire-resource-name': 'control-sql'
  }
}

resource sqlFirewallRule_AllowAllAzureIps 'Microsoft.Sql/servers/firewallRules@2023-08-01' = {
  name: 'AllowAllAzureIps'
  properties: {
    endIpAddress: '0.0.0.0'
    startIpAddress: '0.0.0.0'
  }
  parent: control_sql
}

resource Catalog 'Microsoft.Sql/servers/databases@2023-08-01' = {
  name: 'Catalog'
  location: location
  properties: {
    autoPauseDelay: 60
    zoneRedundant: false
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Zone'
    useFreeLimit: false
  }
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  parent: control_sql
}

output sqlServerFqdn string = control_sql.properties.fullyQualifiedDomainName

output name string = control_sql.name

output id string = control_sql.id

output sqlServerAdminName string = control_sql.properties.administrators.login