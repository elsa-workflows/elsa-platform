targetScope = 'resourceGroup'

@description('Short, unique name for the isolated restore target.')
@minLength(3)
@maxLength(16)
param targetName string

@allowed([
  'westeurope'
  'northeurope'
  'swedencentral'
])
param location string = 'westeurope'

@description('Immutable repository name without a tag or digest.')
param imageRepository string = 'valenceruntimeimages.azurecr.io/runtime-combined'

@description('Lower-case SHA-256 image digest without the sha256 prefix.')
@minLength(64)
@maxLength(64)
param imageDigest string

param registryName string = 'valenceruntimeimages'
param registrySubscriptionId string = subscription().subscriptionId
param registryResourceGroupName string
param bootstrapObjectId string
param restoredDatabaseId string

@minLength(64)
@maxLength(64)
param recoveryPointDigest string

@minLength(64)
@maxLength(64)
param templateFingerprint string

param deployWorkload bool = true
param expiryUtc string
param owner string = 'elsa-control'
param adminUsername string = 'proof-admin'
param elsaVersion string = '3.8'
param sqlWorkflowPackageVersion string = '3.8.0-preview.5413'
param sqlQuartzPackageVersion string = '3.8.0-preview.342'
@minLength(1)
@maxLength(127)
param sqlConnectionSecretName string = 'sql-connection'

@minLength(1)
@maxLength(127)
param signingKeySecretName string = 'identity-signing-key'

@minLength(1)
@maxLength(127)
param adminPasswordSecretName string = 'admin-password'

var planInput = 'proof=129|template=${toLower(templateFingerprint)}|target=${targetName}|location=${location}|image=${imageRepository}@sha256:${toLower(imageDigest)}|database=${toLower(restoredDatabaseId)}|point=${toLower(recoveryPointDigest)}|elsa=${elsaVersion}|sql-workflow=${sqlWorkflowPackageVersion}|sql-quartz=${sqlQuartzPackageVersion}'
var planFingerprint = uniqueString(planInput)
var tags = {
  proof: '129'
  owner: owner
  expiry: expiryUtc
  'managed-by': 'elsa-control-bicep'
  'recovery-role': 'target'
  'recovery-point': take(toLower(recoveryPointDigest), 32)
  'plan-fingerprint': planFingerprint
}

module workloadIdentity 'modules/identity.bicep' = {
  name: 'recovery-target-identity'
  params: {
    name: '${targetName}-identity'
    location: location
    tags: tags
  }
}

module observability 'modules/observability.bicep' = {
  name: 'recovery-target-observability'
  params: {
    name: '${targetName}-logs'
    location: location
    tags: tags
  }
}

module vault 'modules/key-vault.bicep' = {
  name: 'recovery-target-key-vault'
  params: {
    name: '${targetName}-kv'
    location: location
    workloadPrincipalId: workloadIdentity.outputs.principalId
    bootstrapObjectId: bootstrapObjectId
    sqlConnectionSecretName: sqlConnectionSecretName
    signingKeySecretName: signingKeySecretName
    adminPasswordSecretName: adminPasswordSecretName
    tags: tags
  }
}

module containerEnvironment 'modules/container-apps-environment.bicep' = {
  name: 'recovery-target-environment'
  params: {
    name: '${targetName}-aca'
    location: location
    logAnalyticsWorkspaceName: observability.outputs.name
    tags: tags
  }
}

module workload 'modules/container-app.bicep' = if (deployWorkload) {
  name: 'recovery-target-app'
  params: {
    name: '${targetName}-app'
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
    adminPasswordSecretUri: vault.outputs.adminCredentialUri
    sqlRef: take(sqlConnectionSecretName, 63)
    signingRef: take(signingKeySecretName, 63)
    adminCredentialRef: take(adminPasswordSecretName, 63)
    adminUsername: adminUsername
    elsaVersion: elsaVersion
    sqlWorkflowPackageVersion: sqlWorkflowPackageVersion
    sqlQuartzPackageVersion: sqlQuartzPackageVersion
    revisionSuffix: take(planFingerprint, 24)
    tags: tags
  }
}

// Safe provider-boundary evidence only. Secret values and connection strings are
// supplied after the foundation deployment and never enter ARM parameters/outputs.
output planFingerprint string = planFingerprint
output workloadIdentityId string = workloadIdentity.outputs.id
output workloadIdentityClientId string = workloadIdentity.outputs.clientId
output workloadIdentityPrincipalId string = workloadIdentity.outputs.principalId
output keyVaultId string = vault.outputs.id
output keyVaultName string = vault.outputs.name
output containerAppsEnvironmentId string = containerEnvironment.outputs.id
output containerAppId string = deployWorkload ? workload!.outputs.id : ''
output containerAppEndpoint string = deployWorkload ? workload!.outputs.endpoint : ''
output immutableImage string = '${imageRepository}@sha256:${toLower(imageDigest)}'
