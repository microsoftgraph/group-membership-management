targetScope = 'subscription'

param location string
param tenantId string
param solutionAbbreviation string
param environmentAbbreviation string
param keyVaultReaders array
param isManagedApplication bool = false
param managedResourceGroupName string = '${solutionAbbreviation}-mrg-${environmentAbbreviation}'
param appConfigurationName string = '${solutionAbbreviation}-appConfig-${environmentAbbreviation}'
param appConfigurationDataOwners array
@description('Must be true for the initial deployment')
param grantAppConfigurationDataOwnersPermission bool = true
@allowed([
  'UserAssignedManagedIdentity'
  'ClientSecret'
  'Certificate'
])
param authenticationType string
// prereqs parameters
// parameters for prereqs key vault
param graphAppCertificateName string = 'not-set'
param graphAppClientId string
@secure()
param graphAppClientSecret string
param graphAppTenantId string
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
param sqlMembershipAppId string
@secure()
param sqlMembershipAppPasswordCredentialValue string
param webapiClientId string
param webApiTenantId string

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
param customDomainName string = ''
param apiAppClientId string
param apiServiceBaseUri string
param uiAppTenantId string
param uiAppClientId string
param sharepointDomain string
param tenantDomain string
param uiLocation string = location

// ADF parameters
param pipeline string = 'ProdPipeline'
param skipADFDeployment bool = false

var prereqsResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-data-${environmentAbbreviation}'
var computeResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-compute-${environmentAbbreviation}'

module gmmResourceGroups 'resourceGroups.bicep' = {
  name: 'resourceGroupsTemplate'
  scope: subscription()
  params: {
    location: location
    prereqsResourceGroupName: prereqsResourceGroupName
    dataResourceGroupName: dataResourceGroupName
    computeResourceGroupName: computeResourceGroupName
    appConfigurationDataOwners: appConfigurationDataOwners
    grantAppConfigurationDataOwnersPermission: grantAppConfigurationDataOwnersPermission
  }
}

module gmmResources 'commonResources.bicep' = {
  name: 'commonResourcesTemplate'
  scope: subscription()
  params: {
    location: location
    uiLocation: uiLocation
    tenantId: tenantId
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    keyVaultReaders: keyVaultReaders
    isManagedApplication: isManagedApplication
    managedResourceGroupName: managedResourceGroupName
    graphAppCertificateName: graphAppCertificateName
    graphAppClientId: graphAppClientId
    graphAppClientSecret: graphAppClientSecret
    graphAppTenantId: graphAppTenantId
    senderPassword: senderPassword
    senderUsername: senderUsername
    supportEmailAddresses: supportEmailAddresses
    syncDisabledCCEmailAddresses: syncDisabledCCEmailAddresses
    syncCompletedCCEmailAddresses: syncCompletedCCEmailAddresses
    teamsChannelServiceAccountObjectId: teamsChannelServiceAccountObjectId
    teamsChannelServiceAccountPassword: teamsChannelServiceAccountPassword
    teamsChannelServiceAccountUsername: teamsChannelServiceAccountUsername
    sqlMembershipAppId: sqlMembershipAppId
    sqlMembershipAppPasswordCredentialValue: sqlMembershipAppPasswordCredentialValue
    webapiClientId: webapiClientId
    webApiTenantId: webApiTenantId
    notifierProviderId: notifierProviderId
    serviceBusMembershipUpdatersTopicSubscriptions: serviceBusMembershipUpdatersTopicSubscriptions
    serviceBusTopicSubscriptions: serviceBusTopicSubscriptions
    sqlAdministratorsGroupId: sqlAdministratorsGroupId
    sqlAdministratorsGroupName: sqlAdministratorsGroupName
    sqlSkuCapacity: sqlSkuCapacity
    sqlSkuFamily: sqlSkuFamily
    sqlSkuName: sqlSkuName
    sqlSkuTier: sqlSkuTier
    customDomainName: customDomainName
    apiAppClientId: apiAppClientId
    apiServiceBaseUri: apiServiceBaseUri
    uiAppTenantId: uiAppTenantId
    uiAppClientId: uiAppClientId
    sharepointDomain: sharepointDomain
    tenantDomain: tenantDomain
    pipeline: pipeline
    skipADFDeployment: skipADFDeployment
    appConfigurationName: appConfigurationName
    authenticationType: authenticationType
  }
  dependsOn: [
    gmmResourceGroups
  ]
}



