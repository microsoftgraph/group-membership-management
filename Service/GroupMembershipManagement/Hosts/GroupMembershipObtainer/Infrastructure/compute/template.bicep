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
var serviceBusConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusConnectionString')
var serviceBusSyncJobTopic = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusSyncJobTopic')
var serviceBusMembershipAggregatorQueue = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMembershipAggregatorQueue')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var syncDisabledCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncDisabledCCEmailAddresses')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')
var membershipStorageAccountName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountName')
var membershipContainerName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'membershipContainerName')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var groupMembershipObtainerStorageAccountProd = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'groupMembershipObtainerStorageAccountProd')
var groupMembershipObtainerStorageAccountStaging = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'groupMembershipObtainerStorageAccountStaging')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-GroupMembershipObtainer'
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
  'AzureFunctionsJobHost:extensions:durableTask:extendedSessionsEnabled': toLower(environmentAbbreviation) == 'prodv2' ? 'True' : 'False'
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: '@Microsoft.KeyVault(SecretUri=${reference(groupMembershipObtainerStorageAccountProd, '2019-09-01').secretUriWithVersion})'
  WEBSITE_CONTENTSHARE: toLower('functionApp-GroupMembershipObtainer')
  serviceBusMembershipAggregatorQueue: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMembershipAggregatorQueue, '2019-09-01').secretUriWithVersion})'
  serviceBusSyncJobTopic: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusSyncJobTopic, '2019-09-01').secretUriWithVersion})'
  serviceBusTopicConnection: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusConnectionString, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientCertificateName': '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientSecret': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:TenantId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:KeyVaultName': prereqsKeyVaultName
  'graphCredentials:KeyVaultTenantId': tenantId
  'ConnectionStrings:JobsContext': '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  senderAddress: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
  senderPassword: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
  syncDisabledCCEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(syncDisabledCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
  supportEmailAddresses: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
  membershipStorageAccountName: '@Microsoft.KeyVault(SecretUri=${reference(membershipStorageAccountName, '2019-09-01').secretUriWithVersion})'
  membershipContainerName: '@Microsoft.KeyVault(SecretUri=${reference(membershipContainerName, '2019-09-01').secretUriWithVersion})'
  appConfigurationEndpoint: appConfigurationEndpoint
  actionableEmailProviderId: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
}

var stagingSettings = {
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(groupMembershipObtainerStorageAccountStaging, '2019-09-01').secretUriWithVersion})'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}GroupMembershipObtainerStaging'
  'AzureWebJobs.StarterFunction.Disabled': 1
  'AzureWebJobs.OrchestratorFunction.Disabled': 1
  'AzureWebJobs.SubOrchestratorFunction.Disabled': 1
  'AzureWebJobs.DeltaUsersReaderFunction.Disabled': 1
  'AzureWebJobs.DeltaUsersSenderFunction.Disabled': 1
  'AzureWebJobs.EmailSenderFunction.Disabled': 1
  'AzureWebJobs.FileDownloaderFunction.Disabled': 1
  'AzureWebJobs.GroupsReaderFunction.Disabled': 1
  'AzureWebJobs.GroupValidatorFunction.Disabled': 1
  'AzureWebJobs.JobStatusUpdaterFunction.Disabled': 1
  'AzureWebJobs.MembersReaderFunction.Disabled': 1
  'AzureWebJobs.SourceGroupsReaderFunction.Disabled': 1
  'AzureWebJobs.SubsequentDeltaUsersReaderFunction.Disabled': 1
  'AzureWebJobs.SubsequentMembersReaderFunction.Disabled': 1
  'AzureWebJobs.SubsequentUsersReaderFunction.Disabled': 1
  'AzureWebJobs.UsersReaderFunction.Disabled': 1
  'AzureWebJobs.UsersSenderFunction.Disabled': 1
  AzureFunctionsWebHost__hostid: '${environmentAbbreviation}GroupMembershipObtainerStaging'
}

var productionSettings = {
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(groupMembershipObtainerStorageAccountProd, '2019-09-01').secretUriWithVersion})'
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}GroupMembershipObtainer'
}

module functionAppTemplate_GroupMembershipObtainer 'functionApp.bicep' = {
  name: 'functionAppTemplate-GroupMembershipObtainer'
  params: {
    name: '${functionAppName}-GroupMembershipObtainer'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_GroupMembershipObtainer 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-GroupMembershipObtainer'
  params: {
    name: '${functionAppName}-GroupMembershipObtainer/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    secretSettings: commonSettings
  }
  dependsOn: [
    functionAppTemplate_GroupMembershipObtainer
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate-GroupMembershipObtainer'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_GroupMembershipObtainer.outputs.msi
        secrets: [
          'get'
          'list'
        ]
      }
      {
        objectId: functionAppSlotTemplate_GroupMembershipObtainer.outputs.msi
        secrets: [
          'get'
          'list'
        ]
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_GroupMembershipObtainer
    functionAppSlotTemplate_GroupMembershipObtainer
  ]
}

module prereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'prereqsKeyVaultPoliciesTemplate-GroupMembershipObtainer'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_GroupMembershipObtainer.outputs.msi
        secrets: [
          'get'
          'list'
        ]
        certificates: [
          'get'
        ]
      }
      {
        objectId: functionAppSlotTemplate_GroupMembershipObtainer.outputs.msi
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
    functionAppTemplate_GroupMembershipObtainer
    functionAppSlotTemplate_GroupMembershipObtainer
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2022-09-01' = {
  name: '${functionAppName}-GroupMembershipObtainer/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, productionSettings)
  dependsOn: [
    functionAppTemplate_GroupMembershipObtainer
    dataKeyVaultPoliciesTemplate
  ]
}

resource functionAppStagingSettings 'Microsoft.Web/sites/slots/config@2022-09-01' = {
  name: '${functionAppName}-GroupMembershipObtainer/staging/appsettings'
  kind: 'string'
  properties: union(commonSettings, appSettings, stagingSettings)
  dependsOn: [
    functionAppSlotTemplate_GroupMembershipObtainer
    dataKeyVaultPoliciesTemplate
  ]
}
