@description('Web App name.')
param name string

@description('Azure region.')
param location string

@description('App Service Plan name.')
param appServicePlanName string

@description('App Service Plan SKU name.')
param skuName string

@description('Full container image reference.')
param containerImage string

@secure()
@description('Catalog SQL connection string.')
param catalogConnectionString string

@secure()
@description('Admin API key.')
param adminApiKey string

@secure()
@description('Optional builder-client API key.')
param builderClientApiKey string = ''

@secure()
@description('Application Insights connection string.')
param appInsightsConnectionString string = ''

@description('Additional application settings.')
param additionalAppSettings object = {}

@description('Resource tags.')
param tags object = {}

var requiredAppSettings = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Production'
  }
  {
    name: 'WEBSITES_PORT'
    value: '8080'
  }
  {
    name: 'Database__Provider'
    value: 'SqlServer'
  }
  {
    name: 'ConnectionStrings__Catalog'
    value: catalogConnectionString
  }
  {
    name: 'Authentication__ApiKey'
    value: adminApiKey
  }
  {
    name: 'Authentication__WorkspaceTrustedHeaders__Enabled'
    value: 'false'
  }
  {
    name: 'Sync__Scheduled__Enabled'
    value: 'true'
  }
  {
    name: 'AZURE_TOKEN_CREDENTIALS'
    value: 'prod'
  }
]

var optionalBuilderSettings = empty(builderClientApiKey) ? [] : [
  {
    name: 'Authentication__BuilderClientApiKey'
    value: builderClientApiKey
  }
]

var optionalObservabilitySettings = empty(appInsightsConnectionString) ? [] : [
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
  {
    name: 'ApplicationInsightsAgent_EXTENSION_VERSION'
    value: '~3'
  }
]

var extraSettings = [
  for setting in items(additionalAppSettings): {
    name: setting.key
    value: string(setting.value)
  }
]

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
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    clientAffinityEnabled: false
    httpsOnly: true
    serverFarmId: plan.id
    siteConfig: {
      acrUseManagedIdentityCreds: true
      alwaysOn: true
      appSettings: concat(requiredAppSettings, optionalBuilderSettings, optionalObservabilitySettings, extraSettings)
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      linuxFxVersion: 'DOCKER|${containerImage}'
      minTlsVersion: '1.2'
    }
  }
}

output defaultHostName string = 'https://${app.properties.defaultHostName}'
output name string = app.name
output principalId string = app.identity.principalId
