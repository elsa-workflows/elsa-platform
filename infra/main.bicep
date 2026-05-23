targetScope = 'resourceGroup'

@description('Short environment name used to derive resource names, for example dev, test, or prod.')
@minLength(1)
param environmentName string

@description('Azure region for all regional resources.')
param location string = resourceGroup().location

@description('Optional globally unique prefix. Leave empty to derive names from the environment and resource group.')
param namePrefix string = ''

@description('Container image to run, for example myacr.azurecr.io/elsa-platform/api:tag. Leave empty for the default repository in the provisioned ACR.')
param containerImage string = ''

@secure()
@description('Admin API key used for dashboard login and admin API access.')
param adminApiKey string

@secure()
@description('Optional builder-client API key for Runtime Builder endpoints.')
param builderClientApiKey string = ''

@description('SQL administrator login name.')
param sqlAdministratorLogin string = 'elsaadmin'

@secure()
@description('SQL administrator password.')
param sqlAdministratorPassword string

@description('App Service Plan SKU.')
param appServiceSkuName string = 'B1'

@description('Azure Container Registry SKU.')
@allowed([
  'Basic'
  'Standard'
  'Premium'
])
param containerRegistrySku string = 'Standard'

@description('Azure SQL database SKU name.')
param sqlDatabaseSkuName string = 'GP_S_Gen5'

@description('Azure SQL database SKU tier.')
param sqlDatabaseSkuTier string = 'GeneralPurpose'

@description('Azure SQL database SKU family.')
param sqlDatabaseSkuFamily string = 'Gen5'

@description('Azure SQL database vCore capacity.')
param sqlDatabaseCapacity int = 1

@description('Azure SQL serverless minimum capacity.')
param sqlDatabaseMinCapacity string = '0.5'

@description('Azure SQL serverless auto-pause delay in minutes. Use -1 to disable auto-pause.')
param sqlDatabaseAutoPauseDelay int = 60

@description('Allow Azure service public IPs to reach Azure SQL. Required for the default App Service to SQL path.')
param allowAzureSqlServiceAccess bool = true

@description('Deploy a managed Keycloak identity service for customer login.')
param deployKeycloak bool = false

@description('Keycloak App Service Plan SKU. Use at least B2/P1v3 for production traffic.')
param keycloakAppServiceSkuName string = 'B2'

@description('Keycloak container image.')
param keycloakContainerImage string = 'quay.io/keycloak/keycloak:26.0'

@description('Keycloak container startup command.')
param keycloakStartCommand string = 'start --hostname-strict=false'

@description('Keycloak realm used by Elsa Platform.')
param keycloakRealm string = 'elsa-platform'

@description('Keycloak OIDC client ID used by the Elsa Platform API.')
param keycloakClientId string = 'elsa-platform-console'

@secure()
@description('Keycloak OIDC confidential client secret used by the Elsa Platform API. Must match the Keycloak client.')
param keycloakClientSecret string = ''

@description('Keycloak bootstrap admin username. Used by Keycloak only when the server initializes an empty database.')
param keycloakAdminUsername string = 'keycloak-admin'

@secure()
@description('Keycloak bootstrap admin password. Required when deployKeycloak is true.')
param keycloakAdminPassword string = ''

@description('Keycloak PostgreSQL administrator login name.')
param keycloakPostgresAdministratorLogin string = 'keycloakadmin'

@secure()
@description('Keycloak PostgreSQL administrator password. Required when deployKeycloak is true.')
param keycloakPostgresAdministratorPassword string = ''

@description('Keycloak PostgreSQL compute SKU.')
param keycloakPostgresSkuName string = 'Standard_B1ms'

@description('Keycloak PostgreSQL storage in GiB.')
param keycloakPostgresStorageGb int = 32

@description('Additional application settings to merge into the Web App.')
param additionalAppSettings object = {}

@description('Tags applied to all resources.')
param tags object = {}

var normalizedPrefix = empty(namePrefix) ? 'elsa-${environmentName}' : namePrefix
var uniqueSuffix = uniqueString(subscription().id, resourceGroup().id, environmentName)
var registryNamePrefix = empty(namePrefix) ? 'elsa${environmentName}' : replace(namePrefix, '-', '')
var registryName = take(toLower('${registryNamePrefix}${uniqueSuffix}'), 50)
var appServicePlanName = '${normalizedPrefix}-plan-${uniqueSuffix}'
var webAppName = take('${normalizedPrefix}-api-${uniqueSuffix}', 60)
var sqlServerName = take(toLower('${normalizedPrefix}-sql-${uniqueSuffix}'), 63)
var sqlDatabaseName = 'Catalog'
var logAnalyticsWorkspaceName = take('${normalizedPrefix}-logs-${uniqueSuffix}', 63)
var appInsightsName = take('${normalizedPrefix}-appi-${uniqueSuffix}', 255)
var keycloakWebAppName = take('${normalizedPrefix}-identity-${uniqueSuffix}', 60)
var keycloakPlanName = '${normalizedPrefix}-identity-plan-${uniqueSuffix}'
var keycloakPostgresName = take(toLower('${normalizedPrefix}-identity-pg-${uniqueSuffix}'), 63)
var keycloakDatabaseName = 'keycloak'
var defaultImage = '${registryName}.azurecr.io/elsa-platform/api:latest'
var effectiveContainerImage = empty(containerImage) ? defaultImage : containerImage
var keycloakDefaultHostName = 'https://${keycloakWebAppName}.azurewebsites.net'
var keycloakAuthority = '${keycloakDefaultHostName}/realms/${keycloakRealm}'

