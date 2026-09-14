param location string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string
param enableFunctionAuthentication bool = false

@description('ADF SQL SKU Name. Must be consistent with the tier/family/capacity params. Supported combinations: Basic/Basic/\'\'/5, S0|S1|S2/Standard/\'\'/10|20|50, GP_S_Gen5/GeneralPurpose/Gen5/<vCores>.')
param adfDBSqlSkuName string = 'S0'

@description('ADF SQL SKU Tier')
param adfDBSqlSkuTier string = 'Standard'

@description('ADF SQL SKU Family. Leave empty for DTU-based SKUs.')
param adfDBSqlSkuFamily string = ''

@description('ADF SQL SKU Capacity')
param adfDBSqlSkuCapacity int = 10

@description('Number of most recent tables to retain in each of the users and mappings schemas. Cleanup of older tables is attempted after all non-cleanup activities succeed.')
@minValue(2)
param tablesToRetain int = 30

var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlDataBaseName = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

module sqlForHRData '../Infrastructure/adf/sql/template.bicep' = {
  name: 'sqlForHRDataTemplate'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    sqlServerName: sqlServerName
    adfSqlDataBaseName: sqlDataBaseName
    adfDBSqlSkuName: adfDBSqlSkuName
    adfDBSqlSkuTier: adfDBSqlSkuTier
    adfDBSqlSkuFamily: adfDBSqlSkuFamily
    adfDBSqlSkuCapacity: adfDBSqlSkuCapacity
  }
}

module adfForHRData '../Infrastructure/adf/pipeline/template.bicep' = {
  name: 'adfForHRDataTemplate'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    sqlServerName: sqlServerName
    sqlDatabaseName: sqlDataBaseName
    functionAuthAppClientId: functionAuthAppClientId
    enableFunctionAuthentication: enableFunctionAuthentication
    tablesToRetain: tablesToRetain
  }
  dependsOn: [
    sqlForHRData
  ]
}
