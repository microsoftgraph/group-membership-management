@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

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

@description('Tenant id.')
param tenantId string

@description('Service plan name.')
param servicePlanName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi-serviceplan'

@description('App service name.')
param appServiceName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-webapi'

@description('Enter the hostname for the api')
param apiHostname string = '${appServiceName}.azurewebsites.net'

@description('Service plan sku')
param servicePlanSku string = 'F1'

@description('Resource location.')
param location string

@description('Maximum elastic worker count.')
param maximumElasticWorkerCount int = 1

@description('Provides the endpoint for the app configuration resource.')
param appConfigurationEndpoint string = 'https://${solutionAbbreviation}-appconfig-${environmentAbbreviation}.azconfig.io'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsKeyVaultName string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the resource group where the \'prereqs\' key vault is located.')
param prereqsResourceGroup string = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'

@description('Name of the \'data\' key vault.')
param dataKeyVaultName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the resource group where the \'data\' key vault is located.')
param dataResourceGroup string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Enter application insights name.')
param appInsightsName string = '${solutionAbbreviation}-data-${environmentAbbreviation}'

@description('Name of the Azure Data Factory resource.')
param dataFactoryName string = '${solutionAbbreviation}-data-${environmentAbbreviation}-adf'

@description('Name of the Azure Data Factory pipeline.')
param adfPipeline string

@description('Flag to indicate if the deployment should set RBAC permissions.')
param setRBACPermissions bool = false

@description('Allowed origins for the SignalR service.')
param signalrCORS array = ['https://microsoft.com']

@description('Location for the OpenAI resource.')
param aiLocation string

param featureFlags object = {
  enableTeamsChannel: false
  enableOpenAI: false
}

var subscriptionId = subscription().subscriptionId
var appInsightsInstrumentationKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'appInsightsInstrumentationKey')
var webapiClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webapiClientId')
var webApiTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'webApiTenantId')
var logAnalyticsCustomerId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsCustomerId')
var logAnalyticsPrimarySharedKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'logAnalyticsPrimarySharedKey')
var jobsStorageAccountConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsStorageAccountConnectionString')
var graphAppClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientId')
var graphAppClientSecret = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppClientSecret')
var graphAppCertificateName = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppCertificateName')
var graphAppTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'graphAppTenantId')
var teamsChannelAppClientId =  resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppClientId')
var teamsChannelAppClientSecret = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppClientSecret')
var teamsChannelAppCertificateName = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppCertificateName')
var teamsChannelAppTenantId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelAppTenantId')
var teamsChannelServiceAccountObjectId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountObjectId')
var teamsChannelServiceAccountUsername = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountUsername')
var teamsChannelServiceAccountPassword = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'teamsChannelServiceAccountPassword')
var actionableEmailProviderId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'notifierProviderId')
var oamEntraAppId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'oamEntraAppId')
var oamEntraAppScope = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'oamEntraAppScope')
var replicaJobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'replicaJobsMSIConnectionString')
var jobsMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobsMSIConnectionString')
var sqlServerMSIConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'sqlServerMSIConnectionString')
var graphUserAssignedManagedIdentityClientId = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'graphUserAssignedManagedIdentityClientId')
var azureSignalRConnectionString = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'azureSignalRConnectionString')
var openAIEndpoint = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'openAIEndpoint')

var serviceBusFQN = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusFQN')
var serviceBusMembershipAggregatorQueue = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMembershipAggregatorQueue')
var serviceBusMembershipUpdatersTopic = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusMembershipUpdatersTopic')
var serviceBusConfigurationQueue = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusConfigurationQueue')
var serviceBusSyncJobTopic = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusSyncJobTopic')
var serviceBusNotificationsQueue = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'serviceBusNotificationsQueue')
var jobSchedulerFunctionBaseUrl = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobSchedulerFunctionBaseUrl')
var jobSchedulerFunctionKey = resourceId(subscription().subscriptionId, dataResourceGroup, 'Microsoft.KeyVault/vaults/secrets', dataKeyVaultName, 'jobSchedulerFunctionKey')
var functionAuthAppClientId = resourceId(subscription().subscriptionId, prereqsResourceGroup, 'Microsoft.KeyVault/vaults/secrets', prereqsKeyVaultName, 'functionAuthAppClientId')

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  scope: resourceGroup(dataResourceGroup)
  name: appInsightsName
}

