param location string
param aiLocation string
param environmentAbbreviation string
param solutionAbbreviation string
param notifierProviderId string
param oamEntraAppId string
param oamEntraAppScope string
param sqlAdministratorsGroupId string
param sqlAdministratorsGroupName string
param sqlSkuCapacity int = 4
param sqlSkuFamily string = 'Gen5'
param sqlSkuName string = 'GP_S_Gen5'
param sqlSkuTier string = 'GeneralPurpose'
param tenantId string
param authenticationType string
param isProduction bool = false
param notificationAlertThreshold int = 10
param skipMailNotifications bool = false
param isMailApplicationPermissionGranted bool = false
param isTeamsChannelApplicationPermissionGranted bool = false
param featureFlags object = {
  enableOpenAI: false
}
param emailReceivers array = [
  {
    name: 'Example name'
    emailAddress: 'example@microsoft.com'
    useCommonAlertSchema: true
  }
]
param serviceBusTopicSubscriptions array = [
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
param appConfigurationKeyData array = []
var defaultAppConfigurationKeyData = [
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
    key: 'AutoApprover:IsEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'AutoApprover'
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
    key: 'AzureMaintenance:JobHistoryRetentionDays'
    value: 30
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
    key: 'PendingConfiguration:IsEnabled'
    value: false
    contentType: 'boolean'
    tag: {
      tag1: 'PendingConfiguration'
    }
  }
]
var resolvedAppConfigurationKeyData = empty(appConfigurationKeyData) ? defaultAppConfigurationKeyData : appConfigurationKeyData
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

//data resources
module dataInfrastructureTemplate '../Infrastructure/data/template.bicep' = {
  name: 'dataInfrastructureResources'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    notifierProviderId: notifierProviderId
    oamEntraAppId: oamEntraAppId
    oamEntraAppScope: oamEntraAppScope
    serviceBusTopicSubscriptions: serviceBusTopicSubscriptions
    sqlAdministratorsGroupId: sqlAdministratorsGroupId
    sqlAdministratorsGroupName: sqlAdministratorsGroupName
    sqlSkuCapacity: sqlSkuCapacity
    sqlSkuFamily: sqlSkuFamily
    sqlSkuName: sqlSkuName
    sqlSkuTier: sqlSkuTier
    tenantId: tenantId
    authenticationType: authenticationType
    skipMailNotifications: skipMailNotifications
    isMailApplicationPermissionGranted: isMailApplicationPermissionGranted
    isTeamsChannelApplicationPermissionGranted: isTeamsChannelApplicationPermissionGranted
    emailReceivers: emailReceivers
    appConfigurationKeyData: resolvedAppConfigurationKeyData
    notificationAlertThreshold: notificationAlertThreshold
    isProduction: isProduction
    availableMembershipUpdaters: availableMembershipUpdaters
    aiLocation: aiLocation
    featureFlags: featureFlags
  }
}
