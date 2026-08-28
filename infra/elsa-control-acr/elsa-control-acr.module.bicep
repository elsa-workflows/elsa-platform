@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource elsa_control_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: take('elsacontrolacr${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: {
    name: 'Basic'
  }
  tags: {
    'aspire-resource-name': 'elsa-control-acr'
  }
}

output name string = elsa_control_acr.name

output loginServer string = elsa_control_acr.properties.loginServer

output id string = elsa_control_acr.id