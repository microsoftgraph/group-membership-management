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

@description('Instance identifier')
@allowed([
  's1'
  'l1'
])
param instanceIdentifier string

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-${instanceIdentifier}-${substring(uniqueString(subscription().id,'MessageSplitter'),0,6)}'

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

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool = false

@description('Available messageSplitter subscriptions')
param availableMessageSplitterSubscriptions array

var functionFullName = '${functionAppName}-MessageSplitter-${instanceIdentifier}'
var messageSplitterSubscription = filter(availableMessageSplitterSubscriptions, x => x.name == instanceIdentifier)[0].subscription
var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var membershipStorageAccountName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountName')
var membershipContainerName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'membershipContainerName')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var serviceBusFQN = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var serviceBusMembershipUpdatersTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMembershipUpdatersTopic')
var serviceBusMessageSplitterTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMessageSplitterTopic')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-MessageSplitter-${instanceIdentifier}'
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
  FUNCTIONS_WORKER_RUNTIME: 'dotnet-isolated'
  FUNCTIONS_EXTENSION_VERSION: '~4'
}

var appSettings = {
  AzureWebJobsStorage__accountName: storageAccountNameReader.outputs.value
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}MessageSplitter${instanceIdentifier}'
  AzureFunctionsWebHost__hostid: 'MessageSplitter${instanceIdentifier}'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  membershipStorageAccountName: '@Microsoft.KeyVault(SecretUri=${reference(membershipStorageAccountName, '2019-09-01').secretUriWithVersion})'
  membershipContainerName: '@Microsoft.KeyVault(SecretUri=${reference(membershipContainerName, '2019-09-01').secretUriWithVersion})'
  gmmServiceBus__fullyQualifiedNamespace: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  serviceBusMembershipUpdatersTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMembershipUpdatersTopic, '2019-09-01').secretUriWithVersion})'
  serviceBusMessageSplitterTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMessageSplitterTopic, '2019-09-01').secretUriWithVersion})'
  instanceIdentifier: instanceIdentifier
  messageSplitterSubscription: messageSplitterSubscription
  ConnectionStrings__JobsContext: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  ConnectionStrings__JobsContextReadOnly: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
}

var activityFunctionSettings = {
  'AzureWebJobs.StarterFunction.Disabled': 0
}

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-MessageSplitter-${instanceIdentifier}'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module storageAccountNameReader 'keyVaultReader.bicep' = {
  name: 'storageAccountNameReader-MessageSplitter${instanceIdentifier}'
  params: {
    value: dataKeyVault.getSecret('messageSplitter${instanceIdentifier}StorageAccountProd')
  }
  dependsOn: [
    dataKeyVault
  ]
}

resource graphUAMI 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: userAssignedManagedIdentityNameReader.outputs.value
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module existingLogAnalyticsWorkspace 'logAnalyticsWorkspace.bicep' = {
  name: 'existingLogAnalyticsWorkspace-${instanceIdentifier}'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
  params: {
    environmentAbbreviation: environmentAbbreviation
    resourceGroupClassification: 'data'
    solutionAbbreviation: solutionAbbreviation
  }
}

module functionAppTemplate_MessageSplitter 'functionApp.bicep' = {
  name: 'faTemplate-MessageSplitter-${instanceIdentifier}'
  params: {
    name: functionFullName
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    secretSettings: commonSettings
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
    logAnalyticsWorkspaceId: existingLogAnalyticsWorkspace.outputs.workspaceId
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
    instanceIdentifier: instanceIdentifier
    storageAccountName: storageAccountNameReader.outputs.value
  }
  dependsOn: [
    servicePlanTemplate
    graphUAMI
    existingLogAnalyticsWorkspace
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: '${functionFullName}/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, activityFunctionSettings)
  dependsOn: [
    functionAppTemplate_MessageSplitter
  ]
}
