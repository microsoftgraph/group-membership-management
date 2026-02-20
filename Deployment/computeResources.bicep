// common parameters
param location string
param aiLocation string
param environmentAbbreviation string
param solutionAbbreviation string
param tenantId string
param functionAuthAppClientId string
param managedResourceGroupName string = ''
param isManagedApplication bool = false
param appConfigurationName string
param setRBACPermissions bool

// UI parameters
param customDomainName string = ''
param apiServiceBaseUri string
param uiLocation string
param branch string = 'not-set'
param repositoryUrl string = 'https://url'

// API parameters
param pipeline string

//WebAPI, Notifier
param apiHostname string = ''
var resolvedApiHostname = apiHostname == '' ? '${solutionAbbreviation}-compute-${environmentAbbreviation}-webapi.azurewebsites.net' : apiHostname

// Message Splitter
param availableMessageSplitterSubscriptions array = [
  {
    name: 's1'
    subscription: 'Small'
  }
  {
    name: 'l1'
    subscription: 'Large'
  }
]

//AzureUserReader
param storageAccountSecretName string = 'adfStorageAccountName'

// GraphUpdater
param concurrentAddRequests int = 1
param concurrentRemoveRequests int = 1


// Used by: JobTrigger, DestinationAttributesUpdater, AzureUserReader, Notifier, JobScheduler, WebApi, NonProdService, GraphUpdater
param featureFlags object = {
  skipListingFunctionAppKeys : true
  enableTeamsChannel: false
  enableOpenAI: false
}

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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    authority: 'https://login.windows.net/${tenantId}'
    subscriptionId: subscription().subscriptionId
    pipeline: pipeline
    setRBACPermissions: setRBACPermissions
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    membershipAggregatorDataResources
  ]
}

// ----------------- GraphUpdater

var guinstanceIds = [
  ''
  'small'
  'large'
]

module graphUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/data/template.bicep' = [for instance in guinstanceIds: {
  name: instance == '' ? 'graphUpdaterDataResourcesTemplate' : 'graphUpdater${instance}DataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    instanceIdentifier: instance
  }
}
]

module graphUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/compute/template.bicep' = [for instance in guinstanceIds: {
  name: instance == '' ? 'graphUpdaterComputeResourcesTemplate' : 'graphUpdater${instance}ComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    instanceIdentifier: instance
    concurrentAddRequests: concurrentAddRequests
    concurrentRemoveRequests: concurrentRemoveRequests
  }
  dependsOn: [
    graphUpdaterDataResources
  ]
}]


// ----------------- TeamsChannelUpdater
module teamsChannelUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/TeamsChannelUpdater/Infrastructure/data/template.bicep' = {
  name: 'teamsChannelUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
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
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
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
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    appConfigurationName: appConfigurationName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
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
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    storageAccountSecretName: storageAccountSecretName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
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
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    apiHostname: resolvedApiHostname
    functionAuthAppClientId: functionAuthAppClientId
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
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    functionAuthAppClientId: functionAuthAppClientId
  }
  dependsOn: [
    jobSchedulerDataResources
  ]
}

// ----------------- SyncJobUpdater
module syncJobUpdaterDataResources '../Service/GroupMembershipManagement/Hosts/SyncJobUpdater/Infrastructure/data/template.bicep' = {
  name: 'syncJobUpdaterDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module syncJobUpdaterComputeResources '../Service/GroupMembershipManagement/Hosts/SyncJobUpdater/Infrastructure/compute/template.bicep' = {
  name: 'syncJobUpdaterComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
  }
  dependsOn: [
    syncJobUpdaterDataResources
  ]
}

// ----------------- MessageSplitter instances
var instanceIds = [
  's1'
  'l1'
]

module messageSplitterDataResources '../Service/GroupMembershipManagement/Hosts/MessageSplitter/Infrastructure/data/template.bicep' = [for instance in instanceIds: {
    name: 'messageSplitter${instance}DataResources'
    scope: resourceGroup(dataResourceGroupName)
    params: {
      location: location
      environmentAbbreviation: environmentAbbreviation
      solutionAbbreviation: solutionAbbreviation
      instanceIdentifier: instance
    }
  }
]

