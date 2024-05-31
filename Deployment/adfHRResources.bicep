param location string
param environmentAbbreviation string
param solutionAbbreviation string

param tenantId string

var sqlServerName = '${solutionAbbreviation}-data-${environmentAbbreviation}'
module adfForHRData '../Infrastructure/adf/pipeline/template.bicep' = {
  name: 'adfForHRDataTemplate'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    sqlServerName: sqlServerName
  }
}
