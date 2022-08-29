@secure()
param jobsTableNameSecret string

@secure()
param jobsSourceTableConnectionStringSecret string

@secure()
param jobsDestinationTableConnectionStringSecret string

param backupType string

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string

var backupSetting = '[ { "SourceTableName":"${jobsTableNameSecret}", "SourceConnectionString":"${jobsSourceTableConnectionStringSecret}", "DestinationConnectionString":"${jobsDestinationTableConnectionStringSecret}", "BackupType":"${backupType}", "CleanupOnly":false, "DeleteAfterDays":30 }]'

module secureSecretsTemplate 'keyVaultSecretsSecure.bicep' = {
  name: 'secureSecretsTemplate-GraphUpdater'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecret: {
      name: 'tablesToBackup'
      value: backupSetting
    }
  }
}
