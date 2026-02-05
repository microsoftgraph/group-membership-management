type topicSubscription = {
  topicName: string
  subscriptionName: string
  ruleName: string
  ruleSqlExpression: string
  sessionEnabled: bool?
}

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

@description('Whether or not this environment is a production environment that needs to have authorization locks.')
param isProduction bool = false

@description('Subscription Id for the environment')
param subscriptionId string = subscription().subscriptionId

@description('Tenant id.')
param tenantId string

@description('SQL SKU Name')
param sqlSkuName string = 'GP_S_Gen5'

@description('SQL SKU Tier')
param sqlSkuTier string = 'GeneralPurpose'

@description('SQL SKU Family')
param sqlSkuFamily string = 'Gen5'

@description('SQL SKU Capacity')
param sqlSkuCapacity int = 4

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

@description('Enter service bus topic\'s subscriptions.')
param serviceBusTopicSubscriptions topicSubscription[] = [
  {
    topicName: 'membershipUpdaters'
    subscriptionName: 'GraphUpdater'
    ruleName: 'updaterType'
    ruleSqlExpression: 'Type = \'GroupMembership\''
  }
  {
    topicName: 'membershipUpdaters'
    subscriptionName: 'TeamsChannelUpdater'
    ruleName: 'updaterType'
    ruleSqlExpression: 'Type = \'TeamsChannelMembership\''
  }
  {
    topicName: 'syncJobs'
    subscriptionName: 'PlaceMembership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'PlaceMembership\''
  }
  {
    topicName: 'syncJobs'
    subscriptionName: 'GroupMembership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'GroupMembership\''
  }
  {
    topicName: 'syncJobs'
    subscriptionName: 'TeamsChannelMembership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'TeamsChannelMembership\''
  }
  {
    topicName: 'syncJobs'
    subscriptionName: 'GroupOwnership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'GroupOwnership\''
  }
  {
    topicName: 'syncJobs'
    subscriptionName: 'SqlMembership'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'SqlMembership\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Small'
    ruleName: 'jobSize'
    ruleSqlExpression: 'LaneSize = \'Small\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Large'
    ruleName: 'jobSize'
    ruleSqlExpression: 'LaneSize = \'Large\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Pending_Small'
    ruleName: 'pending_small'
    ruleSqlExpression: 'MessageType = \'pending_small\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Pending_Large'
    ruleName: 'pending_large'
    ruleSqlExpression: 'MessageType = \'pending_large\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Completion_Small'
    ruleName: 'completion_small'
    ruleSqlExpression: 'MessageType = \'completion_small\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'Completion_Large'
    ruleName: 'completion_large'
    ruleSqlExpression: 'MessageType = \'completion_large\''
  }
  {
    topicName: 'messageSplitter'
    subscriptionName: 'LeaseRenew_Large'
    ruleName: 'lease_renew_large'
    ruleSqlExpression: 'MessageType = \'lease_renew_large\''
  }
  {
    topicName: 'membershipUpdaters'
    subscriptionName: 'GraphUpdater_small_1'
    ruleName: 'GraphUpdater_small_rule'
    ruleSqlExpression: 'Type = \'groupmembership_small_1\''
  }
  {
    topicName: 'membershipUpdaters'
    subscriptionName: 'GraphUpdater_large_1'
    ruleName: 'GraphUpdater_large_rule'
    ruleSqlExpression: 'Type = \'groupmembership_large_1\''
    sessionEnabled: true
  }
]

@description('Enter membership aggregator service bus queue name')
param serviceBusMembershipAggregatorQueue string = 'membershipAggregator'

@description('Enter notifications service bus queue name')
param serviceBusNotificationsQueue string = 'notifications'

@description('Enter notifications service bus queue name')
param serviceBusFailedNotificationsQueue string = 'failedNotifications'

@description('Enter job finalizer service bus queue name')
param serviceBusSyncJobUpdaterQueue string = 'syncJobUpdater'

@description('Enter pending configuration service bus queue name')
param serviceBusConfigurationQueue string = 'configuration'

@description('Enter failed pending configuration service bus queue name')
param serviceBusFailedConfigurationQueue string = 'failedConfiguration'

@description('Available membership updaters')
param availableMembershipUpdaters array = [
  {
    name: 'GroupMembership'
    lanes: [
      {
        name: 'small'
        instances: 1
        messageSize: 400
      }
      {
        name: 'large'
        instances: 1
        messageSize: 400
      }
    ]
  }
]

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

