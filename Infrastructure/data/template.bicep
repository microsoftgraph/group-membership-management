@description('Enter an abbreviation for the solution.')
@minLength(2)
@maxLength(3)
param solutionAbbreviation string = 'gmm'

@description('Classify the types of resources in data resource group.')
param resourceGroupClassification string = 'data'

@description('Classify the types of resources in prereqs resource group.')
param prereqsResourceGroupClassification string = 'prereqs'

@description('Classify the types of resources in compute resource group.')
param computeResourceGroupClassification string = 'compute'

@description('Enter an abbreviation for the environment.')
@minLength(2)
@maxLength(6)
param environmentAbbreviation string

@description('Subscription Id for the environment')
param subscriptionId string = subscription().subscriptionId

@description('Tenant id.')
param tenantId string

@description('SQL SKU Name')
param sqlSkuName string

@description('SQL SKU Tier')
param sqlSkuTier string

@description('SQL SKU Family')
param sqlSkuFamily string

@description('SQL SKU Capacity')
param sqlSkuCapacity int

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
    name: 'GroupMembership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'GroupMembership\''
  }
  {
    name: 'AzureMembershipProvider'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'AzureMembershipProvider\''
  }
]

@description('Enter service bus membership updaters topic\'s and subscriptions details.')
param serviceBusMembershipUpdatersTopicSubscriptions object

@description('Enter membership aggregator service bus queue name')
param serviceBusMembershipAggregatorQueue string = 'membershipAggregator'

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

@description('Enter membership container name.')
@minLength(1)
param membershipContainerName string = 'membership'

@description('Enter jobs table name.')
@minLength(1)
param jobsTableName string

@description('Enter notifications table name.')
@minLength(1)
param notificationsTableName string = 'notifications'

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
    key: 'JobTrigger:IsGroupReadWriteAllGranted'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'JobTrigger'
    }
  }
  {
    key: 'GroupMembershipObtainer:IsDeltaCacheEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'GroupMembershipObtainer'
    }
  }
  {
    key: 'GroupMembershipObtainer:IsDryRunEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'DryRun'
    }
  }
  {
    key: 'MembershipAggregator:IsMembershipAggregatorDryRunEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'DryRun'
    }
  }
  {
    key: 'MembershipAggregator:MaximumNumberOfThresholdRecipients'
    value: '3'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
    }
  }
  {
    key: 'MembershipAggregator:NumberOfThresholdViolationsToNotify'
    value: '2'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
    }
  }
  {
    key: 'MembershipAggregator:NumberOfThresholdViolationsToDisableJob'
    value: '7'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
    }
  }
  {
    key: 'GraphUpdater:IsDeltaCacheEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'GraphUpdater'
    }
  }
  {
    key: 'AzureMaintenance:HandleInactiveJobsEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'AzureMaintenance'
    }
  }
  {
    key: 'AzureMaintenance:NumberOfDaysBeforeDeletion'
    value: 35
    contentType: 'int'
    tag: {
      tag1: 'AzureMaintenance'
    }
  }
  {
    key: 'JobScheduler:JobSchedulerConfiguration'
    value: '{"ResetJobs":false,"DaysToAddForReset":0,"DistributeJobs":true,"IncludeFutureJobs":false,"StartTimeDelayMinutes":5,"DelayBetweenSyncsSeconds":5,"DefaultRuntimeSeconds":60,"GetRunTimeFromLogs":true,"RunTimeMetric":"Max","RunTimeRangeInDays":7,"RuntimeQuery":"AppEvents | where Name == \'SyncComplete\' | project TimeElapsed = todouble(Properties[\'SyncJobTimeElapsedSeconds\']), Destination = tostring(Properties[\'TargetOfficeGroupId\']), RunId = Properties[\'RunId\'], Result = Properties[\'Result\'], DryRun = Properties[\'IsDryRunEnabled\'] | where Result == \'Success\' and DryRun == \'False\' | project TimeElapsed, Destination, RunId | summarize MaxProcessingTime=max(TimeElapsed), AvgProcessingTime=avg(TimeElapsed) by Destination"}'
    contentType: 'string'
    tag: {
      tag1: 'JobScheduler'
    }
  }
  {
    key: 'GMM:LearnMoreUrl'
    value: 'http://learn-more-about-gmm-url.com'
    contentType: 'string'
    tag: {
      tag1: 'GMM'
    }
  }
  {
    key: 'Mail:IsAdaptiveCardEnabled'
    value: 'false'
    contentType: 'bool'
    tag: {
      tag1: 'Mail'
    }
  }
  {
    key: 'ThresholdNotification:IsThresholdNotificationEnabled'
    value: 'false'
    contentType: 'bool'
    tag: {
      tag1: 'ThresholdNotification'
    }
  }
]

