@description('Keycloak Web App name.')
param name string

@description('Azure region.')
param location string

@description('App Service Plan name.')
param appServicePlanName string

@description('App Service Plan SKU name.')
param skuName string

@description('Keycloak container image.')
param containerImage string

@description('Keycloak container startup command.')
param startCommand string

@description('Keycloak PostgreSQL flexible server name.')
param postgresServerName string

@description('Keycloak PostgreSQL database name.')
param databaseName string

@description('Keycloak PostgreSQL administrator login.')
param postgresAdministratorLogin string

@secure()
@description('Keycloak PostgreSQL administrator password.')
param postgresAdministratorPassword string

@description('Keycloak PostgreSQL compute SKU.')
param postgresSkuName string

@description('Keycloak PostgreSQL storage in GiB.')
param postgresStorageGb int

@description('Keycloak bootstrap admin username.')
param adminUsername string

@secure()
@description('Keycloak bootstrap admin password.')
param adminPassword string

@description('Resource tags.')
param tags object = {}

var keycloakPort = '8080'
var postgresHost = '${postgres.name}.postgres.database.azure.com'
var jdbcUrl = 'jdbc:postgresql://${postgresHost}:5432/${databaseName}?sslmode=require'
var keycloakUrl = 'https://${name}.azurewebsites.net'
var postgresSkuTier = startsWith(postgresSkuName, 'Standard_B') ? 'Burstable' : startsWith(postgresSkuName, 'Standard_E') ? 'MemoryOptimized' : 'GeneralPurpose'

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2023-12-01-preview' = {
  name: postgresServerName
  location: location
  tags: tags
  sku: {
    name: postgresSkuName
    tier: postgresSkuTier
  }
  properties: {
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: postgresAdministratorPassword
    version: '16'
    storage: {
      storageSizeGB: postgresStorageGb
    }
    backup: {
      backupRetentionDays: 14
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

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2023-12-01-preview' = {
  parent: postgres
  name: databaseName
}

resource allowAzureServices 'Microsoft.DBforPostgreSQL/flexibleServers/firewallRules@2023-12-01-preview' = {
  parent: postgres
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  tags: tags
  sku: {
    name: skuName
  }
  properties: {
    reserved: true
  }
}

resource app 'Microsoft.Web/sites@2023-12-01' = {
  name: name
  location: location
  kind: 'app,linux,container'
  tags: tags
  properties: {
    clientAffinityEnabled: true
    httpsOnly: true
    serverFarmId: plan.id
    siteConfig: {
      alwaysOn: true
      appSettings: [
        {
          name: 'WEBSITES_PORT'
          value: keycloakPort
        }
        {
          name: 'WEBSITES_CONTAINER_START_TIME_LIMIT'
          value: '1800'
        }
        {
          name: 'WEBSITE_WARMUP_PATH'
          value: '/health/live'
        }
        {
          name: 'KC_DB'
          value: 'postgres'
        }
        {
          name: 'KC_DB_URL'
          value: jdbcUrl
        }
        {
          name: 'KC_DB_USERNAME'
          value: postgresAdministratorLogin
        }
        {
          name: 'KC_DB_PASSWORD'
          value: postgresAdministratorPassword
        }
        {
          name: 'KC_HOSTNAME'
          value: keycloakUrl
        }
        {
          name: 'KC_HTTP_ENABLED'
          value: 'true'
        }
        {
          name: 'KC_PROXY_HEADERS'
          value: 'xforwarded'
        }
        {
          name: 'KC_HEALTH_ENABLED'
          value: 'true'
        }
        {
          name: 'KC_METRICS_ENABLED'
          value: 'true'
        }
        {
          name: 'KC_BOOTSTRAP_ADMIN_USERNAME'
          value: adminUsername
        }
        {
          name: 'KC_BOOTSTRAP_ADMIN_PASSWORD'
          value: adminPassword
        }
      ]
      appCommandLine: startCommand
      ftpsState: 'Disabled'
      healthCheckPath: '/health/live'
      linuxFxVersion: 'DOCKER|${containerImage}'
      minTlsVersion: '1.2'
    }
  }
  dependsOn: [
    database
    allowAzureServices
  ]
}

output defaultHostName string = keycloakUrl
output name string = app.name
output postgresServerName string = postgres.name
