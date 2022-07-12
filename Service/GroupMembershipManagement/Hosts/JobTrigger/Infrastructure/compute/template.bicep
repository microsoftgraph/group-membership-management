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
var serviceBusConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusConnectionString')
var serviceBusSyncJobTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusSyncJobTopic')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var syncDisabledCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncDisabledCCEmailAddresses')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-JobTrigger'
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
    value: '~4'
  }
  {
    name: 'jobTriggerSchedule'
    value: '0 */5 * * * *'
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
    name: 'serviceBusConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'serviceBusSyncJobTopic'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusSyncJobTopic, '2019-09-01').secretUriWithVersion})'
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
    name: 'syncDisabledCCEmailAddresses'
    value: '@Microsoft.KeyVault(SecretUri=${reference(syncDisabledCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'supportEmailAddresses'
    value: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
]

var stagingSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-JobTrigger-staging')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}JobTriggerStaging'
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
    name: 'AzureWebJobs.SubOrchestratorFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupVerifierFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.SyncJobsReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.TopicMessageSenderFunction.Disabled'
    value: 1
  }
]

var productionSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-JobTrigger')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}JobTrigger'
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
    name: 'AzureWebJobs.SubOrchestratorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupVerifierFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.SyncJobsReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.TopicMessageSenderFunction.Disabled'
    value: 0
  }
]

module functionAppTemplate_JobTrigger 'functionApp.bicep' = {
  name: 'functionAppTemplate-JobTrigger'
  params: {
    name: '${functionAppName}-JobTrigger'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: union(appSettings, productionSettings)
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_JobTrigger 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-JobTrigger'
  params: {
    name: '${functionAppName}-JobTrigger/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: union(appSettings, stagingSettings)
  }
  dependsOn: [
    functionAppTemplate_JobTrigger
  ]
}

module keyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultPoliciesTemplate-JobTrigger'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobTrigger.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_JobTrigger.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_JobTrigger
    functionAppSlotTemplate_JobTrigger
  ]
}

module PrereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'PrereqsKeyVaultPoliciesTemplate-JobTrigger'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobTrigger.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_JobTrigger.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_JobTrigger
    functionAppSlotTemplate_JobTrigger
  ]
}
