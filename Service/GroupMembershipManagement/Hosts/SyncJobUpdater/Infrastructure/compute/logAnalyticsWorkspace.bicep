@minLength(2)
@maxLength(3)
@description('Enter an abbreviation for the solution.')
param solutionAbbreviation string

@minLength(4)
@maxLength(7)
@description('Enter an abbreviation for the solution.')
param resourceGroupClassification string

@minLength(2)
@maxLength(6)
@description('Enter an abbreviation for the environment.')
param environmentAbbreviation string

param logAnalyticsName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2021-06-01' existing = {
  name: logAnalyticsName
}

output workspaceId string = logAnalyticsWorkspace.id
