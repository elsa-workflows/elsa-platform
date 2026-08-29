targetScope = 'resourceGroup'

@description('Short, unique suffix for this disposable proof. Use lowercase letters, numbers and hyphens only.')
@minLength(3)
@maxLength(16)
param proofName string

@description('The proof is intentionally constrained to West Europe.')
@allowed([
  'westeurope'
])
param location string = 'westeurope'

@description('Immutable repository name for the Elsa 3.8 Combined image, without a tag or digest.')
@minLength(1)
param imageRepository string = 'valenceruntimeimages.azurecr.io/runtime-combined'

@description('Lower-case SHA-256 digest for the image, without the sha256: prefix. Tags are not accepted.')
@minLength(64)
@maxLength(64)
param imageDigest string

@description('Existing ACR resource name. The default is the commercial runtime registry.')
@minLength(5)
@maxLength(50)
param registryName string = 'valenceruntimeimages'

@description('Subscription ID containing the existing runtime ACR.')
param registrySubscriptionId string = subscription().subscriptionId

@description('Resource group containing the existing runtime ACR.')
@minLength(1)
param registryResourceGroupName string

@description('Microsoft Entra object ID used only as the temporary SQL bootstrap administrator.')
param sqlBootstrapObjectId string

@description('Microsoft Entra login/display name for the temporary SQL bootstrap administrator.')
@minLength(1)
@maxLength(128)
param sqlBootstrapLogin string

@description('Name of the SQL connection secret in the proof Key Vault.')
@minLength(1)
@maxLength(127)
param sqlConnectionSecretName string = 'sql-connection'

@description('Name of the Elsa identity signing secret in the proof Key Vault.')
@minLength(1)
@maxLength(127)
param signingKeySecretName string = 'identity-signing-key'

@description('Name of the disposable proof administrator password in Key Vault.')
@minLength(1)
@maxLength(127)
param adminPasswordSecretName string = 'admin-password'

@description('Disposable proof administrator username.')
@minLength(1)
@maxLength(128)
param adminUsername string = 'proof-admin'

@description('Elsa version represented by the immutable image. Version is data, not an IaC branch.')
@minLength(1)
param elsaVersion string = '3.8'

@description('Exact Nuplane feed version for Elsa SQL Server workflow/identity persistence packages.')
@minLength(1)
param sqlWorkflowPackageVersion string = '3.8.0-preview.5413'

@description('Exact Nuplane feed version for Elsa Quartz SQL Server scheduling package.')
@minLength(1)
param sqlQuartzPackageVersion string = '3.8.0-preview.342'

@description('UTC date after which the disposable proof must be reviewed and removed.')
param expiryUtc string = '2026-09-02'

@description('Owner tag for the disposable proof.')
@minLength(1)
param owner string = 'elsa-control'

@description('Create the externally reachable Container App. Set false for the foundation phase while the runbook seeds Key Vault secrets.')
param deployWorkload bool = true

@description('Additional tags. Required proof/owner/expiry/fingerprint tags always win.')
param additionalTags object = {}

var planInput = 'proof=108|name=${proofName}|location=${location}|image=${imageRepository}@sha256:${toLower(imageDigest)}|elsa=${elsaVersion}|sql-workflow=${sqlWorkflowPackageVersion}|sql-quartz=${sqlQuartzPackageVersion}|topology=combined|acr=${registrySubscriptionId}/${registryResourceGroupName}/${registryName}|sql-bootstrap=${sqlBootstrapObjectId}/${sqlBootstrapLogin}|admin=${adminUsername}|secrets=${sqlConnectionSecretName}/${signingKeySecretName}/${adminPasswordSecretName}|expiry=${expiryUtc}'
// Bicep 0.43 has no SHA-256 function. uniqueString is deterministic for the
// canonical input; external evidence may additionally hash the compiled template.
var planFingerprint = uniqueString(planInput)
var revisionSuffix = take(planFingerprint, 24)
var requiredTags = {
  proof: '108'
  owner: owner
  expiry: expiryUtc
  'plan-fingerprint': planFingerprint
  'managed-by': 'elsa-control-bicep'
}
var tags = union(additionalTags, requiredTags)

module workloadIdentity 'modules/identity.bicep' = {
  name: 'workload-identity'
  params: {
    name: '${proofName}-identity'
    location: location
    tags: tags
  }
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    name: '${proofName}-logs'
    location: location
    tags: tags
  }
}

module database 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: '${proofName}-sql'
    databaseName: 'Elsa'
    location: location
    bootstrapObjectId: sqlBootstrapObjectId
    bootstrapLogin: sqlBootstrapLogin
    tags: tags
  }
}

module vault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    name: '${proofName}-kv'
    location: location
    workloadPrincipalId: workloadIdentity.outputs.principalId
    bootstrapObjectId: sqlBootstrapObjectId
    sqlConnectionSecretName: sqlConnectionSecretName
    signingKeySecretName: signingKeySecretName
    tags: tags
  }
}

module containerEnvironment 'modules/container-apps-environment.bicep' = {
  name: 'container-apps-environment'
  params: {
    name: '${proofName}-aca'
    location: location
    logAnalyticsWorkspaceName: observability.outputs.name
    tags: tags
  }
}

module workload 'modules/container-app.bicep' = if (deployWorkload) {
  name: 'container-app'
  params: {
    name: '${proofName}-app'
    location: location
    managedEnvironmentId: containerEnvironment.outputs.id
    registryName: registryName
    registrySubscriptionId: registrySubscriptionId
    registryResourceGroupName: registryResourceGroupName
    imageRepository: imageRepository
    imageDigest: imageDigest
    workloadIdentityId: workloadIdentity.outputs.id
    sqlConnectionSecretUri: vault.outputs.sqlConnectionSecretUri
    signingKeySecretUri: vault.outputs.signingKeySecretUri
    adminPasswordSecretUri: '${vault.outputs.uri}secrets/${adminPasswordSecretName}'
    sqlRef: take(sqlConnectionSecretName, 63)
    signingRef: take(signingKeySecretName, 63)
    adminCredentialRef: take(adminPasswordSecretName, 63)
    adminUsername: adminUsername
    elsaVersion: elsaVersion
    sqlWorkflowPackageVersion: sqlWorkflowPackageVersion
    sqlQuartzPackageVersion: sqlQuartzPackageVersion
    revisionSuffix: revisionSuffix
    tags: tags
  }
}

// Deliberately limited to identifiers, endpoint and fingerprint metadata. Secret values,
// shared keys, tokens and connection strings never cross the output boundary.
output resourceGroupName string = resourceGroup().name
output deploymentName string = take('elsa108-${proofName}-${take(planFingerprint, 12)}', 64)
output planFingerprint string = planFingerprint
output workloadIdentityId string = workloadIdentity.outputs.id
output workloadIdentityClientId string = workloadIdentity.outputs.clientId
output workloadIdentityPrincipalId string = workloadIdentity.outputs.principalId
output keyVaultId string = vault.outputs.id
output keyVaultUri string = vault.outputs.uri
output sqlServerId string = database.outputs.id
output sqlServerName string = database.outputs.name
output sqlDatabaseName string = database.outputs.databaseName
output sqlServerFqdn string = database.outputs.fullyQualifiedDomainName
output containerAppsEnvironmentId string = containerEnvironment.outputs.id
output containerAppId string = deployWorkload ? workload!.outputs.id : ''
output containerAppEndpoint string = deployWorkload ? workload!.outputs.endpoint : ''
output immutableImage string = '${imageRepository}@sha256:${toLower(imageDigest)}'
