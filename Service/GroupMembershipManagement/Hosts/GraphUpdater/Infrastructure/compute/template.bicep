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

@description('Instance identifier')
@allowed([
  ''
  'small'
  'large'
])
param instanceIdentifier string = ''
var instanceSuffix = empty(instanceIdentifier) ? '' : '-${instanceIdentifier}'
@description('Service plan name.')
var servicePlanName  = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}${instanceSuffix}-${substring(uniqueString(subscription().id,'GraphUpdater'),0,8)}'

@description('Service plan sku')
@allowed([
  'FC1'
])
param servicePlanSku string = 'FC1'

@description('Resource location.')
param location string

@description('Enter function app name.')
param functionAppName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Function app kind.')
@allowed([
  'functionapp'
  'linux'
  'container'
  'functionapp,linux'
])
param functionAppKind string = 'functionapp,linux'

@description('Maximum instance count.')
param maxInstanceCount int = 40

@description('Instance memory in MB.')
param instanceMemoryMB int = 4096

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

@description('Object with flags to determine behaviour')
param featureFlags object = {
  skipListingFunctionAppKeys : false
}

@description('Number of concurrent add requests to the Graph API.')
@minValue(1)
@maxValue(10)
param concurrentAddRequests int = 1

@description('Number of concurrent remove requests to the Graph API.')
@minValue(1)
@maxValue(10)
param concurrentRemoveRequests int = 1

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var membershipStorageAccountName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountName')
var membershipContainerName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'membershipContainerName')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var serviceBusFQN = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var serviceBusMembershipUpdatersTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMembershipUpdatersTopic')
var serviceBusMessageSplitterTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMessageSplitterTopic')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var serviceBusNotificationsQueue = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusNotificationsQueue')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-GraphUpdater${instanceSuffix}'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
  }
}

var triggerSchedule = instanceIdentifier == 'small' ? '*/10 * * * * *' : instanceIdentifier == 'large' ? '*/20 * * * * *' :  '*/30 * * * * *'
var triggerDelay = 0

var appSettings = {
  AZURE_TOKEN_CREDENTIALS: 'ManagedIdentityCredential'
  AzureWebJobsStorage__accountName: storageAccountNameReader.outputs.value
  AzureWebJobsStorage__credential: 'managedidentity'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}GraphUpdater${instanceIdentifier}'
  AzureFunctionsWebHost__hostid: 'GraphUpdater${instanceIdentifier}'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientCertificateName: '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientSecret: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__TenantId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__KeyVaultName: prereqsKeyVaultName
  graphCredentials__KeyVaultTenantId: tenantId
  ConnectionStrings__JobsContext: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  ConnectionStrings__JobsContextReadOnly: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  membershipStorageAccountName: '@Microsoft.KeyVault(SecretUri=${reference(membershipStorageAccountName, '2019-09-01').secretUriWithVersion})'
  membershipContainerName: '@Microsoft.KeyVault(SecretUri=${reference(membershipContainerName, '2019-09-01').secretUriWithVersion})'
  actionableEmailProviderId: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  gmmServiceBus__fullyQualifiedNamespace: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  serviceBusMembershipUpdatersTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMembershipUpdatersTopic, '2019-09-01').secretUriWithVersion})'
  serviceBusMessageSplitterTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMessageSplitterTopic, '2019-09-01').secretUriWithVersion})'
  serviceBusNotificationsQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  graphCredentials__UserAssignedManagedIdentityClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
  instanceIdentifier: instanceIdentifier
  triggerSchedule: triggerSchedule
  triggerDelay: triggerDelay
  concurrentAddRequests: concurrentAddRequests
  concurrentRemoveRequests: concurrentRemoveRequests
  GraphUpdater__UpdateBatchSize: 400
  'AzureWebJobs.StarterFunction.Disabled': instanceIdentifier == '' ? 0 : 1
  'AzureWebJobs.StarterFunction_small.Disabled': instanceIdentifier == 'small' ? 0 : 1
  'AzureWebJobs.StarterFunction_large.Disabled': instanceIdentifier == 'large' ? 0 : 1
}

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-GraphUpdater${instanceSuffix}'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module storageAccountNameReader 'keyVaultReader.bicep' = {
  name: 'storageAccountNameReader-GraphUpdater${instanceSuffix}'
  params: {
    value: dataKeyVault.getSecret('graphUpdater${instanceIdentifier}StorageAccountProd')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module appPackageContainerNameReader 'keyVaultReader.bicep' = {
  name: 'appPackageContainerNameReader-GraphUpdater${instanceSuffix}'
  params: {
    value: dataKeyVault.getSecret('graphUpdater${instanceIdentifier}AppPackageContainerProd')
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
  name: 'existingLogAnalyticsWorkspace-gu${instanceSuffix}'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
  params: {
    environmentAbbreviation: environmentAbbreviation
    resourceGroupClassification: 'data'
    solutionAbbreviation: solutionAbbreviation
  }
}

module functionAppTemplate_GraphUpdater 'functionApp.bicep' = {
  name: 'functionAppTemplate-GraphUpdater${instanceSuffix}'
  params: {
    name: functionAppName
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    appSettings: appSettings
    functionAuthAppClientId: functionAuthAppClientId
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
    logAnalyticsWorkspaceId: existingLogAnalyticsWorkspace.outputs.workspaceId
    featureFlags: featureFlags
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
    instanceIdentifier: instanceIdentifier
    storageAccountName: storageAccountNameReader.outputs.value
    appPackageContainerName: appPackageContainerNameReader.outputs.value
    maxInstanceCount: maxInstanceCount
    instanceMemoryMB: instanceMemoryMB
  }
  dependsOn: [
    servicePlanTemplate
    graphUAMI
  ]
}