@allowed([
  'UserAssignedManagedIdentity'
  'ClientSecret'
  'Certificate'
])
param authenticationType string = 'ClientSecret'
param skipMailNotifications bool = false
param isMailApplicationPermissionGranted bool = false
param isTeamsChannelApplicationPermissionGranted bool = false

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
    key: 'JobTrigger:JobCountThreshold'
    value: '10'
    contentType: 'integer'
    tag: {
      tag1: 'JobTrigger'
    }
  }
  {
    key: 'JobTrigger:JobPerMilleThreshold'
    value: '10'
    contentType: 'integer'
    tag: {
      tag1: 'JobTrigger'
    }
  }
  {
    key: 'MaxExceptionHandlingAttempts'
    value: '2'
    contentType: 'integer'
    tag: {
      tag1: 'RetryPolicy'
    }
  }
  {
    key: 'MaxRetryAfterAttempts'
    value: '4'
    contentType: 'integer'
    tag: {
      tag1: 'RetryPolicy'
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
    key: 'GroupMembershipObtainer:EnableHttpHandlerDiagnosticListener'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'GroupMembershipObtainer'
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
    key: 'NumberOfThresholdViolationsFollowUps'
    value: '3'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
      tag2: 'Notifier'
    }
  }
  {
    key: 'MaximumNumberOfThresholdRecipients'
    value: '3'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
      tag2: 'Notifier'
    }
  }
  {
    key: 'NumberOfThresholdViolationsToNotify'
    value: '2'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
      tag2: 'Notifier'
    }
  }
  {
    key: 'NumberOfThresholdViolationsToDisableJob'
    value: '7'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
      tag2: 'Notifier'
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
    key: 'AzureMaintenance:NumberOfDaysBeforePurging'
    value: 30
    contentType: 'int'
    tag: {
      tag1: 'AzureMaintenance'
    }
  }
  {
    key: 'AzureMaintenance:NumberOfDaysBeforePurgingToSendWarning'
    value: 7
    contentType: 'int'
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
    value: '{"ResetJobs":false,"DaysToAddForReset":0,"DistributeJobs":true,"IncludeFutureJobs":false,"StartTimeDelayMinutes":5,"DelayBetweenSyncsSeconds":5,"DefaultRuntimeSeconds":60,"GetRunTimeFromLogs":true,"RunTimeMetric":"MaxProcessingTime","RunTimeRangeInDays":7,"RuntimeQuery":"AppEvents | where Name == \'SyncComplete\' | project TimeElapsed = todouble(Properties[\'SyncJobTimeElapsedSeconds\']), Destination = tostring(Properties[\'TargetOfficeGroupId\']), RunId = Properties[\'RunId\'], Result = Properties[\'Result\'], DryRun = Properties[\'IsDryRunEnabled\'] | where Result == \'Success\' and DryRun == \'False\' | project TimeElapsed, Destination, RunId | summarize MaxProcessingTime=max(TimeElapsed), AvgProcessingTime=avg(TimeElapsed) by Destination"}'
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
    value: 'true'
    contentType: 'boolean'
    tag: {
      tag1: 'Mail'
    }
  }
  {
    key: 'Mail:ActionableMessageViewerGroupId'
    value: ''
    contentType: 'string'
    tag: {
      tag1: 'Mail'
    }
  }
  {
    key: 'ThresholdNotification:IsThresholdNotificationEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'ThresholdNotification'
    }
  }
  {
    key: 'GraphAPI:AuthenticationType'
    value: authenticationType
    contentType: 'string'
    tag: {
      tag1: 'GraphAPI'
    }
    description: 'Authentication type for Graph API. Possible values: UserAssignedManagedIdentity, ClientSecret, Certificate'
  }
  {
    key: 'Mail:IsMailApplicationPermissionGranted'
    value: isMailApplicationPermissionGranted
    contentType: 'boolean'
    tag: {
      tag1: 'Mail'
    }
  }
  {
    key: 'Mail:SkipMailNotifications'
    value: skipMailNotifications
    contentType: 'boolean'
    tag: {
      tag1: 'Mail'
    }
  }
  {
    key: 'TeamsChannel:IsChannelReadWriteApplicationPermissionGranted'
    value: isTeamsChannelApplicationPermissionGranted
    contentType: 'boolean'
    tag: {
      tag1: 'TeamsChannel'
    }
  }
  {
    key: 'MultiLane:Small'
    value: 400
    contentType: 'integer'
    tag: {
      tag1: 'MultiLane'
    }
    description: 'small: equal or less than value.'
  }
  {
    key: 'MultiLane:IsEnabled'
    value: false
    contentType: 'boolean'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:AvailableMembershipUpdaters'
    value: string(availableMembershipUpdaters)
    contentType: 'string'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Small:RateLimiter:IsEnabled'
    value: true
    contentType: 'boolean'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Small:RateLimiter:MaxInFlightMessages'
    value: 16
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Small:RateLimiter:LeaseTimeoutMinutes'
    value: 5
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Small:RateLimiter:HeartbeatIntervalMinutes'
    value: 0
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Large:RateLimiter:IsEnabled'
    value: true
    contentType: 'boolean'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Large:RateLimiter:MaxInFlightMessages'
    value: 3
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Large:RateLimiter:LeaseTimeoutMinutes'
    value: 15
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'MultiLane:Large:RateLimiter:HeartbeatIntervalMinutes'
    value: 3
    contentType: 'int'
    tag: {
      tag1: 'MultiLane'
    }
  }
  {
    key: 'PendingConfiguration:IsEnabled'
    value: false
    contentType: 'boolean'
    tag: {
      tag1: 'PendingConfiguration'
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

@description('Enter OAM Entra App Id.')
@minLength(0)
@maxLength(36)
param oamEntraAppId string

@description('Enter OAM Entra App Expose and API Scope.')
param oamEntraAppScope string

@description('JSON string with an array listing the existing data resources [{Name: string, ResourceType: string}]')
param existingDataResources string = '[]'

@description('Administrators Azure AD Group Object Id')
param sqlAdministratorsGroupId string

@description('Administrators Azure AD Group Name')
param sqlAdministratorsGroupName string

@description('Failed notifications alert threshold.')
param notificationAlertThreshold int = 10

@description('Location for the OpenAI resource.')
param aiLocation string = location

param featureFlags object = {
  enableOpenAI: false
}

var syncJobsTopicName = 'syncJobs'

module sqlServer 'sqlServer.bicep' = {
  name: 'sqlServerTemplate'
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    isProduction: isProduction
    location: location
    sqlSkuName: sqlSkuName
    sqlSkuTier: sqlSkuTier
    sqlSkuFamily: sqlSkuFamily
    sqlSkuCapacity: sqlSkuCapacity
    sqlAdministratorsGroupId: sqlAdministratorsGroupId
    sqlAdministratorsGroupName: sqlAdministratorsGroupName
    tenantId: tenantId
  }
  dependsOn: [
    dataKeyVaultTemplate
  ]
}

var isDataKVPresent = !empty(existingDataResources)
  ? !empty(filter(
      json(existingDataResources),
      x => x.Name == keyVaultName && x.ResourceType == 'Microsoft.KeyVault/vaults'
    ))
  : false
var graphUserAssignedManagedIdentityName = '${solutionAbbreviation}-identity-${environmentAbbreviation}-Graph'

module dataKeyVaultTemplate 'keyVault.bicep' = if (!isDataKVPresent) {
  name: 'dataKeyVaultTemplate'
  params: {
    name: keyVaultName
    skuName: keyVaultSkuName
    skuFamily: keyVaultSkuFamily
    location: location
    tenantId: tenantId
  }
}

module graphUserAssignedManagedIdentity 'userAssignedIdentity.bicep' = {
  name: 'graphUserAssignedManagedIdentity'
  params: {
    identityName: graphUserAssignedManagedIdentityName
    location: location
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
    logAnalyticsWorkspaceId: logAnalyticsTemplate.outputs.resourceId
  }
  dependsOn: [
    dataKeyVaultTemplate
    logAnalyticsTemplate
  ]
}

var allTopics = [for topic in serviceBusTopicSubscriptions: topic.topicName]
var uniqueTopics = union(allTopics, [])

module serviceBusTopicTemplate 'serviceBusTopic.bicep' = [
  for topic in uniqueTopics: {
    name: '${topic}-Template'
    params: {
      serviceBusName: serviceBusName
      topicName: topic
    }
    dependsOn: [
      serviceBusTemplate
      logAnalyticsTemplate
    ]
  }
]

module serviceBusSubscriptionsTemplate 'serviceBusSubscription.bicep' = [
  for topic in serviceBusTopicSubscriptions: {
    name: '${topic.topicName}-${topic.subscriptionName}-Template'
    params: {
      serviceBusName: serviceBusName
      topicSubscriptions: serviceBusTopicSubscriptions
    }
    dependsOn: [
      serviceBusTopicTemplate
      logAnalyticsTemplate
    ]
  }
]

module membershipAggregatorQueue 'serviceBusQueue.bicep' = {
  name: 'membershipAggregatorQueue'
  params: {
    queueName: serviceBusMembershipAggregatorQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
    logAnalyticsTemplate
  ]
}

module notificationsQueue 'serviceBusQueue.bicep' = {
  name: 'notificationsQueue'
  params: {
    queueName: serviceBusNotificationsQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
    logAnalyticsTemplate
  ]
}

module failedNotificationsQueue 'serviceBusQueue.bicep' = {
  name: 'failedNotificationsQueue'
  params: {
    queueName: serviceBusFailedNotificationsQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
  ]
}

module syncJobUpdaterQueue 'serviceBusQueue.bicep' = {
  name: 'syncJobUpdaterQueue'
  params: {
    queueName: serviceBusSyncJobUpdaterQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
  ]
}

module configurationQueue 'serviceBusQueue.bicep' = {
  name: 'configurationQueue'
  params: {
    queueName: serviceBusConfigurationQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
    logAnalyticsTemplate
  ]
}

module failedConfigurationQueue 'serviceBusQueue.bicep' = {
  name: 'failedConfigurationQueue'
  params: {
    queueName: serviceBusFailedConfigurationQueue
    serviceBusName: serviceBusName
    requiresSession: false
    maxDeliveryCount: 5
  }
  dependsOn: [
    serviceBusTemplate
    logAnalyticsTemplate
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
  dependsOn: [
    dataKeyVaultTemplate
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
  dependsOn: [
    dataKeyVaultTemplate
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
    workspaceId: logAnalyticsTemplate.outputs.resourceId
    keyVaultName: keyVaultName
  }
  dependsOn: [
    dataKeyVaultTemplate
    logAnalyticsTemplate
  ]
}

module openAIResources 'openAIResources.bicep' = if (featureFlags.enableOpenAI) {
  name: 'openAIResources'
  params: {
    aiLocation: aiLocation
    openAIResourceName: '${solutionAbbreviation}-${resourceGroupClassification}-${environmentAbbreviation}-openai'
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    allowedIpAddresses: ''
  }
  dependsOn: [
    logAnalyticsTemplate
  ]
}

var defaultAppConfigurationKeyData = [
  {
    key: 'GraphAPI:GraphAppName'
    value: '${solutionAbbreviation}-Graph-${environmentAbbreviation}'
    contentType: 'string'
    tag: {
      tag1: 'GraphAPI'
    }
    description: 'Name of the application registered in Azure AD for Graph API.'
  }
  {
    key: 'GraphAPI:GraphUAMIName'
    value: '${solutionAbbreviation}-identity-${environmentAbbreviation}-graph'
    contentType: 'string'
    tag: {
      tag1: 'GraphAPI'
    }
    description: 'Name of the user assigned managed identity for Graph API.'
  }
]

module appConfigurationTemplate 'appConfiguration.bicep' = {
  name: 'appConfigurationTemplate'
  params: {
    configStoreName: appConfigurationName
    appConfigurationSku: appConfigurationSku
    location: location
    appConfigurationKeyData: union(appConfigurationKeyData, defaultAppConfigurationKeyData)
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

var baseSecrets = [
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
    name: 'appInsightsAppId'
    value: appInsightsTemplate.outputs.appId
  }
  {
    name: 'serviceBusNamespace'
    value: serviceBusName
  }
  {
    name: 'serviceBusSyncJobTopic'
    value: syncJobsTopicName
  }
  {
    name: 'serviceBusMembershipUpdatersTopic'
    value: 'membershipUpdaters'
  }
  {
    name: 'serviceBusMessageSplitterTopic'
    value: 'messageSplitter'
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
    name: 'oamEntraAppId'
    value: oamEntraAppId
  }
  {
    name: 'oamEntraAppScope'
    value: oamEntraAppScope
  }
  {
    name: 'serviceBusMembershipAggregatorQueue'
    value: serviceBusMembershipAggregatorQueue
  }
  {
    name: 'serviceBusNotificationsQueue'
    value: serviceBusNotificationsQueue
  }
  {
    name: 'serviceBusFailedNotificationsQueue'
    value: serviceBusFailedNotificationsQueue
  }
  {
    name: 'serviceBusSyncJobUpdaterQueue'
    value: serviceBusSyncJobUpdaterQueue
  }
  {
    name: 'serviceBusConfigurationQueue'
    value: serviceBusConfigurationQueue
  }
  {
    name: 'serviceBusFailedConfigurationQueue'
    value: serviceBusFailedConfigurationQueue
  }
  {
    name: 'graphUserAssignedManagedIdentityName'
    value: graphUserAssignedManagedIdentityName
  }
  {
    name: 'graphUserAssignedManagedIdentityClientId'
    value: graphUserAssignedManagedIdentity.outputs.clientId
  }
]

var openAISecrets = featureFlags.enableOpenAI ? [
  {
    name: 'openAIEndpoint'
    value: openAIResources.outputs.openAIEndpoint
  }
] : []

var allSecrets = union(baseSecrets, openAISecrets)

module secretsTemplate 'keyVaultSecrets.bicep' = {
  name: 'secretsTemplate'
  params: {
    keyVaultName: keyVaultName
    keyVaultParameters: allSecrets
  }
  dependsOn: [
    dataKeyVaultTemplate
    storageAccountTemplate
    jobsStorageAccountTemplate
    serviceBusTemplate
    logAnalyticsTemplate
    appInsightsTemplate
    graphUserAssignedManagedIdentity
    openAIResources
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

module serviceBusQueueAlert 'serviceBusQueueAlert.bicep' = {
  name: 'serviceBusQueueAlert'
  params: {
    serviceBusNamespaceId: resourceId('Microsoft.ServiceBus/namespaces', serviceBusName)
    serviceBusQueueName: serviceBusFailedNotificationsQueue
    actionGroupId: actionGroupTemplate.outputs.actionGroupId
    threshold: notificationAlertThreshold
  }
  dependsOn: [
    appConfigurationTemplate
    actionGroupTemplate
    failedNotificationsQueue
  ]
}

var nspName = '${solutionAbbreviation}-nsp-${environmentAbbreviation}'
var prereqsResourceGroupName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'
var prereqsKeyVaultName = '${solutionAbbreviation}-${prereqsResourceGroupClassification}-${environmentAbbreviation}'

// Deploy Network Security Perimeter, Profiles, and Resource Associations
module prereqsNetworkSecurityPerimeterTemplate 'networkSecurityPerimeter.bicep' = {
  name: 'prereqsNetworkSecurityPerimeterTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    nspLocation: location
  }
}

module prereqsNetworkSecurityPerimeterProfilesTemplate 'networkSecurityPerimeterProfiles.bicep' = {
  name: 'prereqsNetworkSecurityPerimeterProfilesTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    nspProfileNames: [
      'keyvault'
      'sql'
      'storageaccount'
    ]
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterTemplate
  ]
}

module nspDataKeyVaultAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspDataKeyVaultAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'keyvault'
    resourceId: resourceId('Microsoft.KeyVault/vaults', keyVaultName)
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

module nspPrereqsKeyVaultAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspPrereqsKeyVaultAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'keyvault'
    resourceId: resourceId(subscription().subscriptionId, prereqsResourceGroupName, 'Microsoft.KeyVault/vaults', prereqsKeyVaultName)
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

module nspSqlServerAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspSqlServerAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'sql'
    resourceId: sqlServer.outputs.sqlServerId
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

module nspReplicaSqlServerAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspReplicaSqlServerAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'sql'
    resourceId: sqlServer.outputs.replicaSqlServerId
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

module nspStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: storageAccountTemplate.outputs.storageAccountId
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

module nspJobsStorageAccountAssociationTemplate 'networkSecurityPerimeterResourceAssociation.bicep' = {
  name: 'nspJobsStorageAccountAssociationTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    nspName: nspName
    profileName: 'storageaccount'
    resourceId: jobsStorageAccountTemplate.outputs.storageAccountId
  }
  dependsOn: [
    prereqsNetworkSecurityPerimeterProfilesTemplate
  ]
}

output storageAccountName string = storageAccountName
output serviceBusName string = serviceBusName
output serviceBusTopicName string = syncJobsTopicName
output isDataKVPresent bool = isDataKVPresent
