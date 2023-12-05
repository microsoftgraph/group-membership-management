@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string = 'dh'

@description('Resource location.')
param location string

@description('Name of the public source branch where webapp repo exists.')
param branch string

@description('Repository URL.')
param repositoryUrl string

@description('customDomainName')
param customDomainName string

@description('Enter application insights name.')
param appInsightsName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

resource appInsightsResource 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

var appSettings = {
  REACT_APP_APPINSIGHTS_CONNECTIONSTRING: appInsightsResource.properties.ConnectionString
  REACT_APP_TEST_SETTING: 'test setting'
}

resource staticWebApp 'Microsoft.Web/staticSites@2022-03-01' = {
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
    repositoryUrl: repositoryUrl
    stagingEnvironmentPolicy: 'Disabled'
  }
}

resource symbolicname 'Microsoft.Web/staticSites/config@2022-09-01' = {
  name: 'appsettings'
  kind: 'string'
  parent: staticWebApp
  properties: appSettings
}

resource customDomain 'Microsoft.Web/staticSites/customDomains@2022-03-01' = if (!empty(customDomainName)) {
  parent: staticWebApp
  name: !empty(customDomainName) ? customDomainName : 'blank' // https://github.com/Azure/bicep/issues/1754
  properties: {}
}

// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
