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
    name: 'SecurityGroup'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'SecurityGroup\''
  }
  {
    name: 'AzureMembershipProvider'
    ruleName: 'syncType'
    ruleSqlExpression: 'Type = \'AzureMembershipProvider\''
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
    key: 'JobTrigger:IsGroupReadWriteAllGranted'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'JobTrigger'
    }
  }
  {
    key: 'SecurityGroup:IsDeltaCacheEnabled'
    value: 'false'
    contentType: 'boolean'
    tag: {
      tag1: 'SecurityGroup'
    }
  }
  {
    key: 'SecurityGroup:IsSecurityGroupDryRunEnabled'
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
    value: '3'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
    }
  }
  {
    key: 'MembershipAggregator:NumberOfThresholdViolationsFollowUps'
    value: '3'
    contentType: 'integer'
    tag: {
      tag1: 'MembershipAggregator'
    }
  }
  {
    key: 'MembershipAggregator:NumberOfThresholdViolationsToDisableJob'
    value: '10'
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

module storageAccountTemplate 'storageAccount.bicep' = {
  name: 'storageAccountTemplate'
  params: {
    name: storageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
  }
}

module jobsStorageAccountTemplate 'storageAccount.bicep' = {
  name: 'jobsStorageAccountTemplate'
  params: {
    name: jobsStorageAccountName
    sku: storageAccountSku
    keyVaultName: keyVaultName
    add30DayDeletionPolicy: true
  }
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
        name: 'logAnalyticsCustomerId'
        value: logAnalyticsTemplate.outputs.customerId
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