module messageSplitterComputeResources '../Service/GroupMembershipManagement/Hosts/MessageSplitter/Infrastructure/compute/template.bicep' = [for instance in instanceIds: {
  name: 'messageSplitter${instance}ComputeResources'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    functionAuthAppClientId: functionAuthAppClientId
    prereqsKeyVaultResourceGroup: prereqsResourceGroupName
    dataKeyVaultResourceGroup: dataResourceGroupName
    setRBACPermissions: setRBACPermissions
    availableMessageSplitterSubscriptions: availableMessageSplitterSubscriptions
    instanceIdentifier: instance
  }
  dependsOn: [
    messageSplitterDataResources
  ]
}]

// ----------------- AzureMaintenance
module azureMaintenanceDataResources '../Service/GroupMembershipManagement/Hosts/AzureMaintenance/Infrastructure/data/template.bicep' = {
  name: 'azureMaintenanceDataResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
}

module azureMaintenanceComputeResources '../Service/GroupMembershipManagement/Hosts/AzureMaintenance/Infrastructure/compute/template.bicep' = {
  name: 'azureMaintenanceComputeResourcesTemplate'
  scope: resourceGroup(computeResourceGroupName)
  params: {
    location: location
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
    tenantId: tenantId
    setRBACPermissions: setRBACPermissions
    functionAuthAppClientId: functionAuthAppClientId
  }
  dependsOn: [
    azureMaintenanceDataResources
  ]
}

/// Functions Post Compute tasks
module azureUserReaderPostCompute '../Service/GroupMembershipManagement/Hosts/AzureUserReader/Infrastructure/compute/postCompute.bicep' = {
  name: 'azureUserReaderPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    azureUserReaderComputeResources
    messageSplitterComputeResources
  ]
}

module graphUpdaterPostCompute '../Service/GroupMembershipManagement/Hosts/GraphUpdater/Infrastructure/compute/postCompute.bicep' = {
  name: 'graphUpdaterPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    graphUpdaterComputeResources
    messageSplitterComputeResources
  ]
}

module jobSchedulerPostCompute '../Service/GroupMembershipManagement/Hosts/JobScheduler/Infrastructure/compute/postCompute.bicep' = {
  name: 'jobSchedulerPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    jobSchedulerComputeResources
    messageSplitterComputeResources
  ]
}

module nonProdServicePostCompute '../Service/GroupMembershipManagement/Hosts/NonProdService/Infrastructure/compute/postCompute.bicep' = {
  name: 'nonProdServicePostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    nonProdServiceComputeResources
    messageSplitterComputeResources
  ]
}

module notifierPostCompute '../Service/GroupMembershipManagement/Hosts/Notifier/Infrastructure/compute/postCompute.bicep' = {
  name: 'notifierPostCompute'
  params: {
    dataKeyVaultName: '${solutionAbbreviation}-data-${environmentAbbreviation}'
    dataKeyVaultResourceGroup: dataResourceGroupName
    environmentAbbreviation: environmentAbbreviation
    solutionAbbreviation: solutionAbbreviation
  }
  dependsOn:[
    notifierComputeResources
    messageSplitterComputeResources
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
    aiLocation: aiLocation
    prereqsResourceGroup: prereqsResourceGroupName
    dataResourceGroup: dataResourceGroupName
    adfPipeline: pipeline
    setRBACPermissions: setRBACPermissions
    featureFlags: featureFlags
    apiHostname: resolvedApiHostname
  }
  dependsOn: [
    sqlMembershipObtainerComputeResources
    webApiDataResources
    jobSchedulerPostCompute
  ]
}

module uiComputeResources '../Service/GroupMembershipManagement/Hosts/UI/Infrastructure/compute/template.bicep' = {
  name: 'uiComputeResourcesTemplate'
  scope: resourceGroup(dataResourceGroupName)
  params: {
    solutionAbbreviation: solutionAbbreviation
    environmentAbbreviation: environmentAbbreviation
    location: uiLocation
    branch: branch
    repositoryUrl: repositoryUrl
    customDomainName: customDomainName
    apiServiceBaseUri: apiServiceBaseUri
    dataResourceGroupName: dataResourceGroupName
    computeResourceGroupName: computeResourceGroupName
    provider: 'Custom'
  }
  dependsOn: [
    webApiComputeResources
  ]
}
