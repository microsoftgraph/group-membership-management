@minLength(2)
@maxLength(3)
@description('Enter an abbreviation for the solution.')
param solutionAbbreviation string

@minLength(2)
@maxLength(6)
@description('Enter an abbreviation for the environment.')
param environmentAbbreviation string

@description('Resource location.')
param location string

@description('Tenant Id.')
param tenantId string

@description('Name of storage account that stores adf data')
param storageAccountName string = '${solutionAbbreviation}${environmentAbbreviation}adf'

@description('Enter storage account sku.')
@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_ZRS'
  'Premium_LRS'
])
param storageAccountSku string = 'Standard_LRS'

@description('Name of blob container')
param storageAccountContainerName string = 'csvcontainer'

@description('Name of SQL Server')
param sqlServerName string

@description('Name of ADF  SQL Server')
param adfSqlDataBaseName string

@description('Name of Jobs SQL Server')
param jobsSqlDataBaseName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('ADF SQL SKU Name. Must be consistent with the tier/family/capacity params. Supported combinations: Basic/Basic/\'\'/5, S0|S1|S2/Standard/\'\'/10|20|50, GP_S_Gen5/GeneralPurpose/Gen5/<vCores>.')
param adfDBSqlSkuName string = 'S0'

@description('ADF SQL SKU Tier')
param adfDBSqlSkuTier string = 'Standard'

@description('ADF SQL SKU Family. Leave empty for DTU-based SKUs.')
param adfDBSqlSkuFamily string = ''

@description('ADF SQL SKU Capacity')
param adfDBSqlSkuCapacity int = 10

var dataKeyVaultName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

module sqlServer 'sqlServer.bicep' =  {
  name: 'adfSqlServerTemplate'
  params: {
    location: location
    sqlServerName: sqlServerName
    adfSqlDatabaseName: adfSqlDataBaseName
    jobsSqlDatabaseName: jobsSqlDataBaseName
    dataKeyVaultName: dataKeyVaultName
    adfDBSqlSkuName: adfDBSqlSkuName
    adfDBSqlSkuTier: adfDBSqlSkuTier
    adfDBSqlSkuFamily: adfDBSqlSkuFamily
    adfDBSqlSkuCapacity: adfDBSqlSkuCapacity
  }
}

module storageAccountTemplate 'storageAccount.bicep' = {
  name: 'storageAccountTemplate'
  params: {
    storageAccountName: storageAccountName
    containerName: storageAccountContainerName
    sku: storageAccountSku
    keyVaultName: dataKeyVaultName
    location: location
  }
}
