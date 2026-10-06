targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environmentName string = 'Acceptance'
param deploymentEpoch int
param apiImage string
param workerImage string
param migrationImage string
param publicBaseUrl string
param otlpEndpoint string
param identityTenantId string
param identityClientId string
param firmId string
param registryServer string
param registryId string
@description('External-effect worker groups deployed here; each stays isolated from API credentials.')
param externalWorkerGroups array = ['mail', 'pbc', 'client-sites']
@description('Name prefix distinguishing this environment, e.g. auditsphere-acc.')
param namePrefix string = 'auditsphere-acc'

module stack '../../modules/stack.bicep' = {
  name: 'auditsphere-stack'
  params: {
    location: location
    environmentName: environmentName
    observabilityName: '${namePrefix}-logs'
    containerEnvironmentName: '${namePrefix}-env'
    networkName: '${namePrefix}-vnet'
    networkAddressPrefix: '10.43.0.0/16'
    appsSubnetPrefix: '10.43.0.0/20'
    postgresSubnetPrefix: '10.43.16.0/24'
    postgresServerName: '${namePrefix}-pg'
    postgresAdministratorLogin: 'auditsphere_admin'
    postgresPasswordSecretName: 'postgres-admin-password'
    registryServer: registryServer
    registryId: registryId
    keyVaultName: '${namePrefix}-kv'
    connectionSecretName: 'auditsphere-connection-string'
    identityClientSecretName: 'identity-client-secret'
    identityTenantId: identityTenantId
    identityClientId: identityClientId
    publicBaseUrl: publicBaseUrl
    otlpEndpoint: otlpEndpoint
    apiName: '${namePrefix}-api'
    apiImage: apiImage
    migrationJobName: '${namePrefix}-migrations'
    migrationImage: migrationImage
    workerImage: workerImage
    firmId: firmId
    deploymentEpoch: deploymentEpoch
    workerGroups: union(['general', 'processing', 'records'], externalWorkerGroups)
    uiStorageAccountName: '${namePrefix}uiassets'
  }
}

output apiFqdn string = stack.outputs.apiFqdn
output migrationJobName string = stack.outputs.migrationJobName
