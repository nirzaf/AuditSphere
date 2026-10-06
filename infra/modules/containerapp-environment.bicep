@description('Managed Container Apps environment shared by the API, every worker role and the migration job. No Kubernetes cluster is operated; this is the platform-native runtime for the modular monolith roles.')
param name string
param location string
param tags object = {}
@description('Log Analytics workspace resource id for platform and application telemetry.')
param logAnalyticsWorkspaceId string

resource environment 'Microsoft.App/managedEnvironments@2024-08-02-preview' = {
  name: name
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: reference(logAnalyticsWorkspaceId, '2023-09-01').customerId
        sharedKey: listKeys(logAnalyticsWorkspaceId, '2023-09-01').primarySharedKey
      }
    }
  }
}

output environmentId string = environment.id
