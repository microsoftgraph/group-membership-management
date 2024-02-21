@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Resource location.')
param location string

@description('Name of the public source branch where webapp repo exists.')
param branch string

@description('Repository URL.')
param repositoryUrl string

@description('customDomainName')
param customDomainName string

@description('The tags for the resource.')
param tags object

@description('The provider that submitted the last deployment.')
param provider string = 'DevOps'

resource staticWebApp 'Microsoft.Web/staticSites@2022-03-01' = {
  name: '${solutionAbbreviation}-ui'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  tags: tags
  properties: {
    allowConfigFileUpdates: true
    branch: branch
    buildProperties: {
      appLocation: 'UI/web-app'
      skipGithubActionWorkflowGeneration: true
    }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: provider
    repositoryUrl: repositoryUrl
    stagingEnvironmentPolicy: 'Disabled'
  }
}

resource customDomain 'Microsoft.Web/staticSites/customDomains@2022-03-01' = if (!empty(customDomainName)) {
  parent: staticWebApp
  name: !empty(customDomainName) ? customDomainName : 'blank' // https://github.com/Azure/bicep/issues/1754
  properties: {}
}

// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
