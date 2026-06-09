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

@description('Function authentication app client id.')
param functionAuthAppClientId string

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-${substring(uniqueString(subscription().id,'JobScheduler'),0,8)}'

@description('Service plan sku')
param servicePlanSku string = 'FC1'

@description('Resource location.')
param location string

@description('Enter function app name.')
param functionAppName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Function app kind.')
param functionAppKind string = 'functionapp,linux'

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool = false

@description('Object with flags to determine behaviour')
param featureFlags object = {
  skipListingFunctionAppKeys : false
}

@description('CRON schedule for triggering this function app.')
param timerTriggerSchedule string = '0 0 0 * * *'

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-JobScheduler'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
  }
}

var appSettings = {
  AZURE_TOKEN_CREDENTIALS: 'ManagedIdentityCredential'
  AzureWebJobsStorage__accountName: storageAccountNameReader.outputs.value
  AzureWebJobsStorage__credential: 'managedidentity'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}JobScheduler'
  AzureFunctionsWebHost__hostid: 'JobScheduler'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  jobSchedulerSchedule: timerTriggerSchedule
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  ConnectionStrings__JobsContext: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  ConnectionStrings__JobsContextReadOnly: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
}

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module storageAccountNameReader 'keyVaultReader.bicep' = {
  name: 'storageAccountNameReader-JobScheduler'
  params: {
    value: dataKeyVault.getSecret('functionsStorageAccountName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

var appPackageContainerName = 'jobscheduler-app-package'


module existingLogAnalyticsWorkspace 'logAnalyticsWorkspace.bicep' = {
  name: 'existingLogAnalyticsWorkspace-js'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
  params: {
    environmentAbbreviation: environmentAbbreviation
    resourceGroupClassification: 'data'
    solutionAbbreviation: solutionAbbreviation
  }
}

module functionAppTemplate_JobScheduler 'functionApp.bicep' = {
  name: 'functionAppTemplate-JobScheduler'
  params: {
    name: '${functionAppName}-JobScheduler'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    appSettings: appSettings
    userManagedIdentities: {}
    logAnalyticsWorkspaceId: existingLogAnalyticsWorkspace.outputs.workspaceId
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
    storageAccountName: storageAccountNameReader.outputs.value
    appPackageContainerName: appPackageContainerName
    instanceMemoryMB: 4096
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
  }
  dependsOn: [
    servicePlanTemplate
    existingLogAnalyticsWorkspace
  ]
}
