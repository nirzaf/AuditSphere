@description('The AuditSphere production/acceptance topology as one reviewable composition: Angular+API, capability workers, PostgreSQL, observability, migration job, canary-capable revisions. External provider effects exist only in the isolated groups that the worker host validates.')
param location string
param tags object = {}
@description('Deployment environment name: Production or Acceptance. Development defaults never become production configuration.')
@allowed(['Production', 'Acceptance'])
param environmentName string
@description('Log Analytics workspace name for telemetry.')
param observabilityName string
@description('Container Apps managed environment name.')
param containerEnvironmentName string
@description('Virtual network name for private connectivity.')
param networkName string
@description('Address space for the private virtual network, e.g. 10.42.0.0/16.')
param networkAddressPrefix string
@description('Delegated subnet for Container Apps infrastructure.')
param appsSubnetPrefix string
@description('Delegated subnet for PostgreSQL flexible server.')
param postgresSubnetPrefix string
@description('PostgreSQL flexible server name.')
param postgresServerName string
@description('PostgreSQL administrator login.')
param postgresAdministratorLogin string
@description('Key Vault secret name holding the PostgreSQL administrator password.')
param postgresPasswordSecretName string
@description('AuditSphere database name created by the migration job (single schema authority).')
param databaseName string = 'auditsphere'
@description('Existing Azure Container Registry (immutable SHA-tagged images).')
param registryServer string
param registryId string
@description('Key Vault holding deployment secrets; values are provisioned by the approved secret process, never in source.')
param keyVaultName string
@description('Secret name for the full AuditSphere PostgreSQL connection string.')
param connectionSecretName string
@description('Secret name for the Entra ID confidential-client secret used by the API host identity.')
param identityClientSecretName string
@description('Entra tenant and client identifiers for the API host OIDC configuration (non-secret).')
param identityTenantId string
param identityClientId string
@description('Public base URL of the API host (also the OpenTelemetry deployment identity input).')
param publicBaseUrl string
@description('OTLP destination endpoint; telemetry failure never participates in a business transaction.')
param otlpEndpoint string
@description('API host container app name.')
param apiName string
@description('Immutable API image reference (registry/repo:git-sha).')
param apiImage string
@description('Migration job name and image.')
param migrationJobName string
param migrationImage string
@description('Worker image reference (same image for every group).')
param workerImage string
@description('Firm the workers process durable operations for.')
param firmId string
@description('Durable-operation deployment epoch; the release pipeline assigns a strictly increasing positive value.')
@minValue(1)
param deploymentEpoch int
@description('Worker groups to deploy. Core groups always; external-effect groups only where separately approved.')
@allowed(['general', 'processing', 'records', 'mail', 'pbc', 'client-sites'])
param workerGroups array = ['general', 'processing', 'records']
@description('Storage account for approved Angular asset sets (current + previous) enabling rollback-window chunk fallback.')
param uiStorageAccountName string
param uiCurrentShareName string = 'ui-current'
param uiPreviousShareName string = 'ui-previous'

var baseTags = union(tags, { 'auditsphere:environment': environmentName, 'auditsphere:deployment-epoch': string(deploymentEpoch) })

resource managedIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' = {
  name: '${apiName}-identity'
  location: location
  tags: baseTags
}

resource observability 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: observabilityName
  location: location
  tags: baseTags
  properties: {
    sku: { name: 'PerGB2018' }
    publicNetworkAccessForQuery: 'Disabled'
  }
}

resource network 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: networkName
  location: location
  tags: baseTags
  properties: {
    addressSpace: { addressPrefixes: [networkAddressPrefix] }
  }
}

resource appsSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = {
  parent: network
  name: 'apps'
  properties: {
    addressPrefix: appsSubnetPrefix
    delegations: [
      {
        name: 'containerapps'
        properties: { serviceName: 'Microsoft.App/environments' }
      }
    ]
  }
}

resource postgresSubnet 'Microsoft.Network/virtualNetworks/subnets@2024-01-01' = {
  parent: network
  name: 'postgres'
  properties: {
    addressPrefix: postgresSubnetPrefix
    delegations: [
      {
        name: 'postgres'
        properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' }
      }
    ]
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-08-02-preview' = {
  name: containerEnvironmentName
  location: location
  tags: baseTags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: observability.properties.customerId
        sharedKey: observability.listKeys().primarySharedKey
      }
    }
    vnetConfiguration: {
      infrastructureSubnetId: appsSubnet.id
      internal: false
    }
  }
}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: baseTags
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enablePurgeProtection: true
    networkAcls: {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: []
      ipRules: []
    }
  }
}