var keycloakApiSettings = deployKeycloak ? {
  Authentication__PlatformIdentity__Provider: 'Keycloak'
  Authentication__PlatformIdentity__Authority: keycloakAuthority
  Authentication__PlatformIdentity__Issuer: keycloakAuthority
  Authentication__PlatformIdentity__Audience: keycloakClientId
  Authentication__PlatformIdentity__ClientId: keycloakClientId
  Authentication__PlatformIdentity__ClientSecret: keycloakClientSecret
  Authentication__PlatformIdentity__RequireHttpsMetadata: 'true'
  Authentication__PlatformIdentity__Scopes__0: 'openid'
  Authentication__PlatformIdentity__Scopes__1: 'profile'
  Authentication__PlatformIdentity__Scopes__2: 'email'
  Authentication__PlatformIdentity__Claims__Subject: 'sub'
  Authentication__PlatformIdentity__Claims__DisplayName__0: 'name'
  Authentication__PlatformIdentity__Claims__DisplayName__1: 'preferred_username'
  Authentication__PlatformIdentity__Claims__Email__0: 'email'
} : {}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    appInsightsName: appInsightsName
    location: location
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
    tags: tags
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  sku: {
    name: containerRegistrySku
  }
  tags: tags
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

module sql 'modules/sql-catalog.bicep' = {
  name: 'sql-catalog'
  params: {
    allowAzureServiceAccess: allowAzureSqlServiceAccess
    administratorLogin: sqlAdministratorLogin
    administratorPassword: sqlAdministratorPassword
    databaseName: sqlDatabaseName
    databaseSkuCapacity: sqlDatabaseCapacity
    databaseSkuFamily: sqlDatabaseSkuFamily
    databaseSkuName: sqlDatabaseSkuName
    databaseSkuTier: sqlDatabaseSkuTier
    location: location
    minCapacity: sqlDatabaseMinCapacity
    autoPauseDelay: sqlDatabaseAutoPauseDelay
    serverName: sqlServerName
    tags: tags
  }
}

module keycloak 'modules/keycloak-webapp.bicep' = if (deployKeycloak) {
  name: 'keycloak-webapp'
  params: {
    adminPassword: keycloakAdminPassword
    adminUsername: keycloakAdminUsername
    appServicePlanName: keycloakPlanName
    containerImage: keycloakContainerImage
    databaseName: keycloakDatabaseName
    location: location
    name: keycloakWebAppName
    postgresAdministratorLogin: keycloakPostgresAdministratorLogin
    postgresAdministratorPassword: keycloakPostgresAdministratorPassword
    postgresServerName: keycloakPostgresName
    postgresSkuName: keycloakPostgresSkuName
    postgresStorageGb: keycloakPostgresStorageGb
    skuName: keycloakAppServiceSkuName
    startCommand: keycloakStartCommand
    tags: tags
  }
}

module web 'modules/platform-api-webapp.bicep' = {
  name: 'platform-api-webapp'
  params: {
    additionalAppSettings: union(keycloakApiSettings, additionalAppSettings)
    adminApiKey: adminApiKey
    appInsightsConnectionString: observability.outputs.applicationInsightsConnectionString
    appServicePlanName: appServicePlanName
    builderClientApiKey: builderClientApiKey
    catalogDatabaseName: sqlDatabaseName
    containerImage: effectiveContainerImage
    location: location
    name: webAppName
    skuName: appServiceSkuName
    sqlAdministratorLogin: sqlAdministratorLogin
    sqlAdministratorPassword: sqlAdministratorPassword
    sqlServerFullyQualifiedDomainName: sql.outputs.fullyQualifiedDomainName
    tags: tags
  }
}

var acrPullRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')

resource webAppAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registryName, webAppName, acrPullRoleDefinitionId)
  scope: containerRegistry
  properties: {
    principalId: web.outputs.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: acrPullRoleDefinitionId
  }
}

output containerRegistryLoginServer string = containerRegistry.properties.loginServer
output containerRegistryName string = containerRegistry.name
output defaultContainerImage string = defaultImage
output platformApiUrl string = web.outputs.defaultHostName
output keycloakUrl string = deployKeycloak ? keycloakDefaultHostName : ''
output keycloakAuthority string = deployKeycloak ? keycloakAuthority : ''
output keycloakRealm string = deployKeycloak ? keycloakRealm : ''
output keycloakClientId string = deployKeycloak ? keycloakClientId : ''
output resourceGroupName string = resourceGroup().name
output sqlDatabaseName string = sqlDatabaseName
output sqlServerFullyQualifiedDomainName string = sql.outputs.fullyQualifiedDomainName
output webAppName string = web.outputs.name
