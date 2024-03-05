// common parameters
param location string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param managedResourceGroupName string
param isManagedApplication bool
param appConfigurationName string

// UI parameters
param customDomainName string
param apiAppClientId string
param apiServiceBaseUri string
param uiAppTenantId string
param uiAppClientId string
param sharepointDomain string
param tenantDomain string
param uiLocation string

// API parameters
param pipeline string

var prereqsResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-prereqs-${environmentAbbreviation}'
var dataResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-data-${environmentAbbreviation}'
var computeResourceGroupName = isManagedApplication ? managedResourceGroupName : '${solutionAbbreviation}-compute-${environmentAbbreviation}'

// function resources
// ----------------- JobTrigger
module jobTriggerDataResources '../Service/GroupMembershipManagement/Hosts/JobTrigger/Infrastructure/data/template.bicep' = {
  name: 'jobTriggerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module jobTriggerComputeResources '../Service/GroupMembershipManagement/Hosts/JobTrigger/Infrastructure/compute/template.bicep' = {
  name: 'jobTriggerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    jobTriggerDataResources
  ]
}

// ----------------- DestinationAttributesUpdater
module destinationAttributesUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/destinationAttributesUpdater/Infrastructure/data/template.bicep' = {
  name: 'destinationAttributesUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module destinationAttributesUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/destinationAttributesUpdater/Infrastructure/compute/template.bicep' = {
  name: 'destinationAttributesUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    destinationAttributesUpdaterDataResources
  ]
}

// ----------------- GroupMembershipObtainer
module groupMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/GroupMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'groupMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module groupMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/GroupMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'groupMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    groupMembershipObtainerDataResources
  ]
}

// ----------------- SqlMembershipObtainer
module sqlMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/SqlMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'sqlMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module sqlMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/SqlMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'sqlMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    authority: 'https://login.windows.net/${tenantId}'
    subscriptionId: subscription().subscriptionId
    sqlMembershipStorageAccountName: 'not-used'
    sqlMembershipStorageAccountConnectionString: 'not-used'
    pipeline: pipeline
  }
  dependsOn: [
    groupMembershipObtainerDataResources
  ]
}

// ----------------- GroupOwnershipObtainer
module groupOwnershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/GroupOwnershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'groupOwnershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module groupOwnershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/GroupOwnershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'groupOwnershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    groupOwnershipObtainerDataResources
  ]
}

// ----------------- PlaceMembershipObtainer
module placeMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/PlaceMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'placeMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module placeMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/PlaceMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'placeMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    placeMembershipObtainerDataResources
  ]
}

// ----------------- TeamsChannelMembershipObtainer
module teamsChannelMembershipObtainerDataResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelMembershipObtainer/Infrastructure/data/template.bicep' = {
  name: 'teamsChannelMembershipObtainerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module teamsChannelMembershipObtainerComputeResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelMembershipObtainer/Infrastructure/compute/template.bicep' = {
  name: 'teamsChannelMembershipObtainerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    teamsChannelMembershipObtainerDataResources
  ]
}

// ----------------- MembershipAggregator
module membershipAggregatorDataResources '../Service/GroupMembershipManagement/Hosts/MembershipAggregator/Infrastructure/data/template.bicep' = {
  name: 'membershipAggregatorDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module membershipAggregatorComputeResources '../Service/GroupMembershipManagement/Hosts/MembershipAggregator/Infrastructure/compute/template.bicep' = {
  name: 'membershipAggregatorComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    membershipAggregatorDataResources
  ]
}

// ----------------- GraphUpdater
module graphUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/data/template.bicep' = {
  name: 'graphUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module graphUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/compute/template.bicep' = {
  name: 'graphUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    graphUpdaterDataResources
  ]
}

// ----------------- TeamsChannelUpdater
module teamsChannelUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelUpdater/Infrastructure/data/template.bicep' = {
  name: 'teamsChannelUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module teamsChannelUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelUpdater/Infrastructure/compute/template.bicep' = {
  name: 'teamsChannelUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    teamsChannelUpdaterDataResources
  ]
}

// ----------------- NonProdService
module nonProdServiceDataResources '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/data/template.bicep' = {
  name: 'nonProdServiceDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module nonProdServiceComputeResources '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/compute/template.bicep' = {
  name: 'nonProdServiceComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    appConfigurationName: appConfigurationName
  }
  dependsOn: [
    nonProdServiceDataResources
  ]
}

// ----------------- AzureUserReader
module azureUserReaderDataResources '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/data/template.bicep' = {
  name: 'azureUserReaderDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module azureUserReaderComputeResources '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/compute/template.bicep' = {
  name: 'azureUserReaderComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    storageAccountSecretName: 'storageAccountConnectionString'
  }
  dependsOn: [
    azureUserReaderDataResources
  ]
}

// ----------------- Notifier
module notifierDataResources '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/data/template.bicep' = {
  name: 'notifierDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module notifierComputeResources '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/compute/template.bicep' = {
  name: 'notifierComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notUsed'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    notifierDataResources
  ]
}

// ----------------- JobScheduler
module jobSchedulerDataResources '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/data/template.bicep' = {
  name: 'jobSchedulerDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
  }
}

module jobSchedulerComputeResources '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/compute/template.bicep' = {
  name: 'jobSchedulerComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    storageAccountName: 'notused'
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
  }
  dependsOn: [
    jobSchedulerDataResources
  ]
}

// web api
module webApiDataResources '../Service/GroupMembershipManagement/Hosts/WebApi/Infrastructure/data/template.bicep' = {
  name: 'webApiDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module webApiComputeResources '../Service/GroupMembershipManagement/Hosts/WebApi/Infrastructure/compute/template.bicep' = {
  name: 'webApiComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    location: location
    prereqsResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    adfPipeline: pipeline
  }
  dependsOn: [
    sqlMembershipObtainerComputeResources
    webApiDataResources
  ]
}

module uiComputeResources '../Service/GroupMembershipManagement/Hosts/UI/Infrastructure/compute/template.bicep' = {
  name: 'uiComputeResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: uiLocation
    branch: 'not-set'
    repositoryUrl: 'https://url'
    customDomainName: customDomainName
    apiAppClientId: apiAppClientId
    apiServiceBaseUri: apiServiceBaseUri
    uiAppTenantId: uiAppTenantId
    uiAppClientId: uiAppClientId
    sharepointDomain: sharepointDomain
    tenantDomain: tenantDomain
    dataResourceGroupName: dataResourceGroupName
    computeResourceGroupName: computeResourceGroupName
    provider: 'Custom'
  }
  dependsOn: [
    webApiComputeResources
  ]
}
