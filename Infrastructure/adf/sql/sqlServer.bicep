@description('Resource location.')
param location string

@description('Name of SQL Server')
param sqlServerName string

@description('Name of ADF SQL Database')
param adfSqlDatabaseName string

@description('Name of Jobs SQL Database')
param jobsSqlDatabaseName string

@description('Data Key vault name.')
param dataKeyVaultName string

var sqlServerUrl = 'Server=tcp:${sqlServerName}${environment().suffixes.sqlServerHostname},1433;'
var adfSqlServerDataBaseCatalog = 'Initial Catalog=${adfSqlDatabaseName};'
var jobsSqlDataBaseCatalog = 'Initial Catalog=${jobsSqlDatabaseName};'
var sqlServerAdditionalSettings = 'MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=90;'

resource sqlServer 'Microsoft.Sql/servers@2022-11-01-preview' existing = {
  name: sqlServerName
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2021-02-01-preview' = {
  name: adfSqlDatabaseName
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
          name: 'sqlDatabaseConnectionString'
          value: '${sqlServerUrl}${jobsSqlDataBaseCatalog}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerConnectionString'
          value: '${sqlServerUrl}${adfSqlServerDataBaseCatalog}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerBasicConnectionString'
          value: '${sqlServerUrl}${adfSqlServerDataBaseCatalog}${sqlServerAdditionalSettings}'
        }
        {
          name: 'sqlServerMSIConnectionString'
          value: '${sqlServerUrl}${adfSqlServerDataBaseCatalog}Authentication=Active Directory Default;TrustServerCertificate=True;Encrypt=True;'
        }
        {
          name: 'sqlServerName'
          value: '${sqlServerName}${environment().suffixes.sqlServerHostname}'
        }
        {
          name: 'sqlServerDataBaseName'
          value: adfSqlDatabaseName
        }
      ]
    }
  }
  dependsOn: [
    sqlDatabase
  ]
}
