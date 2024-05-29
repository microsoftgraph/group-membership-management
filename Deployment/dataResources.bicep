param location string
param environmentAbbreviation string
param solutionAbbreviation string
param notifierProviderId string
param serviceBusMembershipUpdatersTopicSubscriptions object
param serviceBusTopicSubscriptions array
param sqlAdministratorsGroupId string
param sqlAdministratorsGroupName string
param sqlSkuCapacity int
param sqlSkuFamily string
param sqlSkuName string
param sqlSkuTier string
param tenantId string
param authenticationType string
param skipMailNotifications bool
param isMailApplicationPermissionGranted bool

//data resources
module dataInfrastructureTemplate '../Infrastructure/data/template.bicep' = {
  name: 'dataInfrastructureResources'
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    notifierProviderId: notifierProviderId
    serviceBusMembershipUpdatersTopicSubscriptions: serviceBusMembershipUpdatersTopicSubscriptions
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
