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
param customDomainName string = ''

@description('The name of the Azure Static Web App resource.')
param staticWebAppName string = '${solutionAbbreviation}-ui'

@description('The URI of the api app service.')
param apiServiceBaseUri string

@description('The name of the data resource group to deploy the resources to.')
param dataResourceGroupName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('The name of the compute resource group to deploy the resources to.')
param computeResourceGroupName string = '${solutionAbbreviation}-compute-${environmentAbbreviation}'

@description('The provider that submitted the last deployment.')
param provider string = 'DevOps'

var appInsightsName = '${solutionAbbreviation}-data-${environmentAbbreviation}'

resource appInsightsResource 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
  scope: resourceGroup(dataResourceGroupName)
}

var hiddenLinkTags = {
  'hidden-link: /app-insights-resource-id': appInsightsResource.id
  'hidden-link: /app-insights-instrumentation-key': appInsightsResource.properties.InstrumentationKey
  'hidden-link: /app-insights-conn-string': appInsightsResource.properties.ConnectionString
}

module staticSiteModule 'staticSite.bicep' = {
  name: staticWebAppName
  scope: resourceGroup(computeResourceGroupName)
  params: {
    solutionAbbreviation: solutionAbbreviation
    location: location
    branch: branch
    repositoryUrl: repositoryUrl
    customDomainName: customDomainName
    staticWebAppName: staticWebAppName
    tags: hiddenLinkTags
    provider: provider
  }
}

// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = staticSiteModule.outputs.deployment_token
output app_insights_connection_string string = appInsightsResource.properties.ConnectionString
output api_service_base_uri string = apiServiceBaseUri
