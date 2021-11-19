@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter tenant Id.')
param tenantId string

@description('Enter storage account name.')
param storageAccountName string

@description('Whether to back up to table or blob storage.')
@allowed([
  'table'
  'blob'
])
param backupType string = 'table'

resource dataKeyVault 'Microsoft.KeyVault/vaults@2019-09-01' existing = {
  name: '${solutionAbbreviation}-data-${environmentAbbreviation}'
  scope: resourceGroup(subscription().subscriptionId, '${solutionAbbreviation}-data-${environmentAbbreviation}' )
}

module settingBuilder 'settingsBuilder.bicep' = {
  name: 'backupSettingsBuilder'
  params: {
    backupType: backupType
    jobsDestinationTableConnectionStringSecret: dataKeyVault.getSecret('jobsStorageAccountConnectionString')
    jobsSourceTableConnectionStringSecret:dataKeyVault.getSecret('jobsStorageAccountConnectionString')
    jobsTableNameSecret: dataKeyVault.getSecret('jobsTableName')
  }
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate'
  params: {
    keyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    keyVaultParameters: [
      {
        name: 'tablesToBackup'
        value: settingBuilder.outputs.backupSettings
      }
    ]
  }
  dependsOn: [
    settingBuilder
  ]
}
