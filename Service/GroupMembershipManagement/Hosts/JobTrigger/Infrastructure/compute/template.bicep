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
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-${substring(uniqueString(subscription().id,'JobTrigger'),0,8)}'

@description('Service plan sku')
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
param instanceMemoryMB int = 2048

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataKeyVaultResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool = false

param featureFlags object = {
  enableTeamsChannel: false
}

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var serviceBusFQN = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var serviceBusSyncJobTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusSyncJobTopic')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var teamsChannelAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppClientId')
var teamsChannelAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppClientSecret')
var teamsChannelAppCertificateName = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppCertificateName')
var teamsChannelAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppTenantId')
var teamsChannelServiceAccountUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountUsername')
var teamsChannelServiceAccountPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountPassword')
var teamsChannelServiceAccountObjectId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountObjectId')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var serviceBusNotificationsQueue = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusNotificationsQueue')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-JobTrigger'
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
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}JobTrigger'
  AzureFunctionsWebHost__hostid: 'JobTrigger'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  jobTriggerSchedule: '0 */5 * * * *'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientCertificateName: '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientSecret: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  graphCredentials__ClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__TenantId: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  graphCredentials__KeyVaultName: prereqsKeyVaultName
  graphCredentials__KeyVaultTenantId: tenantId
  ConnectionStrings__JobsContext: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  ConnectionStrings__JobsContextReadOnly: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  gmmServiceBus__fullyQualifiedNamespace: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  serviceBusSyncJobTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusSyncJobTopic, '2019-09-01').secretUriWithVersion})'
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  TeamsGraphCredentials__ClientCertificateName: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppCertificateName, '2019-09-01').secretUriWithVersion})' : 'not-set'
  TeamsGraphCredentials__ClientSecret: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppClientSecret, '2019-09-01').secretUriWithVersion})' : 'not-set'
  TeamsGraphCredentials__ClientId: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppClientId, '2019-09-01').secretUriWithVersion})' : 'not-set'
  TeamsGraphCredentials__TenantId: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppTenantId, '2019-09-01').secretUriWithVersion})' : 'not-set'
  TeamsGraphCredentials__KeyVaultName: prereqsKeyVaultName
  TeamsGraphCredentials__KeyVaultTenantId: tenantId
  teamsChannelServiceAccountUsername: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountUsername, '2019-09-01').secretUriWithVersion})'
  teamsChannelServiceAccountPassword: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountPassword, '2019-09-01').secretUriWithVersion})'
  teamsChannelServiceAccountObjectId: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountObjectId, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  actionableEmailProviderId: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  serviceBusNotificationsQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  graphCredentials__UserAssignedManagedIdentityClientId: '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
}

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-JobTrigger'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module storageAccountNameReader 'keyVaultReader.bicep' = {
  name: 'storageAccountNameReader-JobTrigger'
  params: {
    value: dataKeyVault.getSecret('jobTriggerStorageAccountProd')
  }
  dependsOn: [
    dataKeyVault
  ]
}

module appPackageContainerNameReader 'keyVaultReader.bicep' = {
  name: 'appPackageContainerNameReader-JobTrigger'
  params: {
    value: dataKeyVault.getSecret('jobTriggerAppPackageContainerProd')
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
  name: 'existingLogAnalyticsWorkspace-jt'
  scope: resourceGroup('${solutionAbbreviation}-data-${environmentAbbreviation}')
  params: {
    environmentAbbreviation: environmentAbbreviation
    resourceGroupClassification: 'data'
    solutionAbbreviation: solutionAbbreviation
  }
}

module functionAppTemplate_JobTrigger 'functionApp.bicep' = {
  name: 'functionAppTemplate-JobTrigger'
  params: {
    name: '${functionAppName}-JobTrigger'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    appSettings: appSettings
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
    logAnalyticsWorkspaceId: existingLogAnalyticsWorkspace.outputs.workspaceId
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultResourceGroup: prereqsKeyVaultResourceGroup
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    setRBACPermissions: setRBACPermissions
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
