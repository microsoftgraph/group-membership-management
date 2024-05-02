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

@description('Name of the Azure Data Factory resource.')
param dataFactoryName string = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

@description('Name of the Azure Data Factory pipeline.')
param adfPipeline string

var subscriptionId = subscription().subscriptionId
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var webapiClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webapiClientId')
var webApiTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webApiTenantId')
var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var sqlServerBasicConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'sqlServerBasicConnectionString')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')

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
    value: '@Microsoft.KeyVault(SecretUri=${reference(webApiTenantId, '2019-09-01').secretUriWithVersion})'
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
    name: 'ConnectionStrings:JobsContextReadOnly'
    value: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'ConnectionStrings:JobsContext'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
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
  {
    name: 'Settings:SqlServerConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(sqlServerBasicConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'ADF:Pipeline'
    value: adfPipeline
  }
  {
    name: 'ADF:DataFactoryName'
    value: dataFactoryName
  }
  {
    name: 'ADF:SubscriptionId'
    value: subscriptionId
  }
  {
    name: 'ADF:ResourceGroup'
    value: dataResourceGroup
  }
  {
    name: 'Settings:GraphUserAssignedManagedIdentityClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
  }
]

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-WebApi'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

resource graphUAMI 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: userAssignedManagedIdentityNameReader.outputs.value
  scope: resourceGroup(dataResourceGroup)
}

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
    dataKeyVaultName: dataKeyVaultName
    dataResourceGroup: dataResourceGroup
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsResourceGroup: prereqsResourceGroup
    tenantId: tenantId
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
  }
  dependsOn: [
    appInsights
    servicePlanTemplate
    graphUAMI
  ]
}
