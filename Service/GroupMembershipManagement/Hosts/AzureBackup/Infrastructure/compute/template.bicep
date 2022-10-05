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

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var tablesToBackup = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'tablesToBackup')
var storageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'storageAccountConnectionString')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-AzureBackup'
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
  backupTriggerSchedule: '0 0 0 * * *'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  tablesToBackup: '@Microsoft.KeyVault(SecretUri=${reference(tablesToBackup, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
}

var stagingSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-AzureBackup-staging')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}AzureBackupStaging'
  'AzureWebJobs.StarterFunction.Disabled': 1
  'AzureWebJobs.OrchestratorFunction.Disabled': 1
  'AzureWebJobs.LoggerFunction.Disabled': 1
  'AzureWebJobs.RetrieveBackupsFunction.Disabled': 1
  'AzureWebJobs.ReviewAndDeleteFunction.Disabled': 1
  'AzureWebJobs.TableBackupFunction.Disabled': 1
}

var productionSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-AzureBackup')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}AzureBackup'
  'AzureWebJobs.StarterFunction.Disabled': 0
  'AzureWebJobs.OrchestratorFunction.Disabled': 0
  'AzureWebJobs.LoggerFunction.Disabled': 0
  'AzureWebJobs.RetrieveBackupsFunction.Disabled': 0
  'AzureWebJobs.ReviewAndDeleteFunction.Disabled': 0
  'AzureWebJobs.TableBackupFunction.Disabled': 0
}

module functionAppTemplate_AzureBackup 'functionApp.bicep' = {
  name: 'functionAppTemplate-AzureBackup'
  params: {
    name: '${functionAppName}-AzureBackup'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_AzureBackup 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-AzureBackup'
  params: {
    name: '${functionAppName}-AzureBackup/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    functionAppTemplate_AzureBackup
  ]
}

module keyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultPoliciesTemplate-AzureBackup'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_AzureBackup.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_AzureBackup.outputs.msi
        permissions: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_AzureBackup
    functionAppSlotTemplate_AzureBackup
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2022-03-01' = {
  name: '${functionAppName}-AzureBackup/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, productionSettings)
  dependsOn: [
    functionAppTemplate_AzureBackup
    keyVaultPoliciesTemplate
  ]
}

resource functionAppStagingSettings 'Microsoft.Web/sites/slots/config@2022-03-01' = {
  name: '${functionAppName}-AzureBackup/staging/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, stagingSettings)
  dependsOn: [
    functionAppSlotTemplate_AzureBackup
    keyVaultPoliciesTemplate
  ]
}
