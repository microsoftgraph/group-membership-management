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

@description('Name of the keyvault secret containing the storage account connection string')
param storageAccountSecretName string

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var storageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, storageAccountSecretName)

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

module functionAppTemplate_AzureUserReader 'functionApp.bicep' = {
  name: 'functionAppTemplate-AzureUserReader'
  params: {
    name: '${functionAppName}-AzureUserReader'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
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
        value: toLower('functionApp-AzureUserReader')
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
        name: 'storageAccountConnectionString'
        value: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
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
        name: 'WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT'
        value: maximumElasticWorkerCount
        slotSetting: false
      }
      {
        name: 'maxRetryAfterAttempts'
        value: '4'
        slotSetting: false
      }
      {
        name: 'maxExceptionHandlingAttempts'
        value: '2'
        slotSetting: false
      }
      {
        name: 'appConfigurationEndpoint'
        value: appConfigurationEndpoint
        slotSetting: false
      }
    ]
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_AzureUserReader.outputs.msi
        permissions: [
          'get'
          'list'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_AzureUserReader
  ]
}

module PrereqsKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'PrereqsKeyVaultPoliciesTemplate'
  scope: resourceGroup(prereqsKeyVaultResourceGroup)
  params: {
    name: prereqsKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_AzureUserReader.outputs.msi
        permissions: [
          'get'
        ]
        type: 'secrets'
      }
    ]
    tenantId: tenantId
  }
  dependsOn: [
    functionAppTemplate_AzureUserReader
  ]
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    keyVaultName: dataKeyVaultName
    keyVaultParameters: [
      {
        name: 'azureUserReaderUrl'
        value: functionAppTemplate_AzureUserReader.outputs.hostName
      }
      {
        name: 'azureUserReaderKey'
        value: functionAppTemplate_AzureUserReader.outputs.adfKey
      }
      {
        name: 'azureUserReaderFunctionName'
        value: '${functionAppName}-AzureUserReader'
      }
    ]
  }
  dependsOn: [
    functionAppTemplate_AzureUserReader
    dataKeyVaultPoliciesTemplate
  ]
}