@description('Array of feature flags objects. {id:"value", description:"description", enabled:true }')
param appConfigurationfeatureFlags array = []

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

@description('Enter actionable email notifier provider id.')
@minLength(0)
@maxLength(36)
param notifierProviderId string

@description('JSON string with an array listing the existing data resources [{Name: string, ResourceType: string}]')
param existingDataResources string = '[]'

@description('Administrators Azure AD Group Object Id')
param sqlAdministratorsGroupId string

@description('Administrators Azure AD Group Name')
param sqlAdministratorsGroupName string

module sqlServer 'sqlServer.bicep' =  {
  name: 'sqlServerTemplate'
  params: {
    environmentAbbreviation: environmentAbbreviation
    location: location
    solutionAbbreviation: solutionAbbreviation
    sqlSkuName: sqlSkuName
    sqlSkuTier: sqlSkuTier
    sqlSkuFamily: sqlSkuFamily
    sqlSkuCapacity: sqlSkuCapacity
    sqlAdministratorsGroupId: sqlAdministratorsGroupId
    sqlAdministratorsGroupName: sqlAdministratorsGroupName
    tenantId: tenantId
  }
}

var isDataKVPresent = !empty(existingDataResources) ? !empty(filter(json(existingDataResources), x => x.Name == keyVaultName && x.ResourceType == 'Microsoft.KeyVault/vaults')) : false

module dataKeyVaultTemplate 'keyVault.bicep' = if(!isDataKVPresent) {
  name: 'dataKeyVaultTemplate'
  params: {
    name: keyVaultName
    skuName: keyVaultSkuName
    skuFamily: keyVaultSkuFamily
    location: location
    tenantId: tenantId
  }
}

module dataKeyVaultPoliciesTemplate 'keyVaultAccessPolicy.bicep' = {
  name: 'dataKeyVaultPoliciesTemplate'
  params: {
    name: keyVaultName
    policies: keyVaultReaders
    tenantId: tenantId
  }
  dependsOn: [
    dataKeyVaultTemplate
  ]
}

