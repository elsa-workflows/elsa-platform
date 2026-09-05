// Deploy this operator-only authority bootstrap against the subscription that
// owns the shared runtime registry. It is intentionally outside the runtime
// provider TemplateRoot and is never executed by the lifecycle worker.
targetScope = 'subscription'

@description('Name of the existing shared runtime registry resource group.')
param registryResourceGroupName string

@description('Name of the existing shared runtime registry.')
@minLength(5)
@maxLength(50)
param registryName string

@description('Object ID of the customer-workload provisioner user-assigned identity.')
param provisionerPrincipalId string

var metadataRoleName = 'Elsa Control Registry Deployment Metadata Reader'
var metadataRoleDefinitionGuid = guid(subscription().id, 'elsa-control-registry-deployment-metadata-v1')
var rbacAdministratorRoleDefinitionId = subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'f58310d9-a9f6-439a-9e8d-f62e7b41a168')
var registryRoleAdministrationCondition = '''
  (
    (!(ActionMatches{'Microsoft.Authorization/roleAssignments/write'}))
    OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d}
        AND @Request[Microsoft.Authorization/roleAssignments:PrincipalType] StringEqualsIgnoreCase 'ServicePrincipal')
  )
  AND
  (
    (!(ActionMatches{'Microsoft.Authorization/roleAssignments/delete'}))
    OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {7f951dda-4ed3-4680-a7ca-43fe172d538d}
        AND @Resource[Microsoft.Authorization/roleAssignments:PrincipalType] StringEqualsIgnoreCase 'ServicePrincipal')
  )
'''

resource registryGroup 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: registryResourceGroupName
}

// This role contains only deployment metadata and registry observation rights.
// It cannot mutate the resource group, registry or any role assignment.
resource metadataRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: metadataRoleDefinitionGuid
  properties: {
    roleName: metadataRoleName
    description: 'Read and manage only Elsa Control registry deployment metadata.'
    type: 'CustomRole'
    permissions: [
      {
        actions: [
          'Microsoft.Resources/deployments/read'
          'Microsoft.Resources/deployments/write'
          'Microsoft.Resources/deployments/delete'
          'Microsoft.Resources/deployments/cancel/action'
          'Microsoft.Resources/deployments/validate/action'
          'Microsoft.Resources/deployments/whatIf/action'
          'Microsoft.Resources/deployments/exportTemplate/action'
          'Microsoft.Resources/deployments/operations/read'
          'Microsoft.Resources/deployments/operationstatuses/read'
          'Microsoft.Resources/subscriptions/resourceGroups/read'
          'Microsoft.ContainerRegistry/registries/read'
          'Microsoft.Authorization/roleAssignments/read'
          'Microsoft.Authorization/roleDefinitions/read'
        ]
        notActions: []
        dataActions: []
        notDataActions: []
      }
    ]
    assignableScopes: [registryGroup.id]
  }
}

module registryAssignments './registry-authority-assignments.bicep' = {
  name: 'elsa-control-registry-authority-assignments'
  scope: registryGroup
  params: {
    registryName: registryName
    provisionerPrincipalId: provisionerPrincipalId
    metadataRoleDefinitionId: metadataRole.id
    rbacAdministratorRoleDefinitionId: rbacAdministratorRoleDefinitionId
    registryRoleAdministrationCondition: registryRoleAdministrationCondition
  }
}

output metadataRoleDefinitionId string = metadataRole.id
output metadataRoleAssignmentId string = registryAssignments.outputs.metadataRoleAssignmentId
output registryRoleAdministrationAssignmentId string = registryAssignments.outputs.registryRoleAdministrationAssignmentId
