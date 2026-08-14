@description('The location for the resource(s) to be deployed.')
param location string = resourceGroup().location

resource valence_control_acr 'Microsoft.ContainerRegistry/registries@2025-04-01' = {
  name: take('valencecontrolacr${uniqueString(resourceGroup().id)}', 50)
  location: location
  sku: {
    name: 'Basic'
  }
  tags: {
    'aspire-resource-name': 'valence-control-acr'
  }
}

output name string = valence_control_acr.name

output loginServer string = valence_control_acr.properties.loginServer

output id string = valence_control_acr.id