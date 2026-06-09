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
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-${substring(uniqueString(subscription().id,'Notifier'),0,8)}'

@description('App service name.')
param appServiceName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi'

@description('Enter the hostname for the api')
param apiHostname string = '${appServiceName}.azurewebsites.net'

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

@description('Name of the resource group where the \'data\' resources are located.')
param dataResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool = false

@description('Object with flags to determine behaviour')
param featureFlags object = {
  skipListingFunctionAppKeys : false
}

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var serviceBusNotificationsQueue = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusNotificationsQueue')
var serviceBusFailedNotificationsQueue = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFailedNotificationsQueue')
var serviceBusFQN = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-Notifier'
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
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}Notifier'
  AzureFunctionsWebHost__hostid: 'Notifier'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  notifierTriggerSchedule: '0 */5 * * * *'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  jobsStorageAccountConnectionString: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  graphCredentials__ClientSecret: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__TenantId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__KeyVaultName: prereqsKeyVaultName
  graphCredentials__KeyVaultTenantId: tenantId
  ConnectionStrings__JobsContext: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  ConnectionStrings__JobsContextReadOnly: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  actionableEmailProviderId: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  apiHostname: apiHostname
  serviceBusNotificationsQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  serviceBusFailedNotificationsQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFailedNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  gmmServiceBus__fullyQualifiedNamespace: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  graphCredentials__UserAssignedManagedIdentityClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
}


resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-Notifier'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module storageAccountNameReader 'keyVaultReader.bicep' = {
  name: 'storageAccountNameReader-Notifier'
  params: {
    value: dataKeyVault.getSecret('functionsStorageAccountName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

var appPackageContainerName = 'notifier-app-package'


resource graphUAMI 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: userAssignedManagedIdentityNameReader.outputs.value
  scope: resourceGroup(dataResourceGroup)
}

module existingLogAnalyticsWorkspace 'logAnalyticsWorkspace.bicep' = {
  name: 'existingLogAnalyticsWorkspace-notifier'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
  params: {
    environmentAbbreviation: environmentAbbreviation
    resourceGroupClassification: 'data'
    solutionAbbreviation: solutionAbbreviation
  }
}

module functionAppTemplate_Notifier 'functionApp.bicep' = {
  name: 'functionAppTemplate-Notifier'
  params: {
    name: '${functionAppName}-Notifier'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataResourceGroup
    appSettings: appSettings
    appPackageContainerName: appPackageContainerName
    functionAuthAppClientId: functionAuthAppClientId
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
    logAnalyticsWorkspaceId: existingLogAnalyticsWorkspace.outputs.workspaceId
    featureFlags: featureFlags
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
    storageAccountName: storageAccountNameReader.outputs.value
    dataResourceGroup: dataResourceGroup
  }
  dependsOn: [
    servicePlanTemplate
    graphUAMI
  ]
}