var appSettings = [
  {
    name: 'AZURE_TOKEN_CREDENTIALS'
    value:'ManagedIdentityCredential'
  }
  {
    name: 'APPINSIGHTS_INSTRUMENTATIONKEY'
    value:'@Microsoft.KeyVault(SecretUri=${reference(appInsightsInstrumentationKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(webapiClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:Audience'
    value: '@Microsoft.KeyVault(SecretUri=${reference(webapiClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:TenantId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(webApiTenantId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'AzureAd:Instance'
    value: environment().authentication.loginEndpoint
  }
  {
    name: 'ApplicationInsights:ConnectionString'
    value: appInsights.properties.ConnectionString
  }
  {
    name: 'ConnectionStrings:JobsContextReadOnly'
    value: '@Microsoft.KeyVault(SecretUri=${reference(replicaJobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'ConnectionStrings:JobsContext'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:appConfigurationEndpoint'
    value: appConfigurationEndpoint
  }
  {
    name: 'Settings:logAnalyticsCustomerId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsCustomerId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:logAnalyticsPrimarySharedKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(logAnalyticsPrimarySharedKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:jobsStorageAccountConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobsStorageAccountConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientCertificateName'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppCertificateName, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientSecret'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientSecret, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:ClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:GraphCredentials:TenantId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphAppTenantId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ClientCertificateName'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppCertificateName, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ClientSecret'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppClientSecret, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ClientId'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppClientId, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:TenantId'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelAppTenantId, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ServiceAccountObjectId'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountObjectId, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ServiceAccountUsername'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountUsername, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:ServiceAccountPassword'
    value: featureFlags.enableTeamsChannel ? '@Microsoft.KeyVault(SecretUri=${reference(teamsChannelServiceAccountPassword, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'Settings:TeamsGraphCredentials:AppName'
    value: '${solutionAbbreviation}-TeamsChannel-${environmentAbbreviation}'
  }
  {
    name: 'Settings:ActionableEmailProviderId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(actionableEmailProviderId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:oamEntraAppId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(oamEntraAppId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:oamEntraAppScope'
    value: '@Microsoft.KeyVault(SecretUri=${reference(oamEntraAppScope, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ApiHostname'
    value: apiHostname
  }
  {
    name: 'Settings:GraphCredentials:KeyVaultName'
    value: prereqsKeyVaultName
  }
  {
    name: 'Settings:TeamsGraphCredentials:KeyVaultName'
    value: prereqsKeyVaultName
  }
  {
    name: 'Settings:GraphCredentials:KeyVaultTenantId'
    value: tenantId
  }
  {
    name: 'Settings:SqlServerConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(sqlServerMSIConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:AzureSignalRConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${reference(azureSignalRConnectionString, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:OpenAIEndpoint'
    value: featureFlags.enableOpenAI ? '@Microsoft.KeyVault(SecretUri=${reference(openAIEndpoint, '2019-09-01').secretUriWithVersion})' : 'not-set'
  }
  {
    name: 'ADF:Pipeline'
    value: adfPipeline
  }
  {
    name: 'ADF:DataFactoryName'
    value: dataFactoryName
  }
  {
    name: 'ADF:SubscriptionId'
    value: subscriptionId
  }
  {
    name: 'ADF:ResourceGroup'
    value: dataResourceGroup
  }
  {
    name: 'Settings:GraphCredentials:UserAssignedManagedIdentityClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(graphUserAssignedManagedIdentityClientId, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:ServiceBusFQN'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusFQN, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:MembershipAggregatorQueue'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMembershipAggregatorQueue, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:MembershipUpdatersTopic'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusMembershipUpdatersTopic, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:SyncJobTopic'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusSyncJobTopic, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:PendingConfigurationQueue'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusConfigurationQueue, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:ServiceBus:NotificationsQueue'
    value: '@Microsoft.KeyVault(SecretUri=${reference(serviceBusNotificationsQueue, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:JobSchedulerFunctionBaseUrl'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobSchedulerFunctionBaseUrl, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:JobSchedulerFunctionKey'
    value: '@Microsoft.KeyVault(SecretUri=${reference(jobSchedulerFunctionKey, '2019-09-01').secretUriWithVersion})'
  }
  {
    name: 'Settings:FunctionAuthAppClientId'
    value: '@Microsoft.KeyVault(SecretUri=${reference(functionAuthAppClientId, '2019-09-01').secretUriWithVersion})'
  }
]

resource dataKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: dataKeyVaultName
  scope: resourceGroup(dataResourceGroup)
}

module userAssignedManagedIdentityNameReader 'keyVaultReader.bicep' = {
  name: 'uamiNameReader-WebApi'
  params: {
    value: dataKeyVault.getSecret('graphUserAssignedManagedIdentityName')
  }
  dependsOn: [
    dataKeyVault
  ]
}

resource graphUAMI 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-07-31-preview' existing = {
  name: userAssignedManagedIdentityNameReader.outputs.value
  scope: resourceGroup(dataResourceGroup)
}

resource signalR 'Microsoft.SignalRService/signalR@2023-08-01-preview' = {
  name: '${solutionAbbreviation}-compute-${environmentAbbreviation}-signalr'
  location: location
  sku: {
    name: 'Standard_S1'
    tier: 'Standard'
    capacity: 1
  }
  kind: 'SignalR'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    cors: {
      allowedOrigins: signalrCORS
    }
    disableLocalAuth: true
    features: [
      {
        flag: 'ServiceMode'
        value: 'Default'
      }
    ]
  }
}

module servicePlanTemplate 'servicePlan.bicep' = {
  name: 'servicePlanTemplate-WebApi'
  params: {
    environmentAbbreviation: environmentAbbreviation
    name: servicePlanName
    sku: servicePlanSku
    location: location
    maximumElasticWorkerCount: maximumElasticWorkerCount
  }
}
module appService 'appService.bicep' = {
  name: 'appServiceTemplate-WebApi'
  params: {
    name: appServiceName
    location: location
    servicePlanName: servicePlanName
    appSettings: appSettings
    dataKeyVaultName: dataKeyVaultName
    dataResourceGroup: dataResourceGroup
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsResourceGroup: prereqsResourceGroup
    tenantId: tenantId
    userManagedIdentities:{
      '${graphUAMI.id}' : {}
    }
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    servicePlanTemplate
    graphUAMI
  ]
}

module openAINetworking 'openAIResources.bicep' = if (featureFlags.enableOpenAI) {
  name: 'openAINetworkingTemplate-WebApi'
  scope: resourceGroup(dataResourceGroup)
  params: {
    openAIResourceName: '${solutionAbbreviation}-data-${environmentAbbreviation}-openai'
    aiLocation: aiLocation
    allowedIpAddresses: '${appService.outputs.outboundIpAddresses},${appService.outputs.possibleOutboundIpAddresses}'
  }
  dependsOn: [
    appService
  ]
}
