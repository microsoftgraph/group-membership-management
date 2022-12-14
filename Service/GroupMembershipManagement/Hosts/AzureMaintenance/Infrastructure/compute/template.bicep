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
var maintenanceJobs = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'maintenanceJobs')
var storageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'storageAccountConnectionString')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsTableName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsTableName')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-AzureMaintenance'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

var commonSettings = {
  WEBSITE_ADD_SITENAME_BINDINGS_IN_APPHOST_CONFIG: 1
  WEBSITE_ENABLE_SYNC_UPDATE_SITE: 1
  SCM_TOUCH_WEBCONFIG_AFTER_DEPLOYMENT: 0
  FUNCTIONS_WORKER_RUNTIME: 'dotnet'
  FUNCTIONS_EXTENSION_VERSION: '~4'
}

var appSettings = {
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  maintenanceTriggerSchedule: '0 0 0 * * *'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  maintenanceJobs: '@Microsoft.KeyVault(SecretUri=${reference(maintenanceJobs, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  jobsStorageAccountConnectionString: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  jobsTableName: '@Microsoft.KeyVault(SecretUri=${reference(jobsTableName, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientSecret': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:TenantId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:KeyVaultName': prereqsKeyVaultName
  'graphCredentials:KeyVaultTenantId': tenantId
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
}

var stagingSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-AzureMaintenance-staging')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}AzureMaintenanceStaging'
  'AzureWebJobs.StarterFunction.Disabled': 1
  'AzureWebJobs.OrchestratorFunction.Disabled': 1
  'AzureWebJobs.LoggerFunction.Disabled': 1
  'AzureWebJobs.RetrieveBackupsFunction.Disabled': 1
  'AzureWebJobs.ReviewAndDeleteFunction.Disabled': 1
  'AzureWebJobs.TableBackupFunction.Disabled': 1
  'AzureWebJobs.BackUpInactiveJobsFunction.Disabled': 1
  'AzureWebJobs.ReadGroupNameFunction.Disabled': 1
  'AzureWebJobs.ReadSyncJobsFunction.Disabled': 1
  'AzureWebJobs.RemoveBackUpsFunction.Disabled': 1
  'AzureWebJobs.RemoveInactiveJobsFunction.Disabled': 1
  'AzureWebJobs.SendEmailFunction.Disabled': 1
}

var productionSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-AzureMaintenance')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}AzureMaintenance'
  'AzureWebJobs.StarterFunction.Disabled': 0
  'AzureWebJobs.OrchestratorFunction.Disabled': 0
  'AzureWebJobs.LoggerFunction.Disabled': 0
  'AzureWebJobs.RetrieveBackupsFunction.Disabled': 0
  'AzureWebJobs.ReviewAndDeleteFunction.Disabled': 0
  'AzureWebJobs.TableBackupFunction.Disabled': 0
  'AzureWebJobs.BackUpInactiveJobsFunction.Disabled': 0
  'AzureWebJobs.ReadGroupNameFunction.Disabled': 0
  'AzureWebJobs.ReadSyncJobsFunction.Disabled': 0
  'AzureWebJobs.RemoveBackUpsFunction.Disabled': 0
  'AzureWebJobs.RemoveInactiveJobsFunction.Disabled': 0
  'AzureWebJobs.SendEmailFunction.Disabled': 0
}

module functionAppTemplate_AzureMaintenance 'functionApp.bicep' = {
  name: 'functionAppTemplate-AzureMaintenance'
  params: {
    name: '${functionAppName}-AzureMaintenance'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_AzureMaintenance 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-AzureMaintenance'
  params: {
    name: '${functionAppName}-AzureMaintenance/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    functionAppTemplate_AzureMaintenance
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate-AzureMaintenance'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_AzureMaintenance.outputs.msi
        secrets: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_AzureMaintenance.outputs.msi
        secrets: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_AzureMaintenance
    functionAppSlotTemplate_AzureMaintenance
  ]
}
module prereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'prereqsKeyVaultPoliciesTemplate-AzureMaintenance'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_AzureMaintenance.outputs.msi
        secrets: [
          'get'
          'list'
        ]
        certificates: [
          'get'
        ]
      }
      {
        objectId: functionAppSlotTemplate_AzureMaintenance.outputs.msi
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
    functionAppTemplate_AzureMaintenance
    functionAppSlotTemplate_AzureMaintenance
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2022-03-01' = {
  name: '${functionAppName}-AzureMaintenance/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, productionSettings)
  dependsOn: [
    functionAppTemplate_AzureMaintenance
    dataKeyVaultPoliciesTemplate
  ]
}

resource functionAppStagingSettings 'Microsoft.Web/sites/slots/config@2022-03-01' = {
  name: '${functionAppName}-AzureMaintenance/staging/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, stagingSettings)
  dependsOn: [
    functionAppSlotTemplate_AzureMaintenance
    dataKeyVaultPoliciesTemplate
  ]
}
