@secure()
param jobsTableNameSecret string
@secure()
param jobsSourceTableConnectionStringSecret string
@secure()
param jobsDestinationTableConnectionStringSecret string

param backupType string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

var jobsBackupSettings = '{"SourceStorageSetting":{"TargetName":"${jobsTableNameSecret}","StorageConnectionString":"${jobsSourceTableConnectionStringSecret}","StorageType":"table"},"DestinationStorageSetting":{"TargetName":"${jobsTableNameSecret}","StorageConnectionString":"${jobsDestinationTableConnectionStringSecret}","StorageType":"${backupType}"},"Backup":true,"Cleanup":true,"DeleteAfterDays":30}'
var backupSetting = '[ ${jobsBackupSettings} ]'

resource secret 'Microsoft.KeyVault/vaults/secrets@2021-06-01-preview' = {
  name: '${dataKeyVaultName}/maintenanceJobs'
  properties: {
    value: backupSetting
  }
}
