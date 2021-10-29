param containerBaseUrl string
param containerSasToken string

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
param resourceGroupClassification string = 'data'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Tenant id.')
param tenantId string

@description('Key vault name.')
@minLength(1)
param keyVaultName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Key vault sku name.')
@allowed([
  'premium'
  'standard'
])
param keyVaultSkuName string = 'standard'

@description('Key vault sku family.')
param keyVaultSkuFamily string = 'A'

@description('Resource location.')
param location string

@description('User(s) and/or Group(s) AAD Object Ids to which access to the keyvault will be granted to.')
param keyVaultReaders array

@description('Enter application insights name.')
param appInsightsName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Enter the application insights type.')
@allowed([
  'web'
  'other'
])
param appInsightsKind string = 'web'

@description('Enter service bus name.')
param serviceBusName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@description('Enter service bus sku.')
@allowed([
  'Standard'
  'Premium'
])
param serviceBusSku string = 'Standard'

@description('Enter service bus topic name.')
param serviceBusTopicName string = 'syncJobs'

@description('Enter service bus topic\'s subscriptions.')
param serviceBusTopicSubscriptions array = [
  {
    name: 'Organization'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'Organization\''
  }
  {
    name: 'SecurityGroup'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'SecurityGroup\''
  }
]

@description('Enter service bus queue name.')
param serviceBusQueueName string = 'membership'

@description('Enter storage account name.')
@minLength(1)
@maxLength(24)
param storageAccountName string = '${solutionAbbreviation}${environmentAbbreviation}${uniqueString(resourceGroup().id)}'

@description('Enter storage account sku. Setting applied to storageAccount and jobsStorageAccount')
@allowed([
  'Standard_LRS'
  'Standard_GRS'
  'Standard_ZRS'
  'Premium_LRS'
])
param storageAccountSku string = 'Standard_LRS'

@description('Enter storage account name.')
@minLength(1)
param jobsStorageAccountName string = 'jobs${environmentAbbreviation}${uniqueString(resourceGroup().id)}'

@description('Enter jobs table name.')
@minLength(1)
param jobsTableName string
param logAnalyticsName string = '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'

@allowed([
  'PerGB2018'
  'Free'
  'Standalone'
  'PerNode'
  'Standard'
  'Premium'
])
param logAnalyticsSku string = 'PerGB2018'

@description('Enter app configuration name.')
@minLength(1)
@maxLength(24)
param appConfigurationName string = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'

@description('Enter app configuration sku.')
@allowed([
  'Standard'
  'Free'
])
param appConfigurationSku string = 'Standard'
param appConfigurationKeyData array = [
  {
    key: 'SecurityGroup:IsSecurityGroupDryRunEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'tag-dry-run'
    }
  }
  {
    key: 'GraphUpdater:IsGraphUpdaterDryRunEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'tag-dry-run'
    }
  }
  {
    key: 'GraphUpdater:LastMessageWaitTimeout'
    value: '10'
    contentType: 'integer'
    tag: {
      tag1: 'GraphUpdater'
    }
  }
  {
    key: 'JobScheduler:JobSchedulerConfiguration'
    value: '{ "ResetJobs": false, "DaysToAddForReset": 0, "DistributeJobs": true, "IncludeFutureJobs": false, "StartTimeDelayMinutes": 5, "DelayBetweenSyncsSeconds": 5, "DefaultRuntimeSeconds": 60 }'
    contentType: 'string'
    tag: {
      tag1: 'JobScheduler'
    }
  }
]

@description('Unique name within the resource group for the Action group.')
param actionGroupName string = 'PIILogAlerts'

@description('Short name up to 12 characters for the Action group.')
param actionGroupShortName string = 'PIILogs'

@description('This email address is used to reach out in the event that a user identifier is logged in order to help resolve an unexpected issue that occurred during a sync. This will only occur when the identifier is absolutely required to resolve the issue. The recipients of these emails should be super responsive to these notifications and clear out the log as soon as possible.')
param emailReceivers array = [
  {
    name: 'Example name'
    emailAddress: 'example@microsoft.com'
    useCommonAlertSchema: true
  }
]

module dataKeyVaultTemplate 'keyVault.bicep' = {
  name: 'dataKeyVaultTemplate'
  params: {
    name: keyVaultName
    skuName: keyVaultSkuName
    skuFamily: keyVaultSkuFamily
    location: location
    tenantId: tenantId
  }
}

module keyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultPoliciesTemplate'
  params: {
    name: keyVaultName
    policies: keyVaultReaders
    tenantId: tenantId
  }
  dependsOn: [
    dataKeyVaultTemplate
  ]
}

module appInsightsTemplate 'applicationInsights.bicep' = {
  name: 'appInsightsTemplate'
  params: {
    name: appInsightsName
    location: location
    kind: appInsightsKind
  }
}

