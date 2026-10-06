@description('One AuditSphere worker deployment per capability group (general, processing, records, mail, pbc, client-sites). The same worker image is configured into each role; only the approved external-effect groups ever receive provider credentials, and the API never does.')
param name string
param location string
param tags object = {}
param environmentId string
@description('Immutable worker image reference tagged with the release Git commit SHA; identical to the API build except no Angular assets.')
param image string
param registryServer string
param managedIdentityId string
param keyVaultUri string
param connectionSecretName string
@description('Worker capability group: general | processing | records | mail | pbc | client-sites. Must match the approved worker compositions the host validates at startup.')
@allowed(['general', 'processing', 'records', 'mail', 'pbc', 'client-sites'])
param workerGroup string
@description('Durable-operation deployment epoch. Positive and strictly increasing per release; assigned by the release pipeline, never ambiguous.')
@minValue(1)
param deploymentEpoch int
@description('Firm the worker processes durable operations for.')
param firmId string
param environmentName string
param otlpEndpoint string
@description('Only these groups may enable external effects, and only in the isolated Acceptance composition the host validates.')
@allowed([false, true])
param externalEffectsEnabled bool = false
@description('Optional provider-specific secret references for the approved external-effect groups; ordinary workers must pass none. Values stay in the approved Key Vault; source holds references only.')
param providerSecrets array = []

var externalEffects = externalEffectsEnabled && (workerGroup == 'mail' || workerGroup == 'pbc' || workerGroup == 'client-sites')

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
      activeRevisionsMode: 'Single'
      registries: [
        { server: registryServer, identity: managedIdentityId }
      ]
      secrets: union(
        [
          {
            name: 'connection-string'
            keyVaultUrl: '${keyVaultUri}secrets/${connectionSecretName}'
            identity: managedIdentityId
          }
        ],
        providerSecrets)
    }
    template: {
      containers: [
        {
          name: 'worker-${workerGroup}'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName }
            { name: 'ConnectionStrings__AuditSphere', secretRef: 'connection-string' }
            { name: 'Worker__Group', value: workerGroup }
            { name: 'Worker__FirmId', value: firmId }
            { name: 'Worker__DeploymentEpoch', value: string(deploymentEpoch) }
            { name: 'Telemetry__Otlp__Endpoint', value: otlpEndpoint }
            { name: 'ExternalEffects__Enabled', value: string(externalEffects) }
            { name: 'AllowSimulationAdapters', value: 'false' }
          ]
          // Workers are BackgroundService hosts with no HTTP listener: no probe can health-check
          // them, so replica crash restarts are the platform signal. A liveness port would create
          // a permanent restart loop.
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output appId string = app.id
output appName string = app.name
output workerGroup string = workerGroup
output deploymentEpoch int = deploymentEpoch
