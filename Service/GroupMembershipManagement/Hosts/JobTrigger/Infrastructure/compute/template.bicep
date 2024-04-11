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
var serviceBusFQN = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var serviceBusSyncJobTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusSyncJobTopic')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var teamsChannelServiceAccountUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountUsername')
var teamsChannelServiceAccountPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountPassword')
var teamsChannelServiceAccountObjectId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountObjectId')
var syncDisabledCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncDisabledCCEmailAddresses')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var serviceBusNotificationsQueue = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusNotificationsQueue')
var jobTriggerStorageAccountProd = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobTriggerStorageAccountProd')
var jobTriggerStorageAccountStaging = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobTriggerStorageAccountStaging')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-JobTrigger'
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
  WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: '@Microsoft.KeyVault(SecretUri=${reference(jobTriggerStorageAccountProd, '2019-09-01').secretUriWithVersion})'
  WEBSITE_CONTENTSHARE: toLower('functionApp-JobTrigger')
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  jobTriggerSchedule: '0 */5 * * * *'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientCertificateName': '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientSecret': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:TenantId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:KeyVaultName': prereqsKeyVaultName
  'graphCredentials:KeyVaultTenantId': tenantId
  'ConnectionStrings:JobsContext': '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  'ConnectionStrings:JobsContextReadOnly': '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  gmmServiceBus__fullyQualifiedNamespace: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  serviceBusSyncJobTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusSyncJobTopic, '2019-09-01').secretUriWithVersion})'
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  teamsChannelServiceAccountUsername: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountUsername, '2019-09-01').secretUriWithVersion})'
  teamsChannelServiceAccountPassword: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountPassword, '2019-09-01').secretUriWithVersion})'
  teamsChannelServiceAccountObjectId: '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountObjectId, '2019-09-01').secretUriWithVersion})'
  syncDisabledCCEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(syncDisabledCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  actionableEmailProviderId: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  serviceBusNotificationsQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:UserAssignedManagedIdentityClientId': '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
}

var stagingSettings = {
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(jobTriggerStorageAccountStaging, '2019-09-01').secretUriWithVersion})'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}JobTriggerStaging'
  'AzureWebJobs.StarterFunction.Disabled': 1
  'AzureWebJobs.OrchestratorFunction.Disabled': 1
  'AzureWebJobs.SubOrchestratorFunction.Disabled': 1
  'AzureWebJobs.EmailSenderFunction.Disabled': 1
  'AzureWebJobs.GroupNameReaderFunction.Disabled': 1
  'AzureWebJobs.GroupVerifierFunction.Disabled': 1
  'AzureWebJobs.JobStatusUpdaterFunction.Disabled': 1
  'AzureWebJobs.SyncJobsReaderFunction.Disabled': 1
  'AzureWebJobs.TopicMessageSenderFunction.Disabled': 1
  AzureFunctionsWebHost__hostid: 'JobTriggerStaging'
}

var productionSettings = {
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(jobTriggerStorageAccountProd, '2019-09-01').secretUriWithVersion})'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}JobTrigger'
  AzureFunctionsWebHost__hostid: 'JobTrigger'
}

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'userAssignedManagedIdentityNameReaderTemplate'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
}

resource graphUAMI 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: userAssignedManagedIdentityNameReader.outputs.value
  scope: resourceGroup(dataKeyVaultResourceGroup)
}

module functionAppTemplate_JobTrigger 'functionApp.bicep' = {
  name: 'functionAppTemplate-JobTrigger'
  params: {
    name: '${functionAppName}-JobTrigger'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
  }
  dependsOn: [
    servicePlanTemplate
    graphUAMI
  ]
}

module functionAppSlotTemplate_JobTrigger 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-JobTrigger'
  params: {
    name: '${functionAppName}-JobTrigger/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
  }
  dependsOn: [
    functionAppTemplate_JobTrigger
    graphUAMI
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate-JobTrigger'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobTrigger.outputs.msi
        secrets: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_JobTrigger.outputs.msi
        secrets: [
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

module prereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'prereqsKeyVaultPoliciesTemplate-JobTrigger'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_JobTrigger.outputs.msi
        secrets: [
          'get'
        ]
        certificates: [
          'get'
        ]
      }
      {
        objectId: functionAppSlotTemplate_JobTrigger.outputs.msi
        secrets: [
          'get'
        ]
        certificates: [
          'get'
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

resource functionAppSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: '${functionAppName}-JobTrigger/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, productionSettings)
  dependsOn: [
    functionAppTemplate_JobTrigger
    dataKeyVaultPoliciesTemplate
  ]
}

resource functionAppStagingSettings 'Microsoft.Web/sites/slots/config@2022-09-01' = {
  name: '${functionAppName}-JobTrigger/staging/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, stagingSettings)
  dependsOn: [
    functionAppSlotTemplate_JobTrigger
    dataKeyVaultPoliciesTemplate
  ]
}
