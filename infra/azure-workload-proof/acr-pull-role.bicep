targetScope = 'resourceGroup'

@description('Name of the existing commercial runtime ACR.')
@minLength(5)
@maxLength(50)
param registryName string = 'valenceruntimeimages'

@description('Resource ID of the workload user-assigned identity.')
param workloadIdentityId string

@description('Principal ID of the workload user-assigned identity.')
param workloadPrincipalId string

@description('Recovery operation identity used to discover and reconcile the assignment independently of ARM deployment history.')
param recoveryId string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

// This template is deployed to the registry's resource group: Azure role
// assignments must be deployed at the ACR's resource scope when the registry
// lives outside the disposable proof resource group.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, workloadIdentityId, acrPullRoleId)
  scope: registry
  properties: {
    principalId: workloadPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    description: 'elsa-control-recovery|${recoveryId}|${workloadIdentityId}'
  }
}

output roleAssignmentId string = acrPull.id
