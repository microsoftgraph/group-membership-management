@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Tenant id.')
param tenantId string

@description('Resource location.')
param location string = 'westus'

@description('Name of the public source branch where webapp repo exists.')
param branch string

resource staticWebApp 'Microsoft.Web/staticSites@2021-03-01' = {
  name: '${solutionAbbreviation}-ui'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
    branch: branch
    buildProperties: {
      appBuildCommand: 'dotnet run'
      appLocation: 'Service/GroupMembershipManagement/Hosts/GmmUI/WebApp'
      outputLocation: 'Service/GroupMembershipManagement/Hosts/GmmUI/WebApp/wwwroot'
      skipGithubActionWorkflowGeneration: true
    }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: 'DevOps'
    repositoryUrl: 'https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public'
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
