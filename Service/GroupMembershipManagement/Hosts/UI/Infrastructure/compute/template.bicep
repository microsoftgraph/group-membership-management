@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Resource location.')
param location string

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
      appLocation: 'UI/web-app'
      skipGithubActionWorkflowGeneration: true
    }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: 'DevOps'
    repositoryUrl: 'https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public'
    stagingEnvironmentPolicy: 'Disabled'
  }
}

// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
