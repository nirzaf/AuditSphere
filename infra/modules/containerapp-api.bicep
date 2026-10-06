@description('The AuditSphere API container app: Angular assets, authentication, antiforgery, the /api surface, health probes and the OpenAPI contract outside production. It never receives credentials intended for privileged Microsoft worker roles.')
param name string
param location string
param tags object = {}
param environmentId string
@description('Immutable runtime image reference, tagged with the release Git commit SHA.')
param image string
param registryServer string
@description('Managed identity resource id used for ACR pull and Key Vault secret reads.')
param managedIdentityId string
param keyVaultUri string
@description('Key Vault secret names (created by the approved secret provisioning process, never in source).')
param connectionSecretName string
param identityClientSecretName string
param publicBaseUrl string
param otlpEndpoint string
@description('Azure Files storage account + share names serving the current and previous approved Angular asset sets for rollback-window chunk fallback.')
param uiStorageAccountName string
param uiCurrentShareName string
param uiPreviousShareName string
param environmentName string
@description('Entra workforce tenant and confidential-client id (non-secret); the client secret arrives by Key Vault reference.')
param identityTenantId string
param identityClientId string
var appPort = 8080

resource app 'Microsoft.App/containerApps@2024-08-02-preview' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${managedIdentityId}': {} }
  }
  properties: {
    environmentId: environmentId
    configuration: {
      activeRevisionsMode: 'Multiple'
      ingress: {
        external: true
        targetPort: appPort
        transport: 'auto'
        allowInsecure: false
      }
      registries: [
        {
          server: registryServer
          identity: managedIdentityId
        }
      ]
      secrets: [
        {
          name: 'connection-string'
          keyVaultUrl: '${keyVaultUri}secrets/${connectionSecretName}'
          identity: managedIdentityId
        }
        {
          name: 'identity-client-secret'
          keyVaultUrl: '${keyVaultUri}secrets/${identityClientSecretName}'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: image
          resources: {
            cpu: json('1.0')
            memory: '2Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName }
            { name: 'ASPNETCORE_URLS', value: 'http://+:${appPort}' }
            { name: 'ConnectionStrings__AuditSphere', secretRef: 'connection-string' }
            { name: 'Identity__ClientSecret', secretRef: 'identity-client-secret' }
            { name: 'Identity__TenantId', value: identityTenantId }
            { name: 'Identity__ClientId', value: identityClientId }
            { name: 'Application__PublicBaseUrl', value: publicBaseUrl }
            { name: 'Telemetry__Otlp__Endpoint', value: otlpEndpoint }
            // External effects stay disabled for the API host; live provider effects run only
            // in their isolated Acceptance workers.
            { name: 'ExternalEffects__Enabled', value: 'false' }
            { name: 'DevelopmentIdentity__Enabled', value: 'false' }
            { name: 'Application__AllowSimulationAdapters', value: 'false' }
            // Approved Angular asset sets: current build plus the retained previous build so
            // existing browser tabs keep loading fingerprinted chunks during the transition.
            { name: 'AngularUi__BuildPath', value: '/app/ui-current' }
            { name: 'AngularUi__PreviousBuildPath', value: '/app/ui-previous' }
          ]
          volumeMounts: [
            { volumeName: 'ui-current', mountPath: '/app/ui-current' }
            { volumeName: 'ui-previous', mountPath: '/app/ui-previous' }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: appPort, scheme: 'HTTP' }
              initialDelaySeconds: 10
              periodSeconds: 15
              failureThreshold: 3
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/ready', port: appPort, scheme: 'HTTP' }
              initialDelaySeconds: 5
              periodSeconds: 10
              failureThreshold: 3
            }
          ]
        }
      ]
      volumes: [
        {
          name: 'ui-current'
          storageType: 'AzureFile'
          storageName: '${uiStorageAccountName}-${uiCurrentShareName}'
        }
        {
          name: 'ui-previous'
          storageType: 'AzureFile'
          storageName: '${uiStorageAccountName}-${uiPreviousShareName}'
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 3
      }
    }
  }
}

@description('Traffic canary and rollback are revision operations: deploy a new revision, split 10% traffic, promote or revert by name.')
output appId string = app.id
output appName string = app.name
output latestRevisionName string = app.properties.latestRevisionName
output fqdn string = app.properties.configuration.ingress.fqdn
