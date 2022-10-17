@secure()
param jobsTableNameSecret string

@secure()
param jobsSourceTableConnectionStringSecret string

@secure()
param jobsDestinationTableConnectionStringSecret string

param backupType string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

var maintenanceSettings = '[ { "SourceTableName":"${jobsTableNameSecret}", "SourceConnectionString":"${jobsSourceTableConnectionStringSecret}", "DestinationConnectionString":"${jobsDestinationTableConnectionStringSecret}", "BackupType":"${backupType}", "CleanupOnly":false, "DeleteAfterDays":30 }]'

resource secret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  name: '${dataKeyVaultName}/maintenanceJobs'
  properties: {
    value: maintenanceSettings
  }
}
