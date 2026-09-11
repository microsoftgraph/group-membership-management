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

@description('ADF SQL SKU Name. Must be consistent with the tier/family/capacity params. Supported combinations: Basic/Basic/\'\'/5, S0|S1|S2/Standard/\'\'/10|20|50, GP_S_Gen5/GeneralPurpose/Gen5/<vCores>.')
param adfSqlSkuName string = 'S0'

@description('ADF SQL SKU Tier')
param adfSqlSkuTier string = 'Standard'

@description('ADF SQL SKU Family. Leave empty for DTU-based SKUs.')
param adfSqlSkuFamily string = ''

@description('ADF SQL SKU Capacity')
param adfSqlSkuCapacity int = 10

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
    name: adfSqlSkuName
    tier: adfSqlSkuTier
    family: adfSqlSkuFamily
    capacity: adfSqlSkuCapacity
  }
}

module secureKeyvaultSecrets 'keyVaultSecretsSecure.bicep' = {
  name: 'adfSqlSecureKeyvaultSecrets'
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
