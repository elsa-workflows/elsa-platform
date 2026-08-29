@description('Deterministic Container App name.')
@minLength(2)
@maxLength(32)
param name string

@description('Azure region for the app.')
param location string

@description('Container Apps environment resource ID.')
param managedEnvironmentId string

@description('Name of the existing valenceruntimeimages ACR.')
@minLength(5)
@maxLength(50)
param registryName string = 'valenceruntimeimages'

@description('Subscription ID containing the existing ACR.')
param registrySubscriptionId string = subscription().subscriptionId

@description('Resource group containing the existing ACR.')
@minLength(1)
param registryResourceGroupName string

@description('Immutable container repository, without a tag or digest.')
param imageRepository string

@description('Lower-case SHA-256 digest without the sha256: prefix.')
@minLength(64)
@maxLength(64)
param imageDigest string

@description('User-assigned identity resource ID used for ACR pull and Key Vault reads.')
param workloadIdentityId string

@description('Key Vault URI for the SQL connection secret.')
param sqlConnectionSecretUri string

@description('Key Vault URI for the Elsa identity signing secret.')
param signingKeySecretUri string

@description('Key Vault URI for the disposable proof administrator password.')
param adminPasswordSecretUri string

@description('SQL Key Vault reference name used inside Container Apps.')
@minLength(1)
@maxLength(63)
param sqlRef string = 'sql-connection'

@description('Signing Key Vault reference name used inside Container Apps.')
@minLength(1)
@maxLength(63)
param signingRef string = 'identity-signing-key'

@description('Administrator password Key Vault reference name used inside Container Apps.')
@minLength(1)
@maxLength(63)
param adminCredentialRef string = 'admin-password'

@description('Disposable proof administrator username.')
@minLength(1)
@maxLength(128)
param adminUsername string = 'proof-admin'

@description('Elsa version represented by the immutable image.')
@minLength(1)
param elsaVersion string = '3.8'

@description('Exact Nuplane feed version for Elsa SQL Server workflow/identity persistence packages.')
@minLength(1)
param sqlWorkflowPackageVersion string = '3.8.0-preview.5413'

@description('Exact Nuplane feed version for Elsa Quartz SQL Server scheduling package.')
@minLength(1)
param sqlQuartzPackageVersion string = '3.8.0-preview.342'

@description('Elsa topology represented by the immutable image.')
@allowed([
  'combined'
])
param topology string = 'combined'

@description('Deterministic revision suffix derived from the plan fingerprint.')
@minLength(8)
@maxLength(63)
param revisionSuffix string

@description('Tags applied to the app.')
param tags object = {}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
  scope: resourceGroup(registrySubscriptionId, registryResourceGroupName)
}
var immutableImage = '${imageRepository}@sha256:${toLower(imageDigest)}'
var nuplaneFeedEnvironment = [
  {
    name: 'Nuplane__Setup__Feeds__0__Name'
    value: 'local-packages'
  }
  {
    name: 'Nuplane__Setup__Feeds__0__DirectoryPath'
    value: 'packages'
  }
  {
    name: 'Nuplane__Setup__Feeds__0__IncludePatterns__0'
    value: '*'
  }
  {
    name: 'Nuplane__Setup__Feeds__0__Directory__Watch'
    value: 'true'
  }
  {
    name: 'Nuplane__Setup__Feeds__0__Directory__DebounceWindow'
    value: '00:00:01'
  }
  {
    name: 'Nuplane__Setup__Feeds__1__Name'
    value: 'nuget.org'
  }
  {
    name: 'Nuplane__Setup__Feeds__1__ServiceIndex'
    value: 'https://api.nuget.org/v3/index.json'
  }
  {
    name: 'Nuplane__Setup__Feeds__2__Name'
    value: 'feedz.io'
  }
  {
    name: 'Nuplane__Setup__Feeds__2__ServiceIndex'
    value: 'https://f.feedz.io/elsa-workflows/elsa-3/nuget/index.json'
  }
  {
    name: 'Nuplane__Setup__Feeds__2__IncludePatterns__0'
    value: 'Elsa.Persistence.EFCore.SqlServer [${sqlWorkflowPackageVersion}]'
  }
  {
    name: 'Nuplane__Setup__Feeds__2__IncludePatterns__1'
    value: 'Elsa.Scheduling.Quartz.EFCore.SqlServer [${sqlQuartzPackageVersion}]'
  }
]
var featureEnvironment = [
  {
    name: 'CShells__Shells__Default__Features__SqliteWorkflowPersistence'
    value: 'false'
  }
  {
    name: 'CShells__Shells__Default__Features__SqliteIdentityPersistence'
    value: 'false'
  }
  {
    name: 'CShells__Shells__Default__Features__QuartzSqlite'
    value: 'false'
  }
  {
    name: 'CShells__Shells__Default__Features__SqlServerWorkflowPersistence__ConnectionString'
    secretRef: sqlRef
  }
  {
    name: 'CShells__Shells__Default__Features__SqlServerIdentityPersistence__ConnectionString'
    secretRef: sqlRef
  }
  {
    name: 'CShells__Shells__Default__Features__QuartzSqlServer__ConnectionString'
    secretRef: sqlRef
  }
  {
    name: 'CShells__Shells__Default__Features__Identity__SigningKey'
    secretRef: signingRef
  }
  {
    name: 'CShells__Shells__Default__Features__DefaultAdminUser__AdminUsername'
    value: adminUsername
  }
  {
    name: 'CShells__Shells__Default__Features__DefaultAdminUser__AdminPassword'
    secretRef: adminCredentialRef
  }
]

resource app 'Microsoft.App/containerApps@2023-05-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workloadIdentityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: managedEnvironmentId
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: workloadIdentityId
        }
      ]
      secrets: [
        {
          name: sqlRef
          keyVaultUrl: sqlConnectionSecretUri
          identity: workloadIdentityId
        }
        {
          name: signingRef
          keyVaultUrl: signingKeySecretUri
          identity: workloadIdentityId
        }
        {
          name: adminCredentialRef
          keyVaultUrl: adminPasswordSecretUri
          identity: workloadIdentityId
        }
      ]
    }
    template: {
      revisionSuffix: revisionSuffix
      containers: [
        {
          name: topology
          image: immutableImage
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ASPNETCORE_HTTP_PORTS'
              value: '8080'
            }
            {
              name: 'ELSA_VERSION'
              value: elsaVersion
            }
            {
              name: 'ELSA_TOPOLOGY'
              value: topology
            }
          ], concat(nuplaneFeedEnvironment, featureEnvironment))
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/alive'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 30
              timeoutSeconds: 5
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 10
              failureThreshold: 6
              timeoutSeconds: 5
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/alive'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 30
              periodSeconds: 30
              failureThreshold: 3
              timeoutSeconds: 5
            }
          ]
        }
      ]
      scale: {
        minReplicas: 0
        maxReplicas: 1
      }
    }
  }
}

output id string = app.id
output name string = app.name
output fqdn string = app.properties.configuration.ingress.fqdn
output endpoint string = 'https://${app.properties.configuration.ingress.fqdn}'
output immutableImage string = immutableImage
