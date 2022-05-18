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

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsTableName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsTableName')
var membershipStorageAccountName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountName')
var membershipContainerName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'membershipContainerName')
var graphUpdaterUrl = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUpdaterUrl')
var graphUpdaterFunctionKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUpdaterFunctionKey')
var functionAppFullName = '${functionAppName}-MembershipAggregator'
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var senderUsername = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderUsername')
var senderPassword = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'senderPassword')
var syncCompletedCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncCompletedCCEmailAddresses')
var syncDisabledCCEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'syncDisabledCCEmailAddresses')
var supportEmailAddresses = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'supportEmailAddresses')

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

module functionAppTemplate_MembershipAggregator 'functionApp.bicep' = {
  name: 'functionAppTemplate-MembershipAggregator'
  params: {
    name: functionAppFullName
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    tenantId: tenantId
    secretSettings: [
      {
        name: 'WEBSITE_RUN_FROM_PACKAGE'
        value: 1
        slotSetting: false
      }
      {
        name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
        value: 1
        slotSetting: false
      }
      {
        name: 'SCM_TOUCH_WEBCONFIG_AFTER_DEPLOYMENT'
        value: '0'
        slotSetting: false
      }
      {
        name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
        value: reference(resourceId(appInsightsResourceGroup, 'microsoft.insights/components/', appInsightsName), '2015-05-01').InstrumentationKey
        slotSetting: false
      }
      {
        name: 'AzureWebJobsStorage'
        value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
        slotSetting: false
      }
      {
        name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
        value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
        slotSetting: false
      }
      {
        name: 'WEBSITE_CONTENTSHARE'
        value: toLower('functionApp-MembershipAggregator')
        slotSetting: false
      }
      {
        name: 'FUNCTIONS_WORKER_RUNTIME'
        value: 'dotnet'
        slotSetting: false
      }
      {
        name: 'FUNCTIONS_EXTENSION_VERSION'
        value: '~3'
        slotSetting: false
      }
      {
        name: 'logAnalyticsCustomerId'
        value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'logAnalyticsPrimarySharedKey'
        value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'jobsStorageAccountConnectionString'
        value: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'jobsTableName'
        value: '@Microsoft.KeyVault(SecretUri=${reference(jobsTableName, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'graphUpdaterUrl'
        value: '@Microsoft.KeyVault(SecretUri=${reference(graphUpdaterUrl, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'graphUpdaterFunctionKey'
        value: '@Microsoft.KeyVault(SecretUri=${reference(graphUpdaterFunctionKey, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'membershipStorageAccountName'
        value: '@Microsoft.KeyVault(SecretUri=${reference(membershipStorageAccountName, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'membershipContainerName'
        value: '@Microsoft.KeyVault(SecretUri=${reference(membershipContainerName, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'appConfigurationEndpoint'
        value: appConfigurationEndpoint
        slotSetting: false
      }
      {
        name: 'graphCredentials:ClientSecret'
        value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'graphCredentials:ClientId'
        value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'graphCredentials:TenantId'
        value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'graphCredentials:KeyVaultName'
        value: prereqsKeyVaultName
        slotSetting: false
      }
      {
        name: 'graphCredentials:KeyVaultTenantId'
        value: tenantId
        slotSetting: false
      }
      {
        name: 'senderAddress'
        value: '@Microsoft.KeyVault(SecretUri=${reference(senderUsername, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'senderPassword'
        value: '@Microsoft.KeyVault(SecretUri=${reference(senderPassword, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'syncCompletedCCEmailAddresses'
        value: '@Microsoft.KeyVault(SecretUri=${reference(syncCompletedCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'syncDisabledCCEmailAddresses'
        value: '@Microsoft.KeyVault(SecretUri=${reference(syncDisabledCCEmailAddresses, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
      {
        name: 'supportEmailAddresses'
        value: '@Microsoft.KeyVault(SecretUri=${reference(supportEmailAddresses, '2019-09-01').secretUriWithVersion})'
        slotSetting: false
      }
    ]
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module PrereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'PrereqsKeyVaultPoliciesTemplate'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_MembershipAggregator.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_MembershipAggregator
  ]
}
