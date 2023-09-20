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

@description('App service name.')
param appServiceName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi'

@description('Enter the hostname for the api')
param apiHostname string = '${appServiceName}.azurewebsites.net'

@description('Service plan sku')
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

var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var webapiClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webapiClientId')
var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  scope: resourceGroup(dataResourceGroup)
  name: appInsightsName
}

var appSettings = [
  {
    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
    value:'@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
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
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'development'
  }
  {
    name: 'ConnectionStrings:JobsContext'
    value: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
  {
    name: 'Settings:logAnalyticsCustomerId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:logAnalyticsPrimarySharedKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:jobsStorageAccountConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientCertificateName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientSecret'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:TenantId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ActionableEmailProviderId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ApiHostname'
    value: apiHostname
  }
  {
    name: 'Settings:GraphCredentials:KeyVaultName'
    value: prereqsKeyVaultName
  }
  {
    name: 'Settings:GraphCredentials:KeyVaultTenantId'
    value: tenantId
  }
]

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-WebApi'
  params: {
    environmentAbbreviation: environmentAbbreviation
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

module appService 'appService.bicep' = {
  name: 'appServiceTemplate-WebApi'
  params: {
    name: appServiceName
    location: location
    servicePlanName: servicePlanName
    appSettings: appSettings
  }
  dependsOn: [
    appInsights
    servicePlanTemplate
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate-WebApi'
  scope: resourceGroup(dataResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: appService.outputs.principalId
        secrets: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    appService
  ]
}

module prereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'prereqsKeyVaultPoliciesTemplate-WebApi'
  scope: resourceGroup(prereqsResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: appService.outputs.principalId
        secrets: [
          'get'
          'list'
        ]
        certificates: [
          'get'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    appService
  ]
}
