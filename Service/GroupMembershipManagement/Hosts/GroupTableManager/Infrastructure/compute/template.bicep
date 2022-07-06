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

@description('Function resource location.')
param functionLocation string = 'westus'

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
    location: functionLocation
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
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

var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var jobsTableName = resourceId(subscription().subscriptionId, dataKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsTableName')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsKeyVaultResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')

var appSettings = [
  {
    name: 'WEBSITE_ADD_SITENAME_BINDINGS_IN_APPHOST_CONFIG'
    value: 1
  }
  {
    name: 'WEBSITE_RUN_FROM_PACKAGE'
    value: 1
  }
  {
    name: 'WEBSITE_ENABLE_SYNC_UPDATE_SITE'
    value: 1
  }
  {
    name: 'SCM_TOUCH_WEBCONFIG_AFTER_DEPLOYMENT'
    value: 0
  }
  {
    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
    value: reference(resourceId(appInsightsResourceGroup, 'microsoft.insights/components/', appInsightsName), '2015-05-01').InstrumentationKey
  }
  {
    name: 'AzureWebJobsStorage'
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
  }
  {
    name: 'WEBSITE_CONTENTAZUREFILECONNECTIONSTRING'
    value: 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${listKeys(resourceId(storageAccountResourceGroup, 'Microsoft.Storage/storageAccounts', storageAccountName), providers('Microsoft.Storage', 'storageAccounts').apiVersions[0]).keys[0].value}'
  }
  {
    name: 'FUNCTIONS_WORKER_RUNTIME'
    value: 'dotnet'
  }
  {
    name: 'FUNCTIONS_EXTENSION_VERSION'
    value: '~3'
  }
  {
    name: 'jobsStorageAccountConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'jobsTableName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsTableName, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:ClientSecret'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:TenantId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'graphCredentials:KeyVaultName'
    value: prereqsKeyVaultName
  }
  {
    name: 'graphCredentials:KeyVaultTenantId'
    value: tenantId
  }
  {
    name: 'logAnalyticsCustomerId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'logAnalyticsPrimarySharedKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'WEBSITE_MAX_DYNAMIC_APPLICATION_SCALE_OUT'
    value: maximumElasticWorkerCount
  }
  {
    name: 'appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
]

var stagingSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-GraphUpdater-staging')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}GraphUpdaterStaging'
  }
  {
    name: 'AzureWebJobs.StarterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.OrchestratorFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupUpdaterSubOrchestratorFunction.Disabled'
    value: 1
  }  
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.FileDownloaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupOwnersReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupUpdaterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.GroupValidatorFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.JobReaderFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 1
  }
  {
    name: 'AzureWebJobs.LoggerFunction.Disabled'
    value: 1
  }
]

var productionSettings = [
  {
    name: 'WEBSITE_CONTENTSHARE'
    value: toLower('functionApp-GraphUpdater')
  }
  {
    name: 'AzureFunctionsJobHost__extensions__durableTask__hubName'
    value: '${solutionAbbreviation}compute${environmentAbbreviation}GraphUpdater'
  }
  {
    name: 'AzureWebJobs.StarterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.OrchestratorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupUpdaterSubOrchestratorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.EmailSenderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.FileDownloaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupNameReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupOwnersReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupUpdaterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.GroupValidatorFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.JobReaderFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.JobStatusUpdaterFunction.Disabled'
    value: 0
  }
  {
    name: 'AzureWebJobs.LoggerFunction.Disabled'
    value: 0
  }
]

module functionAppTemplate_WebAppFunction 'functionApp.bicep' = {
  name: 'functionAppTemplate-WebAppFunction'
  params: {
    name: '${functionAppName}-WebAppFunction'
    kind: functionAppKind
    location: functionLocation
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    secretSettings: union(appSettings, productionSettings)
  }
  dependsOn: [
    servicePlanTemplate
  ]
}

module functionAppSlotTemplate_WebAppFunction 'functionAppSlot.bicep' = {
  name: 'functionAppSlotTemplate-WebAppFunction'
  params: {
    name: '${functionAppName}-WebAppFunction/staging'
    kind: functionAppKind
    location: functionLocation
    servicePlanName: servicePlanName
    dataKeyVaultName: dataKeyVaultName
    dataKeyVaultResourceGroup: dataKeyVaultResourceGroup
    secretSettings: union(appSettings, stagingSettings)
  }
  dependsOn: [
    functionAppTemplate_WebAppFunction
  ]
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate'
  scope: resourceGroup(dataKeyVaultResourceGroup)
  params: {
    name: dataKeyVaultName
    policies: [
      {
        objectId: functionAppTemplate_WebAppFunction.outputs.msi
        permissions: [
          'get'
          'list'
        ]
        type: 'secrets'
      }
      {
        objectId: functionAppSlotTemplate_WebAppFunction.outputs.msi
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
    functionAppTemplate_WebAppFunction
    functionAppSlotTemplate_WebAppFunction
  ]
}

output deployment_token string = listSecrets(staticWebApp.id, staticWebApp.apiVersion).properties.apiKey