module serviceBusTemplate 'serviceBus.bicep' = {
  name: 'serviceBusTemplate'
  params: {
    name: serviceBusName
    sku: serviceBusSku
    location: location
  }
}

module serviceBusTopicTemplate 'serviceBusTopic.bicep' = {
  name: 'serviceBusTopicTemplate'
  params: {
    serviceBusName: serviceBusName
    topicName: serviceBusTopicName
  }
  dependsOn: [
    serviceBusTemplate
  ]
}

module serviceBusSubscriptionsTemplate 'serviceBusSubscription.bicep' = {
  name: 'serviceBusSubscriptionsTemplate'
  params: {
    serviceBusName: serviceBusName
    topicName: serviceBusTopicName
    topicSubscriptions: serviceBusTopicSubscriptions
  }
  dependsOn: [
    serviceBusTopicTemplate
  ]
}

module serviceBusQueueTemplate 'serviceBusQueue.bicep' = {
  name: 'serviceBusQueueTemplate'
  params: {
    serviceBusName: serviceBusName
    queueName: serviceBusQueueName
    requiresSession: true
  }
  dependsOn: [
    serviceBusTemplate
  ]
}

module storageAccountTemplate 'storageAccount.bicep' = {
  name: 'storageAccountTemplate'
  params: {
    name: storageAccountName
    sku: storageAccountSku
  }
}

module jobsStorageAccountTemplate 'storageAccount.bicep' = {
  name: 'jobsStorageAccountTemplate'
  params: {
    name: jobsStorageAccountName
    sku: storageAccountSku
  }
}

module logAnalyticsTemplate 'logAnalytics.bicep' = {
  name: 'logAnalyticsTemplate'
  params: {
    name: logAnalyticsName
    sku: logAnalyticsSku
    location: location
  }
}

module appConfigurationTemplate 'appConfiguration.bicep' = {
  name: 'appConfigurationTemplate'
  params: {
    configStoreName: appConfigurationName
    appConfigurationSku: appConfigurationSku
    location: location
    appConfigurationKeyData: appConfigurationKeyData
  }
}

module actionGroupTemplate 'actionGroup.bicep' = {
  name: 'actionGroupTemplate'
  params: {
    actionGroupName: actionGroupName
    actionGroupShortName: actionGroupShortName
    emailReceivers: emailReceivers
  }
}

module logAlertRuleTemplate 'logAlertRule.bicep' = {
  name: 'logAlertRuleTemplate'
  params: {
    sourceId: logAnalyticsTemplate.outputs.resourceId
    location: location
    actionGroupId: actionGroupTemplate.outputs.actionGroupId
  }
}

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate'
  params: {
    keyVaultName: keyVaultName
    keyVaultParameters: [
      {
        name: 'storageAccountName'
        value: storageAccountName
      }
      {
        name: 'storageAccountConnectionString'
        value: storageAccountTemplate.outputs.connectionString
      }
      {
        name: 'jobsStorageAccountName'
        value: jobsStorageAccountName
      }
      {
        name: 'jobsStorageAccountConnectionString'
        value: jobsStorageAccountTemplate.outputs.connectionString
      }
      {
        name: 'jobsTableName'
        value: jobsTableName
      }
      {
        name: 'appInsightsAppId'
        value: appInsightsTemplate.outputs.appId
      }
      {
        name: 'appInsightsInstrumentationKey'
        value: appInsightsTemplate.outputs.instrumentationKey
      }
      {
        name: 'serviceBusNamespace'
        value: serviceBusName
      }
      {
        name: 'serviceBusPrimaryKey'
        value: serviceBusTemplate.outputs.rootManageSharedAccessKeyPrimaryKey
      }
      {
        name: 'serviceBusConnectionString'
        value: serviceBusTemplate.outputs.rootManageSharedAccessKeyConnectionString
      }
      {
        name: 'serviceBusSyncJobTopic'
        value: serviceBusTopicName
      }
      {
        name: 'serviceBusMembershipQueue'
        value: serviceBusQueueName
      }
      {
        name: 'logAnalyticsCustomerId'
        value: logAnalyticsTemplate.outputs.customerId
      }
      {
        name: 'logAnalyticsPrimarySharedKey'
        value: logAnalyticsTemplate.outputs.primarySharedKey
      }
    ]
  }
  dependsOn: [
    dataKeyVaultTemplate
    storageAccountTemplate
  ]
}

module dashboardTemplate 'dashboard.bicep' = {
  name: 'dashboardTemplate'
  params: {
    location: location
    name: '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'
  }
}

output storageAccountName string = storageAccountName
output serviceBusName string = serviceBusName
output serviceBusTopicName string = serviceBusTopicName
