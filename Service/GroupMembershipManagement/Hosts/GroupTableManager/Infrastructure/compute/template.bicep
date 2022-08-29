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

@description('Name of the public source branch where webapp repo exists.')
param branch string

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
param storageAccountName string = 'gmmecba5p7jzs65srg'

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

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate'
  params: {
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsTableName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsTableName')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var storageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'storageAccountConnectionString')
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')

var appSettings = {
  WEBSITE_ADD_SITENAME_BINDINGS_IN_APPHOST_CONFIG: 1
  WEBSITE_ENABLE_SYNC_UPDATE_SITE: 1
  SCM_TOUCH_WEBCONFIG_AFTER_DEPLOYMENT: 0
  APPINSIGHTS_INSTRUMENTATIONKEY: '@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  AzureWebJobsStorage: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  WEBSITE_CONTENTAZUREFILECONNECTIONSTRING: '@Microsoft.KeyVault(SecretUri=${reference(storageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  FUNCTIONS_WORKER_RUNTIME: 'dotnet'
  FUNCTIONS_EXTENSION_VERSION: '~3'
  jobsStorageAccountConnectionString: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  jobsTableName: '@Microsoft.KeyVault(SecretUri=${reference(jobsTableName, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientSecret': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:ClientId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:TenantId': '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  'graphCredentials:KeyVaultName': prereqsKeyVaultName
  'graphCredentials:KeyVaultTenantId': tenantId
  logAnalyticsCustomerId: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  logAnalyticsPrimarySharedKey: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT: maximumElasticWorkerCount
  appConfigurationEndpoint: appConfigurationEndpoint
}

var stagingSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-GroupTableManager-staging')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}GroupTableManagerStaging'
  'AzureWebJobs.GroupTableManager.Disabled': 0
}

var productionSettings = {
  WEBSITE_CONTENTSHARE: toLower('functionApp-GroupTableManager')
  AzureFunctionsJobHost__extensions__durableTask__hubName: '${solutionAbbreviation}compute${environmentAbbreviation}GroupTableManager'
  'AzureWebJobs.GroupTableManager.Disabled': 1
}

module functionAppTemplate_GroupTableManager 'functionApp.bicep' = {
  name: 'functionAppTemplate-GroupTableManager'
  params: {
    name: '${functionAppName}-GroupTableManager'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_GroupTableManager 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-GroupTableManager'
  params: {
    name: '${functionAppName}-GroupTableManager/staging'
    kind: functionAppKind
    location: location
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
  }
  dependsOn: [
    functionAppTemplate_GroupTableManager
  ]
}

resource staticWebApp 'Microsoft.Web/staticSites@2021-03-01' = {
  name: '${solutionAbbreviation}-ui'
  location: location
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
    branch: branch
    buildProperties: {
      appBuildCommand: 'dotnet run'
      appLocation: 'UI/WebApp'
      outputLocation: 'UI/WebApp/wwwroot'
      skipGithubActionWorkflowGeneration: true
    }
    enterpriseGradeCdnStatus: 'Disabled'
    provider: 'DevOps'
    repositoryUrl: 'https://microsoftit.visualstudio.com/OneITVSO/_git/STW-Sol-GrpMM-public'
    stagingEnvironmentPolicy: 'Disabled'
  }
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_GroupTableManager.outputs.msi
        permissions: [
          'get'
          'list'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_GroupTableManager.outputs.msi
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
    functionAppTemplate_GroupTableManager
    functionAppSlotTemplate_GroupTableManager
  ]
}

resource functionAppSettings 'Microsoft.Web/sites/config@2022-03-01' = {
  name: '${functionAppName}-GroupTableManager/appsettings'
  kind: 'string'
  properties: union(appSettings, productionSettings)
  dependsOn: [
    functionAppTemplate_GroupTableManager
    dataKeyVaultPoliciesTemplate
  ]
}

resource functionAppStagingSettings 'Microsoft.Web/sites/slots/config@2022-03-01' = {
  name: '${functionAppName}-GroupTableManager/staging/appsettings'
  kind: 'string'
  properties: union(appSettings, stagingSettings)
  dependsOn: [
    functionAppSlotTemplate_GroupTableManager
    dataKeyVaultPoliciesTemplate
  ]
}


// Check on if this API Key is acceptable for deployment purposes of front end
output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
