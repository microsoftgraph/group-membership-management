@description('Resource location.')
param location string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of SQL Database')
param sqlDatabaseName string

@description('Data Key vault name.')
param dataKeyVaultName string

var sqlServerUrl = 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;'
var sqlServerDataBaseName = 'Initial Catalog=${sqlDatabaseName};'
var sqlServerAdditionalSettings = 'MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;'

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' existing = {
  name: sqlServerName
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2021-02-01-preview' = {
  name: sqlDatabaseName
  parent: sqlServer
  location: location
  sku: {
    name: 'Basic'
    tier: 'Basic'
    family: ''
    capacity: 0
  }
}

module secureKeyvaultSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'secureKeyvaultSecrets'
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultSecrets: {
      secrets: [
        {
          name: 'sqlServerBasicConnectionStringADF'
          value: '${sqlServerUrl}${sqlServerDataBaseName}${sqlServerAdditionalSettings}'
        }
      ]
    }
  }
  dependsOn: [
    sqlDatabase
  ]
}
