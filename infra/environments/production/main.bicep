targetScope = 'resourceGroup'

param location string = resourceGroup().location
param environmentName string = 'Production'
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
@description('Name prefix, e.g. auditsphere-prod.')
param namePrefix string = 'auditsphere-prod'

module stack '../../modules/stack.bicep' = {
  name: 'auditsphere-stack'
  params: {
    location: location
    environmentName: environmentName
    observabilityName: '${namePrefix}-logs'
    containerEnvironmentName: '${namePrefix}-env'
    networkName: '${namePrefix}-vnet'
    networkAddressPrefix: '10.42.0.0/16'
    appsSubnetPrefix: '10.42.0.0/20'
    postgresSubnetPrefix: '10.42.16.0/24'
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
    workerGroups: ['general', 'processing', 'records']
    uiStorageAccountName: '${namePrefix}uiassets'
  }
}

output apiFqdn string = stack.outputs.apiFqdn
output migrationJobName string = stack.outputs.migrationJobName
