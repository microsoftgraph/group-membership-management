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

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Tenant id.')
param tenantId string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

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
param servicePlanSku string = 'Y1'

@description('Resource location.')
param location string

@description('Enter function app name.')
param functionAppName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
])
param functionAppKind string = 'functionapp'

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

@description('Enter application insights name.')
param appInsightsName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Resource group where Application Insights is located.')
param appInsightsResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Enter storage account name.')
param storageAccountName string

@description('Resource group where storage account is located.')
param storageAccountResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsTableName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsTableName')
var storageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'storageAccountConnectionString')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-JobScheduler'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

var appSettings = [
  {
    name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
    value: 1
  }
  {
    name: 'SCM_TOUCH_WEBCONFIG_AFTER_DEPLOYMENT'
    value: 0
  }
  {
    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
    value: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureWebJobsStorage'
    value: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
    value: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'FUNCTIONS_WORKER_RUNTIME'
    value: 'dotnet'
  }
  {
    name: 'FUNCTIONS_EXTENSION_VERSION'
    value: '~4'
  }
  {
    name: 'jobSchedulerSchedule'
    value: '0 0 0 * * Sun'
  }
  {
    name: 'logAnalyticsCustomerId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'logAnalyticsPrimarySharedKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'jobsStorageAccountConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'jobsTableName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsTableName, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
]

var stagingSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-JobScheduler-staging')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}JobSchedulerStaging'
  }
  {
    name: 'AzureWebJobs.JobSchedulerFunction.Disabled'
    value: 1
  }
]

var productionSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-JobScheduler')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}JobScheduler'
  }
  {
    name: 'AzureWebJobs.JobSchedulerFunction.Disabled'
    value: 0
  }
]

module functionAppTemplate_JobScheduler 'functionApp.bicep' = {
  name: 'functionAppTemplate-JobScheduler'
  params: {
    name: '${functionAppName}-JobScheduler'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: union(appSettings, productionSettings)
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_JobScheduler 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-JobScheduler'
  params: {
    name: '${functionAppName}-JobScheduler/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: union(appSettings, stagingSettings)
  }
  dependsOn: [
    functionAppTemplate_JobScheduler
  ]
}

module keyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultPoliciesTemplate-JobScheduler'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobScheduler.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_JobScheduler.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_JobScheduler
    functionAppSlotTemplate_JobScheduler
  ]
}

module PrereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'PrereqsKeyVaultPoliciesTemplate-JobScheduler'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobScheduler.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_JobScheduler.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_JobScheduler
    functionAppSlotTemplate_JobScheduler
  ]
}
