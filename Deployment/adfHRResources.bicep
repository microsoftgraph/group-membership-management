param location string
param environmentAbbreviation string
param solutionAbbreviation string
param sqlAdministratorsGroupId string
param sqlAdministratorsGroupName string

param tenantId string

var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}-hr'
module sqlForHRData '../Infrastructure/adf/sql/template.bicep' = {
  name: 'sqlForHRDataTemplate'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    sqlAdministratorsGroupId: sqlAdministratorsGroupId
    sqlAdministratorsGroupName: sqlAdministratorsGroupName
    tenantId: tenantId
    sqlServerName: sqlServerName
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
  }
  dependsOn: [
    sqlForHRData
  ]
}
