param location string
param environmentAbbreviation string
param solutionAbbreviation string
param notifierProviderId string
param sqlAdministratorsGroupId string
param sqlAdministratorsGroupName string
param sqlSkuCapacity int = 4
param sqlSkuFamily string = 'Gen5'
param sqlSkuName string = 'GP_S_Gen5'
param sqlSkuTier string = 'GeneralPurpose'
param tenantId string
param authenticationType string
param skipMailNotifications bool
param isMailApplicationPermissionGranted bool
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

//data resources
module dataInfrastructureTemplate '../Infrastructure/data/template.bicep' = {
  name: 'dataInfrastructureResources'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    notifierProviderId: notifierProviderId
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
  }
}