module serviceBusTemplate 'serviceBus.bicep' = {
  name: 'serviceBusTemplate'
  params: {
    name: serviceBusName
    sku: serviceBusSku
    location: location
    keyVaultName: keyVaultName
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

module serviceBusMembershipUpdatersTopicTemplate 'serviceBusTopic.bicep' = {
  name: 'serviceBusMembershipUpdatersTopicTemplate'
  params: {
    serviceBusName: serviceBusName
    topicName: serviceBusMembershipUpdatersTopicSubscriptions.topicName
  }
  dependsOn: [
    serviceBusTemplate
  ]
}

module serviceBusMembershipUpdatersSubscriptionsTemplate 'serviceBusSubscription.bicep' = {
  name: 'serviceBusMembershipUpdatersSubscriptionsTemplate'
  params: {
    serviceBusName: serviceBusName
    topicName: serviceBusMembershipUpdatersTopicSubscriptions.topicName
    topicSubscriptions: serviceBusMembershipUpdatersTopicSubscriptions.subscriptions
  }
  dependsOn: [
    serviceBusMembershipUpdatersTopicTemplate
  ]
}

module membershipAggregatorQueue 'serviceBusQueue.bicep' = {
  name: 'membershipAggregatorQueue'
  params: {
    queueName: serviceBusMembershipAggregatorQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn:[
    serviceBusTemplate
  ]
}

module storageAccountTemplate 'storageAccount.bicep' = {
  name: 'storageAccountTemplate'
  params: {
    name: storageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    location: location
  }
  dependsOn:[
    dataKeyVaultPoliciesTemplate
  ]
}

module jobsStorageAccountTemplate 'storageAccount.bicep' = {
  name: 'jobsStorageAccountTemplate'
  params: {
    name: jobsStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    addJobsStorageAccountPolicies: true
    location: location
  }
  dependsOn:[
    dataKeyVaultPoliciesTemplate
  ]
}

module logAnalyticsTemplate 'logAnalytics.bicep' = {
  name: 'logAnalyticsTemplate'
  params: {
    name: logAnalyticsName
    sku: logAnalyticsSku
    location: location
    keyVaultName: keyVaultName
  }
}

module appInsightsTemplate 'applicationInsights.bicep' = {
  name: 'appInsightsTemplate'
  params: {
    name: appInsightsName
    location: location
    kind: appInsightsKind
    workspaceId: logAnalyticsTemplate.outputs.resourceId
    keyVaultName: keyVaultName
  }
  dependsOn: [
    logAnalyticsTemplate
  ]
}

module appConfigurationTemplate 'appConfiguration.bicep' = {
  name: 'appConfigurationTemplate'
  params: {
    configStoreName: appConfigurationName
    appConfigurationSku: appConfigurationSku
    location: location
    appConfigurationKeyData: appConfigurationKeyData
    featureFlags: appConfigurationfeatureFlags
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
  dependsOn: [
    logAnalyticsTemplate
    actionGroupTemplate
  ]
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
        name: 'jobsStorageAccountName'
        value: jobsStorageAccountName
      }
      {
        name: 'membershipContainerName'
        value: membershipContainerName
      }
      {
        name: 'jobsTableName'
        value: jobsTableName
      }
      {
        name: 'notificationsTableName'
        value: notificationsTableName
      }
      {
        name: 'appInsightsAppId'
        value: appInsightsTemplate.outputs.appId
      }
      {
        name: 'serviceBusNamespace'
        value: serviceBusName
      }
      {
        name: 'serviceBusSyncJobTopic'
        value: serviceBusTopicName
      }
      {
        name: 'serviceBusMembershipUpdatersTopic'
        value: serviceBusMembershipUpdatersTopicSubscriptions.topicName
      }
      {
        name: 'logAnalyticsCustomerId'
        value: logAnalyticsTemplate.outputs.customerId
      }
      {
        name: 'notifierProviderId'
        value: notifierProviderId
      }
      {
        name: 'serviceBusMembershipAggregatorQueue'
        value: serviceBusMembershipAggregatorQueue
      }
    ]
  }
  dependsOn: [
    dataKeyVaultTemplate
    storageAccountTemplate
    jobsStorageAccountTemplate
    serviceBusTemplate
    logAnalyticsTemplate
    appInsightsTemplate
  ]
}

module dashboardTemplate 'dashboard.bicep' = {
  name: 'dashboardTemplate'
  params: {
    location: location
    dashboardName: 'GMM Dashboard (${environmentAbbreviation})'
    resourceGroup: '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}'
    computeResourceGroup: '${solutionAbbreviation}-${computeResourceGroupClassification}-${environmentAbbreviation}'
    prereqsResourceGroup: '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'
    subscriptionId: subscriptionId
    jobsStorageAccountName: jobsStorageAccountName
  }
  dependsOn: [
    jobsStorageAccountTemplate
  ]
}

output storageAccountName string = storageAccountName
output serviceBusName string = serviceBusName
output serviceBusTopicName string = serviceBusTopicName
output isDataKVPresent bool = isDataKVPresent
