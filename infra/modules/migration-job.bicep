@description('Dedicated migration job: applies pending EF Core migrations from the immutable migration bundle image BEFORE the new API/worker revision receives traffic. Application containers never run migrations at startup. Job failure blocks the release.')
param name string
param location string
param tags object = {}
param environmentId string
@description('Migration bundle image built from the same commit as the runtime artifacts.')
param image string
param registryServer string
param managedIdentityId string
param keyVaultUri string
param connectionSecretName string
param environmentName string

resource job 'Microsoft.App/jobs@2024-08-02-preview' = {
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
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      registries: [
        { server: registryServer, identity: managedIdentityId }
      ]
      secrets: [
        {
          name: 'connection-string'
          keyVaultUrl: '${keyVaultUri}secrets/${connectionSecretName}'
          identity: managedIdentityId
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrations'
          image: image
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: environmentName }
            { name: 'ConnectionStrings__AuditSphere', secretRef: 'connection-string' }
          ]
        }
      ]
    }
  }
}

output jobId string = job.id
output jobName string = job.name
