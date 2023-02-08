@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Classify the types of resources in this resource group.')
@allowed([
  'prereqs'
  'data'
  'compute'
])
param resourceGroupClassification string = 'compute'

@description('Tenant id.')
param tenantId string

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi-serviceplan'

@description('Service plan sku')
@allowed([
  'D1'
  'F1'
  'B1'
  'B2'
  'B3'
  'S1'
  'S2'
  'S3'
  'P1'
  'P2'
  'P3'
  'P1V2'
  'P2V2'
  'P3V2'
  'I1'
  'I2'
  'I3'
  'Y1'
])
param servicePlanSku string = 'F1'

@description('Resource location.')
param location string

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Enter application insights name.')
param appInsightsName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

var webapiClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webapiClientId')
var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  scope: resourceGroup(dataResourceGroup)
  name: appInsightsName
}

var appSettings = [
  {
    name: 'Settings:AppConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
    {
    name: 'AzureAd:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(webapiClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:Audience'
    value: '@Microsoft.KeyVault(SecretUri=${reference(webapiClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:TenantId'
    value: tenantId
  }
  {
    name: 'AzureAd:Instance'
    value: environment().authentication.loginEndpoint
  }
  {
    name: 'ApplicationInsights:ConnectionString'
    value: appInsights.properties.ConnectionString
  }
  {
    name: 'logAnalyticsCustomerId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'logAnalyticsPrimarySharedKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  }
]

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-WebAPI'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

module appService 'appService.bicep' = {
  name: 'appServiceTemplate-WebAPI'
  params:{
    name: '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi'
    location: location
    servicePlanName: servicePlanName
    appSettings: appSettings
  }
  dependsOn:[
    appInsights
  ]
}

