param location string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string

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
  }
  dependsOn: [
    sqlForHRData
  ]
}
