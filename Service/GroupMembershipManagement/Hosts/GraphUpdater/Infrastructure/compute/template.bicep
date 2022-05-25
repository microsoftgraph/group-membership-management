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

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Service plan sku')
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

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

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
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var syncCompletedCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncCompletedCCEmailAddresses')
var syncDisabledCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncDisabledCCEmailAddresses')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var membershipStorageAccountName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountName')
var membershipContainerName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'membershipContainerName')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

var appSettings = [
  {
    name: 'WEBSITE_ADD_SITENAME_BINDINGS_IN_APPHOST_CONFIG'
    value: 1
  }
  {
    name: 'WEBSITE_RUN_FROM_PACKAGE'
    value: 1
  }
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
    value: reference(resourceId(appInsightsResourceGroup, 'microsoft.insights/components/', appInsightsName), '2015-05-01').InstrumentationKey
  }
  {
    name: 'AzureWebJobsStorage'
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
  }
  {
    name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
  }
  {
    name: 'FUNCTIONS_WORKER_RUNTIME'
    value: 'dotnet'
  }
  {
    name: 'FUNCTIONS_EXTENSION_VERSION'
    value: '~3'
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
    name: 'graphCredentials:ClientSecret'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:TenantId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:KeyVaultName'
    value: prereqsKeyVaultName
  }
  {
    name: 'graphCredentials:KeyVaultTenantId'
    value: tenantId
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
    name: 'senderAddress'
    value: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'senderPassword'
    value: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'syncCompletedCCEmailAddresses'
    value: '@Microsoft.KeyVault(SecretUri=${reference(syncCompletedCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'syncDisabledCCEmailAddresses'
    value: '@Microsoft.KeyVault(SecretUri=${reference(syncDisabledCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'supportEmailAddresses'
    value: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT'
    value: maximumElasticWorkerCount
  }
  {
    name: 'appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
  {
    name: 'membershipStorageAccountName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(membershipStorageAccountName, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'membershipContainerName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(membershipContainerName, '2019-09-01').secretUriWithVersion})'
  }
]

var stagingSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-GraphUpdater-staging')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}GraphUpdaterStaging'
  }
  {
    name: 'AzureWebJobs.StarterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.OrchestratorFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupUpdaterSubOrchestratorFunction.Disabled'
    value: 1
  }  
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.FileDownloaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupOwnersReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupUpdaterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupValidatorFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.JobReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.LoggerFunction.Disabled'
    value: 1
  }
]

var productionSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-GraphUpdater')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}GraphUpdater'
  }
  {
    name: 'AzureWebJobs.StarterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.OrchestratorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupUpdaterSubOrchestratorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.FileDownloaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupOwnersReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupUpdaterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupValidatorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.JobReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.LoggerFunction.Disabled'
    value: 0
  }
]

module functionAppTemplate_GraphUpdater 'functionApp.bicep' = {
  name: 'functionAppTemplate-GraphUpdater'
  params: {
    name: '${functionAppName}-GraphUpdater'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    secretSettings: union(appSettings, productionSettings)
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_GraphUpdater 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-GraphUpdater'
  params: {
    name: '${functionAppName}-GraphUpdater/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    secretSettings: union(appSettings, stagingSettings)
  }
  dependsOn: [
    functionAppTemplate_GraphUpdater
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_GraphUpdater.outputs.msi
        permissions: [
          'get'
          'list'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_GraphUpdater.outputs.msi
        permissions: [
          'get'
          'list'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_GraphUpdater
    functionAppSlotTemplate_GraphUpdater
  ]
}

module PrereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'PrereqsKeyVaultPoliciesTemplate'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_GraphUpdater.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_GraphUpdater.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_GraphUpdater
    functionAppSlotTemplate_GraphUpdater
  ]
}