resource identitySecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, managedIdentity.id, 'Key Vault Secrets User')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: last(split(registryId, '/'))
}

resource identityAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registryId, managedIdentity.id, 'AcrPull')
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: managedIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: postgresServerName
  location: location
  tags: baseTags
  sku: { name: 'Standard_D2ds_v5', tier: 'GeneralPurpose' }
  properties: {
    version: '18'
    administratorLogin: postgresAdministratorLogin
    administratorLoginPassword: '${keyVault.properties.vaultUri}secrets/${postgresPasswordSecretName}'
    network: { delegatedSubnetResourceId: postgresSubnet.id }
    storage: { storageSizeGB: 64, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 14, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'ZoneRedundant' }
  }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: databaseName
  properties: {}
}

resource uiStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: uiStorageAccountName
  location: location
  tags: baseTags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowSharedKeyAccess: true
    networkAcls: { defaultAction: 'Deny', bypass: 'AzureServices' }
  }
}

resource fileService 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: uiStorage
  name: 'default'
}

resource uiCurrentShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: uiCurrentShareName
  properties: {}
}

resource uiPreviousShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: fileService
  name: uiPreviousShareName
  properties: {}
}

resource uiStorageCurrent 'Microsoft.App/managedEnvironments/storages@2024-08-02-preview' = {
  parent: containerEnvironment
  name: uiCurrentShareName
  properties: {
    azureFile: {
      accountName: uiStorage.name
      accountKey: uiStorage.listKeys().keys[0].value
      shareName: uiCurrentShare.name
      accessMode: 'ReadWrite'
    }
  }
}

resource uiStoragePrevious 'Microsoft.App/managedEnvironments/storages@2024-08-02-preview' = {
  parent: containerEnvironment
  name: uiPreviousShareName
  properties: {
    azureFile: {
      accountName: uiStorage.name
      accountKey: uiStorage.listKeys().keys[0].value
      shareName: uiPreviousShare.name
      accessMode: 'ReadWrite'
    }
  }
}

module migrationJob 'migration-job.bicep' = {
  name: 'migration-job'
  params: {
    name: migrationJobName
    location: location
    tags: baseTags
    environmentId: containerEnvironment.id
    image: migrationImage
    registryServer: registryServer
    managedIdentityId: managedIdentity.id
    keyVaultUri: keyVault.properties.vaultUri
    connectionSecretName: connectionSecretName
    environmentName: environmentName
  }
}

module api 'containerapp-api.bicep' = {
  name: 'api'
  params: {
    name: apiName
    location: location
    tags: baseTags
    environmentId: containerEnvironment.id
    image: apiImage
    registryServer: registryServer
    managedIdentityId: managedIdentity.id
    keyVaultUri: keyVault.properties.vaultUri
    connectionSecretName: connectionSecretName
    identityClientSecretName: identityClientSecretName
    identityTenantId: identityTenantId
    identityClientId: identityClientId
    publicBaseUrl: publicBaseUrl
    otlpEndpoint: otlpEndpoint
    uiStorageAccountName: uiStorage.name
    uiCurrentShareName: uiCurrentShareName
    uiPreviousShareName: uiPreviousShareName
    environmentName: environmentName
  }
}

module workers 'containerapp-worker.bicep' = [for workerGroup in workerGroups: {
  name: 'worker-${workerGroup}'
  params: {
    name: '${apiName}-worker-${workerGroup}'
    location: location
    tags: baseTags
    environmentId: containerEnvironment.id
    image: workerImage
    registryServer: registryServer
    managedIdentityId: managedIdentity.id
    keyVaultUri: keyVault.properties.vaultUri
    connectionSecretName: connectionSecretName
    workerGroup: workerGroup
    deploymentEpoch: deploymentEpoch
    firmId: firmId
    environmentName: environmentName
    otlpEndpoint: otlpEndpoint
  }
}]

output apiFqdn string = api.outputs.fqdn
output apiRevision string = api.outputs.latestRevisionName
output appName string = api.outputs.appName
output migrationJobName string = migrationJob.outputs.jobName
output postgresFqdn string = postgres.properties.fullyQualifiedDomainName
output deploymentEpoch int = deploymentEpoch
