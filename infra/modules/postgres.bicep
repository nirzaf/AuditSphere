@description('PostgreSQL 18 Flexible Server: the single PostgreSQL authority for AuditSphere. TLS enforced, backups with point-in-time recovery, no application-side migration at startup (migrations run as a dedicated job).')
param name string
param location string
param tags object = {}
@description('PostgreSQL major version; the local development standard is 18.')
@allowed([18])
param postgresVersion int = 18
@description('Administrator login name; the password is referenced from the approved secret store.')
param administratorLogin string
@description('Key Vault secret id holding the administrator password.')
param administratorPasswordSecretUri string
@description('Backup retention in days.')
@minValue(7)
@maxValue(35)
param backupRetentionDays int = 14
@description('Subnet id delegated to Microsoft.DBforPostgreSQL/flexibleServers for private access.')
param delegatedSubnetId string
@description('Maintenance window day of week (0 = Sunday).')
param maintenanceDay int = 6

resource server 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: name
  location: location
  tags: tags
  sku: {
    name: 'Standard_D2ds_v5'
    tier: 'GeneralPurpose'
  }
  properties: {
    version: '${postgresVersion}'
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPasswordSecretUri
    network: {
      delegationResourceIds: [delegatedSubnetId]
    }
    storage: {
      storageSizeGB: 64
      autoGrow: 'Enabled'
    }
    backup: {
      backupRetentionDays: backupRetentionDays
      geoRedundantBackup: 'Disabled'
    }
    highAvailability: {
      mode: 'ZoneRedundant'
    }
    maintenanceWindow: {
      dayOfWeek: maintenanceDay
      startHour: 2
      startMinute: 0
    }
  }
}

@description('Databases are created by idempotent deployment scripts after the server exists; the AuditSphere database holds one schema authority.')
output serverId string = server.id
output serverFqdn string = server.properties.fullyQualifiedDomainName
