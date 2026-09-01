targetScope = 'resourceGroup'

@description('Existing SQL logical server that owns both source and restored databases.')
param serverName string

@description('Exact immutable ARM identity of the source database.')
param sourceDatabaseId string

@description('Name of the isolated point-in-time restored database.')
param targetDatabaseName string

@description('Provider-confirmed UTC point selected while the source workload was quiesced.')
param restorePointUtc string

@description('Canonical digest of the sealed recovery manifest.')
@minLength(64)
@maxLength(64)
param recoveryManifestDigest string

param recoveryId string
param expiryUtc string
param location string = resourceGroup().location

resource server 'Microsoft.Sql/servers@2023-08-01' existing = {
  name: serverName
}

resource restoredDatabase 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: server
  name: targetDatabaseName
  location: location
  tags: {
    proof: '129'
    owner: 'elsa-control'
    'recovery-id': recoveryId
    'target-role': 'restore'
    'managed-by': 'elsa-control-recovery'
    'manifest-digest': 'sha256:${toLower(recoveryManifestDigest)}'
    'recovery-point-utc': restorePointUtc
    expiry: expiryUtc
  }
  properties: {
    createMode: 'PointInTimeRestore'
    sourceDatabaseId: sourceDatabaseId
    restorePointInTime: restorePointUtc
  }
}

// The SQL GET surface does not retain createMode/source/restorePoint after the
// restore completes. These immutable deployment outputs remain the provider
// request/provenance record and are verified before the target is admitted.
output restoredDatabaseId string = restoredDatabase.id
output sourceDatabaseId string = sourceDatabaseId
output restorePointUtc string = restorePointUtc
output createMode string = 'PointInTimeRestore'
output recoveryManifestDigest string = 'sha256:${toLower(recoveryManifestDigest)}'
