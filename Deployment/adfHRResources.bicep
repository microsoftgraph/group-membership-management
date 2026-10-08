param location string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string
param enableFunctionAuthentication bool = false

@description('Number of most recent tables to retain in each of the users, mappings and agents schemas. Cleanup of older users and mappings tables is attempted after all non-cleanup activities succeed, and of older agents tables after the agents load completes.')
@minValue(2)
param tablesToRetain int = 30

var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
var sqlDataBaseName = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

// The ADF SQL database and its key-vault secrets are created by the data template
// (Infrastructure/data/sqlServer.bicep), which also makes the database a member of the SQL
// auto-failover group. This module provisions only the ADF CSV-staging storage account.
module storageForHRData '../Infrastructure/adf/storage/template.bicep' = {
  name: 'storageForHRDataTemplate'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
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
    storageForHRData
  ]
}
