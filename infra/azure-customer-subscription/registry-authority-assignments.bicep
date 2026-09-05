// Assignments are deployed at the existing shared registry resource group by
// the operator-only registry authority bootstrap.
targetScope = 'resourceGroup'

@description('Name of the existing shared runtime registry.')
param registryName string

@description('Object ID of the customer-workload provisioner user-assigned identity.')
param provisionerPrincipalId string

@description('Full subscription-scoped custom metadata role definition ID.')
param metadataRoleDefinitionId string

@description('Full subscription-scoped RBAC Administrator role definition ID.')
param rbacAdministratorRoleDefinitionId string

@description('The reviewed AcrPull-only role-assignment condition.')
param registryRoleAdministrationCondition string

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource metadataAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, provisionerPrincipalId, metadataRoleDefinitionId)
  properties: {
    principalId: provisionerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: metadataRoleDefinitionId
  }
}

// This condition belongs to the provisioner's RBAC Administrator assignment,
// never to the workload's AcrPull assignment.
resource registryRoleAdministrationAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, provisionerPrincipalId, rbacAdministratorRoleDefinitionId)
  scope: registry
  properties: {
    principalId: provisionerPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: rbacAdministratorRoleDefinitionId
    condition: registryRoleAdministrationCondition
    conditionVersion: '2.0'
  }
}

output metadataRoleAssignmentId string = metadataAssignment.id
output registryRoleAdministrationAssignmentId string = registryRoleAdministrationAssignment.id
