targetScope = 'subscription'

param location string
param tenantId string
param solutionAbbreviation string
param environmentAbbreviation string
param isManagedApplication bool
param managedResourceGroupName string
param appConfigurationName string
param authenticationType string
param setRBACPermissions bool
param skipMailNotifications bool
param isMailApplicationPermissionGranted bool

// prereqs parameters
// parameters for prereqs key vault
@secure()
param senderPassword string
param senderUsername string
param supportEmailAddresses string
param syncDisabledCCEmailAddresses string
param syncCompletedCCEmailAddresses string
param teamsChannelServiceAccountObjectId string
@secure()
param teamsChannelServiceAccountPassword string
param teamsChannelServiceAccountUsername string

// data parameters
param notifierProviderId string
param serviceBusMembershipUpdatersTopicSubscriptions object
param serviceBusTopicSubscriptions array
param sqlAdministratorsGroupId string
param sqlAdministratorsGroupName string
param sqlSkuCapacity int
param sqlSkuFamily string
param sqlSkuName string
param sqlSkuTier string

// UI parameters
param customDomainName string
param apiAppClientId string
param apiServiceBaseUri string
param uiAppTenantId string
param uiAppClientId string
param sharepointDomain string
param tenantDomain string
param uiLocation string

// ADF parameters
param pipeline string
param skipADFDeployment bool = false

var prereqsResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-data-${environmentAbbreviation}'
var computeResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-compute-${environmentAbbreviation}'

// prereqs key vault
var prereqsKeyVaultName = '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var prereqsKeyVaultSkuName = 'standard'
var prereqsKeyVaultSkuFamily = 'A'

// prereq resources
module prereqResources 'prereqResources.bicep' = {
  name: 'prereqResourcesTemplate'
  scope: resourceGroup(prereqsResourceGroupName)
  params: {
    prereqsKeyVaultName: prereqsKeyVaultName
    prereqsKeyVaultSkuName: prereqsKeyVaultSkuName
    prereqsKeyVaultSkuFamily: prereqsKeyVaultSkuFamily
    location: location
    tenantId: tenantId
    senderPassword: senderPassword
    senderUsername: senderUsername
    supportEmailAddresses: supportEmailAddresses
    syncCompletedCCEmailAddresses: syncCompletedCCEmailAddresses
    syncDisabledCCEmailAddresses: syncDisabledCCEmailAddresses
    teamsChannelServiceAccountObjectId: teamsChannelServiceAccountObjectId
    teamsChannelServiceAccountPassword: teamsChannelServiceAccountPassword
    teamsChannelServiceAccountUsername: teamsChannelServiceAccountUsername
  }
}

// data resources
module dataResources 'dataResources.bicep' = {
  name: 'dataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
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
  dependsOn: [
    prereqResources
  ]
}

// compute resources
module computeResources 'computeResources.bicep' = {
  name: 'computeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    uiLocation: uiLocation
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    managedResourceGroupName: managedResourceGroupName
    isManagedApplication: isManagedApplication
    customDomainName: customDomainName
    apiAppClientId: apiAppClientId
    apiServiceBaseUri: apiServiceBaseUri
    uiAppTenantId: uiAppTenantId
    uiAppClientId: uiAppClientId
    sharepointDomain: sharepointDomain
    tenantDomain: tenantDomain
    pipeline: pipeline
    appConfigurationName: appConfigurationName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    dataResources
  ]
}

// ADF HR Resources
module adfHRResources 'adfHRResources.bicep' = if (!skipADFDeployment) {
  name: 'adfHRResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
  }
  dependsOn: [
    computeResources
  ]
}
