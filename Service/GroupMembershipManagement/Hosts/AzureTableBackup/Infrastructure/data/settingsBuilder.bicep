@secure()
param jobsTableNameSecret string
@secure()
param jobsSourceTableConnectionStringSecret string
@secure()
param jobsDestinationTableConnectionStringSecret string
param backupType string

var backupSetting = '[ { "SourceTableName":"${jobsTableNameSecret}", "SourceConnectionString":"${jobsSourceTableConnectionStringSecret}", "DestinationConnectionString":"${jobsDestinationTableConnectionStringSecret}", "BackupType":"${backupType}", "CleanupOnly":false, "DeleteAfterDays":30 }]'

output backupSettings string = backupSetting
