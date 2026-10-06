@description('Observability backend: Log Analytics workspace receiving OpenTelemetry exports. Telemetry failure never participates in a business transaction.')
param name string
param location string
param tags object = {}
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Disabled'
  }
}

output workspaceId string = workspace.id
output workspaceName string = workspace.name
output customerId string = workspace.properties.customerId
