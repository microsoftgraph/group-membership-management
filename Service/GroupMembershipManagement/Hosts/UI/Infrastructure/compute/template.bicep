@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Resource location.')
param location string

@description('Name of the public source branch where webapp repo exists.')
param branch string

@description('Repository URL.')
param repositoryUrl string

@description('customDomainName')
param customDomainName string


var appInsightsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

resource appInsightsResource 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
}

var hiddenLinkTags = {
  'hidden-link: /app-insights-resource-id': appInsightsResource.id
  'hidden-link: /app-insights-instrumentation-key': appInsightsResource.properties.InstrumentationKey
  'hidden-link: /app-insights-conn-string': appInsightsResource.properties.ConnectionString
}

var appSettings = {
  REACT_APP_APPINSIGHTS_CONNECTIONSTRING: appInsightsResource.properties.ConnectionString
  REACT_APP_TEST_SETTING: 'test setting'
}

module staticSiteModule 'staticSite.bicep' = {
  name: '${solutionAbbreviation}-ui'
  params: {
    solutionAbbreviation: solutionAbbreviation
    location: location
    branch: branch
    repositoryUrl: repositoryUrl
    customDomainName: customDomainName
    appSettings: appSettings
    tags: hiddenLinkTags
  }
}

// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = staticSiteModule.outputs.deployment_token
